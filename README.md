# 🎙️ RimNarrator

**AI-Powered Dynamic Narration for RimWorld**

Transform your RimWorld experience with real-time AI narration. Events, letters, and colonist interactions are spoken aloud using local TTS and optional LLM enhancement.

---

## 📋 Table of Contents

- [Features](#-features)
- [Requirements](#-requirements)
- [Installation](#-installation)
- [Setup Tutorial](#-setup-tutorial)
- [Configuration](#-configuration)
- [Usage](#-usage)
- [FAQ](#-faq)
- [Credits](#-credits)

---

## ✨ Features

### Core Features

- 🔊 **Real-time narration** of game events (letters, messages, social interactions)
- 🤖 **AI text enhancement** - LLM rewrites game logs into natural narration
- 🎭 **Multiple voices** - Swap voices on-the-fly
- ⚡ **Local processing** - No cloud services, runs on your PC
- 🎚️ **Full control** - Volume, cooldowns, filters, and event types

### Smart Filtering

- ✅ Drama-only mode (skip chitchat)
- ✅ On-screen only (narrate visible colonists)
- ✅ Speed-limited (pause narration at 2x/3x speed)
- ✅ Cooldown system (prevent audio spam)
- ✅ Duplicate detection

### Performance

- 🗑️ Automatic audio file cleanup
- 📊 Queue management (prevents memory overflow)
- 🔄 Text sanitization (removes emojis, tags, special chars)
- 💾 Low memory footprint

---

## 📦 Requirements

### RimWorld

- **RimWorld 1.5+** (tested on 1.6)
- **Harmony**

### Python Backend

- **Python 3.9+**
- **FastAPI** - Web server
- **Soundfile** - Audio conversion
- **NumPy** - Audio processing
- **PyYAML** - Config files
- **Requests** - HTTP client

### AI Services (Local)

1. **TTS WebUI** - Text-to-speech generation (<https://github.com/rsxdalv/TTS-WebUI>)

2. **LM Studio** (Optional) - Text enhancement
   - Download: [lmstudio.ai](https://lmstudio.ai/)
   - Any local LLM server works (Oobabooga, Kobold, etc.)

---

## 🚀 Installation

### Step 1: Install the RimWorld Mod

1. Download the mod  - git clone https://github.com/D3voz/RimNarrator
2. Extract to your RimWorld `Mods` folder:

```
C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\RimNarrator\
```

3. Enable in RimWorld mod menu (must be AFTER Harmony)

### Step 2: Install Python Dependencies

Open a terminal in the `RimNarrator/Orchestrator/` folder:

```bash
pip install fastapi uvicorn soundfile numpy pyyaml requests
```

### Step 3: Install TTS WebUI

Follow instructions at: <https://github.com/rsxdalv/TTS-WebUI>

### Step 4: Install LM Studio (Optional)

- Download from lmstudio.ai
- Download a model according to your Vram capacity (e.g. Mistral nemo 12b , Gemma-3 4b etc)
- Load model and start server on port 1234

---

## 📖 Setup Tutorial

### Quick Start (5 Minutes)

#### 1. Prepare Voice Sample

- Record or find a 10-30 second WAV file of the voice you want
- Place in `RimNarrator/Orchestrator/voices/narrator.wav`

#### 2. Start TTS webui

- Install Chatterbox tts extension (first time only)
- Go to Tools → Activate API

#### 3. Start Orchestrator

```bash
# In RimNarrator/Orchestrator folder
python server.py
```

You should see:

```
INFO: ═══════════════════════════════════════════════
INFO: RimNarrator Orchestrator v2.0
INFO: ═══════════════════════════════════════════════
INFO: TTS:    http://localhost:7851/api/tts-generate
INFO: LLM:    http://localhost:1234/v1/chat/completions (ENABLED)
INFO: Voices: 1 loaded
INFO: Output: D:\...\RimNarrator\Orchestrator\audio_cache
INFO: ═══════════════════════════════════════════════
INFO: ✓ TTS server reachable
INFO: Uvicorn running on http://0.0.0.0:8000
```

#### 4. Test in RimWorld

- Launch RimWorld
- Go to Options → Mod Settings → RimNarrator
- Click "Test Connection" → Should show "✓ Server connected!"
- Click "Test Narration" → Should hear audio within 5-10 seconds
- Check the Dev Console (~ key) for logs

---

## ⚙️ Configuration

### In-Game Settings

Open **Options → Mod Settings → RimNarrator**

#### General

| Setting | Description | Default |
|---------|-------------|---------|
| Enable Narration | Master on/off switch | ✅ On |
| Volume | Audio volume (0-100%) | 80% |

#### Voice Selection

| Setting | Description |
|---------|-------------|
| Selected Voice | Choose from available voices |
| Refresh Voice List | Fetch voices from server |

#### Social Interactions

| Setting | Description | Default |
|---------|-------------|---------|
| Enable Social Chatter | Narrate colonist interactions | ✅ On |
| Only at 1x Speed | Disable at 2x/3x speed | ✅ On |
| Drama Only | Skip chitchat/small talk | ❌ Off |
| Only Visible Pawns | Narrate on-screen events only | ✅ On |
| Cooldown | Seconds between social events | 20s |

#### Performance

| Setting | Description | Default |
|---------|-------------|---------|
| Max Queue Size | Max audio files in queue | 5 |
| Max Text Length | Character limit before truncation | 200 |
| Auto-Cleanup | Delete old audio files | ✅ On |

### Server Configuration (narrator_config.yaml)

```yaml
server:
  host: "0.0.0.0"      # Listen on all interfaces
  port: 8000           # Orchestrator port

tts:
  api_url: "http://localhost:7851/api/tts-generate"
  
  # Model name (check your TTS server's API)
  model: "chatterbox"
  
  # Speech speed (1.0 = normal, 1.5 = faster, 0.8 = slower)
  speed: 1.0

llm:
  # Enable AI text enhancement (set to false for raw game text)
  enabled: true
  
  # LM Studio or other OpenAI-compatible endpoint
  api_base: "http://localhost:1234/v1/chat/completions"

paths:
  # Where to save generated audio files
  output_folder: "audio_cache"

performance:
  # Maximum characters sent to TTS (prevents GPU overload)
  max_text_length: 300
  
  # How often to clean old files (hours)
  cleanup_interval_hours: 1
  
  # Delete files older than this (hours)
  file_max_age_hours: 2
```

---

## 🎮 Usage

### What Gets Narrated?

| Event Type | Example | Configurable |
|------------|---------|--------------|
| Letters | "Refugee pod crash landed" | Always on |
| Messages | "Colonist needs rescue" | Always on |
| Social | "Chaz insulted Martinho" | Toggle in settings |

### Fine-Tuning Narration Style

Depends on llm model used. For better llm models, edit the system prompt in `server.py` to whatever you like (make sure not to create emoji or it will crash the tts-webui).

### Multi-Voice Setup

Create character-specific voices:

**1. Prepare voice samples:**

```
voices/
├── narrator.wav       # Default
├── female_young.wav
├── male_gruff.wav
├── raider_angry.wav
└── ai_robotic.wav
```

**2. Update voices.json:**

```json
{
  "narrator": "narrator.wav",
  "young_female": "female_young.wav",
  "gruff_male": "male_gruff.wav",
  "raider": "raider_angry.wav",
  "ai": "ai_robotic.wav"
}
```

**3. Refresh in-game:**

- Open mod settings
- Click "Refresh Voice List"
- Select from dropdown

---

## 🚀 Performance Tips

### For Low-End PCs

```yaml
# narrator_config.yaml
performance:
  max_text_length: 150        # Shorter = faster TTS

tts:
  speed: 1.3                  # Faster playback

# In-game settings
Max Queue Size: 3             # Lower memory usage
Social Cooldown: 30s          # Less frequent events
```

**Disable LLM:**

```yaml
llm:
  enabled: false
```

### For Mid/High-End PCs

```yaml
performance:
  max_text_length: 500
  cleanup_interval_hours: 4

# In-game
Max Queue Size: 10
Social Cooldown: 10s
```

---

## 📁 File Structure

```
RimNarrator/
├── About/
│   └── About.xml               # Mod metadata
├── Assemblies/
│   ├── RimNarrator.dll         # Main mod code
│   ├── 0Harmony.dll
│   └── Newtonsoft.Json.dll
├── Orchestrator/
│   ├── server.py               # FastAPI backend
│   ├── narrator_config.yaml    # Server config
│   ├── voices.json             # Voice mappings
│   ├── voices/
│   │   └── narrator.wav        # Voice samples
│   └── audio_cache/            # Temporary audio files
└── Source/
    └── Main.cs                 # C# source code
```

---

## 🔧 Building from Source

### Requirements

- Visual Studio 2022 or Rider
- .NET Framework 4.7.2
- RimWorld assemblies

### Build steps

1. Clone repository
2. Open `Source/RimNarrator.sln`
3. Add references to RimWorld DLLs:
   - `Assembly-CSharp.dll`
   - `UnityEngine.CoreModule.dll`
   - `UnityEngine.dll`
4. Build → Output to `Assemblies/RimNarrator.dll`

---

## 📚 Dependencies

- **Harmony** - Runtime patching
- **Newtonsoft.Json** - JSON serialization
- **tts-webui** - Text-to-speech generation
- **Soundfile** - Audio I/O

---

## ❓ FAQ

### Q: Why isn't narration working?

1. Check Python server is running (`python server.py`)
2. Verify TTS WebUI is running with API enabled
3. Test connection in mod settings
4. Check RimWorld dev console for errors

### Q: Audio sounds robotic/low quality

- Use higher quality voice samples (16kHz+ WAV files)
- Adjust TTS model settings in TTS WebUI
- Try different TTS models

### Q: Game lags when narrating

- Reduce `max_text_length` in config
- Lower `Max Queue Size` in-game
- Disable LLM enhancement
- Increase social cooldown

### Q: Can I use cloud TTS instead?

Yes! Modify `server.py` to use any TTS API endpoint.

---

## 🙏 Credits

- **TTS**: [TTS-WebUI](https://github.com/rsxdalv/TTS-WebUI)
- **LLM**: [LM Studio](https://lmstudio.ai/)
- **Framework**: RimWorld Harmony

---

## 📝 License

This mod is provided as-is for personal use. RimWorld is © Ludeon Studios.

---

**Enjoy your narrated RimWorld experience! 🎙️**
