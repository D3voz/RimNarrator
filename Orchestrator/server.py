import os
import uuid
import yaml
import json
import logging
import requests
import io
import time
import soundfile as sf
import numpy as np
from pathlib import Path
from typing import Optional, Dict, List
from datetime import datetime, timedelta
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

# ═══════════════════════════════════════════════════════════════
# CONFIGURATION
# ═══════════════════════════════════════════════════════════════

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    datefmt='%H:%M:%S'
)
logger = logging.getLogger("RimNarrator")

# Load config files
try:
    with open("narrator_config.yaml", "r", encoding="utf-8") as f:
        config = yaml.safe_load(f)
    logger.info("✓ Loaded narrator_config.yaml")
except Exception as e:
    logger.error(f"✗ Failed to load narrator_config.yaml: {e}")
    raise

try:
    voices_path = Path("voices.json")
    if voices_path.exists():
        with open(voices_path, "r", encoding="utf-8") as f:
            voice_map = json.load(f)
        logger.info(f"✓ Loaded {len(voice_map)} voices from voices.json")
    else:
        logger.warning("✗ voices.json not found, creating default")
        voice_map = {"narrator": "voices/narrator.wav"}
        with open(voices_path, "w") as f:
            json.dump(voice_map, f, indent=2)
except Exception as e:
    logger.error(f"✗ Failed to load voices.json: {e}")
    voice_map = {"narrator": "voices/narrator.wav"}

# Setup paths
BASE_DIR = Path(__file__).parent.resolve()
VOICES_DIR = BASE_DIR / "voices"
OUTPUT_DIR = BASE_DIR / config["paths"]["output_folder"]
OUTPUT_DIR.mkdir(exist_ok=True)

# Settings
USE_LLM = config.get("llm", {}).get("enabled", True)
TTS_URL = config["tts"]["api_url"]
LLM_URL = config.get("llm", {}).get("api_base", "http://localhost:1234/v1/chat/completions")
MAX_TEXT_LENGTH = config.get("performance", {}).get("max_text_length", 300)
CLEANUP_INTERVAL = config.get("performance", {}).get("cleanup_interval_hours", 1)
FILE_MAX_AGE = config.get("performance", {}).get("file_max_age_hours", 2)

app = FastAPI(title="RimNarrator Orchestrator", version="2.0")

# ═══════════════════════════════════════════════════════════════
# MODELS
# ═══════════════════════════════════════════════════════════════

class GameEvent(BaseModel):
    text: str = Field(..., min_length=1, max_length=500)
    type: str = Field(default="message", pattern="^(message|letter|social)$")
    voice: str = Field(default="narrator")

class HealthResponse(BaseModel):
    status: str
    voices_loaded: int
    output_dir: str
    llm_enabled: bool

# ═══════════════════════════════════════════════════════════════
# VOICE MANAGEMENT
# ═══════════════════════════════════════════════════════════════

def resolve_voice_path(voice_name: str) -> Optional[str]:
    """
    Resolves a voice name to an absolute path.
    """
    # Check voice_map first
    if voice_name in voice_map:
        voice_path = voice_map[voice_name]
    else:
        voice_path = voice_name
    
    # Convert to Path
    path = Path(voice_path)
    
    # If not absolute, try relative to VOICES_DIR
    if not path.is_absolute():
        path = VOICES_DIR / path
    
    # Validate
    if path.exists() and path.suffix.lower() in ['.wav', '.mp3', '.ogg']:
        abs_path = str(path.resolve())
        logger.info(f"Resolved voice '{voice_name}' -> {abs_path}")
        return abs_path
    
    logger.warning(f"Voice path not found: {voice_name} -> {path}")
    
    # Fallback to default
    default_path = VOICES_DIR / "narrator.wav"
    if default_path.exists():
        return str(default_path.resolve())
    
    return None

def get_available_voices() -> List[str]:
    """Returns list of available voice names"""
    voices = list(voice_map.keys())
    
    # Also scan voices/ folder
    if VOICES_DIR.exists():
        for wav_file in VOICES_DIR.glob("*.wav"):
            name = wav_file.stem
            if name not in voices:
                voices.append(name)
    
    return sorted(voices)

# ═══════════════════════════════════════════════════════════════
# FILE CLEANUP
# ═══════════════════════════════════════════════════════════════

last_cleanup_time = time.time()

def cleanup_old_files():
    """Removes audio files older than FILE_MAX_AGE hours"""
    global last_cleanup_time
    
    now = time.time()
    if now - last_cleanup_time < CLEANUP_INTERVAL * 3600:
        return
    
    last_cleanup_time = now
    cutoff_time = now - (FILE_MAX_AGE * 3600)
    deleted_count = 0
    
    try:
        for audio_file in OUTPUT_DIR.glob("*.wav"):
            if audio_file.stat().st_mtime < cutoff_time:
                audio_file.unlink()
                deleted_count += 1
        
        if deleted_count > 0:
            logger.info(f"🗑️  Cleaned up {deleted_count} old audio files")
    except Exception as e:
        logger.error(f"Cleanup error: {e}")

# ═══════════════════════════════════════════════════════════════
# LLM PROCESSING
# ═══════════════════════════════════════════════════════════════

def generate_dialogue(raw_text: str, event_type: str) -> str:
    """
    Uses LLM to rewrite game logs into narration.
    """
    if not USE_LLM:
        return raw_text
    
    # Skip LLM for short/simple messages
    if event_type != "social" or len(raw_text) < 15:
        return raw_text
    
    system_prompt = (
        "You are a crude, uncensored narrator for an unfiltered colony simulator. "
        "Rewrite the input into one SHORT sentence . (under 15 words). "
        "Rules:\n"
        "1. Be explicit and witty .\n"
        "2. No invented details (locations).\n"
        "3. Keep character names .\n"
        "4. Style: Style: Cynical, deadpan .\n\n"
        "Examples:\n"
        "IN: Chaz commented on salty food to Martinho.\n"
        "OUT: Chaz bitched that the rations were so fucking salty to Martinho.\n\n"
        "IN: Nelson tried to romance Chaz but was rejected.\n"
        "OUT: Nelson tried to get with Chaz but she told him to fuck off.\n\n"
        "IN: Darcie told a joke about eating peas to Nelson.\n"
        "OUT: Darcie cracked a dry joke about the mushy peas, making Nelson chuckle."
    )
    
    payload = {
        "model": "local-model",
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": raw_text}
        ],
        "temperature": 0.7,
        "max_tokens": 50,
        "stop": ["\n"]
    }
    
    try:
        response = requests.post(LLM_URL, json=payload, timeout=10)
        if response.status_code == 200:
            data = response.json()
            content = data['choices'][0]['message']['content'].strip()
            content = content.replace('"', '').replace('*', '')
            
            # Validate output
            if len(content) > 10 and len(content) < 200:
                logger.info(f"LLM: '{raw_text}' -> '{content}'")
                return content
        
        logger.warning(f"LLM failed (status {response.status_code}), using raw text")
    except Exception as e:
        logger.warning(f"LLM error: {e}, using raw text")
    
    return raw_text

# ═══════════════════════════════════════════════════════════════
# TTS PROCESSING
# ═══════════════════════════════════════════════════════════════

def generate_tts(text: str, voice_path: str) -> Optional[Path]:
    """
    Calls TTS API and returns path to generated WAV file.
    """
    # Truncate if too long
    if len(text) > MAX_TEXT_LENGTH:
        text = text[:MAX_TEXT_LENGTH] + "..."
    
    payload = {
        "model": config["tts"]["model"],
        "voice": voice_path,
        "input": text,
        "response_format": "wav",
        "speed": config["tts"].get("speed", 1.0)
    }
    
    logger.info(f"Sending to TTS: '{text}' with voice: {voice_path}")
    
    try:
        response = requests.post(TTS_URL, json=payload, timeout=30)
        
        if response.status_code == 200:
            # Read audio data
            audio_data, samplerate = sf.read(io.BytesIO(response.content))
            
            # Generate unique filename
            filename = f"{uuid.uuid4().hex[:8]}.wav"
            filepath = OUTPUT_DIR / filename
            
            # Save as PCM_16 (Unity-compatible)
            sf.write(str(filepath), audio_data, samplerate, subtype='PCM_16')
            
            abs_path = filepath.resolve()
            logger.info(f"✓ Generated: {filename} at {abs_path}")
            
            return filepath
        else:
            logger.error(f"TTS API error {response.status_code}: {response.text[:200]}")
            return None
            
    except Exception as e:
        logger.error(f"TTS generation failed: {e}")
        return None

# ═══════════════════════════════════════════════════════════════
# API ENDPOINTS
# ═══════════════════════════════════════════════════════════════

@app.get("/health", response_model=HealthResponse)
async def health_check():
    """Health check endpoint"""
    return HealthResponse(
        status="ok",
        voices_loaded=len(voice_map),
        output_dir=str(OUTPUT_DIR),
        llm_enabled=USE_LLM
    )

@app.get("/voices")
async def list_voices():
    """Returns available voices"""
    return {"voices": get_available_voices()}

@app.post("/event")
async def handle_event(event: GameEvent):
    """
    Main endpoint: Receives game event, processes text, generates TTS.
    """
    cleanup_old_files()
    
    logger.info(f"[{event.type}] {event.text[:80]}...")
    
    # Process text with LLM if enabled
    final_text = generate_dialogue(event.text, event.type)
    
    # Resolve voice path
    voice_path = resolve_voice_path(event.voice)
    if not voice_path:
        raise HTTPException(status_code=400, detail=f"Voice not found: {event.voice}")
    
    # Generate TTS
    audio_file = generate_tts(final_text, voice_path)
    
    if audio_file:
        abs_path = str(audio_file.resolve())
        logger.info(f"Returning audio path: {abs_path}")
        
        return {
            "status": "success",
            "audio_path": abs_path,
            "text_processed": final_text
        }
    else:
        raise HTTPException(status_code=500, detail="TTS generation failed")

# ═══════════════════════════════════════════════════════════════
# STARTUP
# ═══════════════════════════════════════════════════════════════

@app.on_event("startup")
async def startup_event():
    logger.info("═" * 50)
    logger.info("RimNarrator Orchestrator v2.0")
    logger.info("═" * 50)
    logger.info(f"TTS:    {TTS_URL}")
    logger.info(f"LLM:    {LLM_URL} ({'ENABLED' if USE_LLM else 'DISABLED'})")
    logger.info(f"Voices: {len(voice_map)} loaded")
    logger.info(f"Output: {OUTPUT_DIR}")
    logger.info("═" * 50)
    
    # Validate TTS connection
    try:
        test_response = requests.get(TTS_URL.replace("/v1/audio/speech", ""), timeout=5)
        logger.info("✓ TTS server reachable")
    except:
        logger.warning("✗ TTS server not reachable!")
    
    # Validate LLM connection
    if USE_LLM:
        try:
            requests.get(LLM_URL.replace("/v1/chat/completions", ""), timeout=5)
            logger.info("✓ LLM server reachable")
        except:
            logger.warning("✗ LLM server not reachable!")

# ═══════════════════════════════════════════════════════════════
# RUN SERVER
# ═══════════════════════════════════════════════════════════════

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        app,
        host=config["server"]["host"],
        port=config["server"]["port"],
        log_level="info"
    )