using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using RimWorld;
using HarmonyLib;
using Newtonsoft.Json;

namespace RimNarrator
{
    // ═══════════════════════════════════════════════════════════════
    // 1. SETTINGS
    // ═══════════════════════════════════════════════════════════════
    public class RimNarratorSettings : ModSettings
    {
        public bool enabled = true;
        public float volume = 0.8f;
        public string serverUrl = "http://127.0.0.1:8000";

        // Social settings
        public bool enableSocial = true;
        public bool only1xSpeed = true;
        public bool dramaOnly = false;
        public bool onlyOnScreen = true;
        public float socialCooldown = 20f;

        // Voice settings
        public string selectedVoice = "narrator";
        public List<string> availableVoices = new List<string> { "narrator" };

        // Performance settings
        public int maxQueueSize = 5;
        public int maxTextLength = 200;
        public bool cleanupOldFiles = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref volume, "volume", 0.8f);
            Scribe_Values.Look(ref serverUrl, "serverUrl", "http://127.0.0.1:8000");
            Scribe_Values.Look(ref enableSocial, "enableSocial", true);
            Scribe_Values.Look(ref only1xSpeed, "only1xSpeed", true);
            Scribe_Values.Look(ref dramaOnly, "dramaOnly", false);
            Scribe_Values.Look(ref onlyOnScreen, "onlyOnScreen", true);
            Scribe_Values.Look(ref socialCooldown, "socialCooldown", 20f);
            Scribe_Values.Look(ref selectedVoice, "selectedVoice", "narrator");
            Scribe_Collections.Look(ref availableVoices, "availableVoices");
            Scribe_Values.Look(ref maxQueueSize, "maxQueueSize", 5);
            Scribe_Values.Look(ref maxTextLength, "maxTextLength", 200);
            Scribe_Values.Look(ref cleanupOldFiles, "cleanupOldFiles", true);

            base.ExposeData();

            if (availableVoices == null || availableVoices.Count == 0)
                availableVoices = new List<string> { "narrator" };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. MAIN MOD CLASS
    // ═══════════════════════════════════════════════════════════════
    [StaticConstructorOnStartup]
    public class RimNarratorMod : Mod
    {
        public static RimNarratorSettings settings;
        private Vector2 scrollPosition;

        public RimNarratorMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<RimNarratorSettings>();

            // Initialize AudioController
            var go = new GameObject("RimNarratorController");
            go.AddComponent<AudioController>();
            UnityEngine.Object.DontDestroyOnLoad(go);

            // Apply Harmony patches
            var harmony = new Harmony("com.rimnarrator.main");
            try
            {
                // Patch Letters
                MethodInfo letterMethod = AccessTools.GetDeclaredMethods(typeof(LetterStack))
                    .FirstOrDefault(m => m.Name == "ReceiveLetter" &&
                                    m.GetParameters().Length >= 1 &&
                                    m.GetParameters()[0].ParameterType == typeof(Letter));
                if (letterMethod != null)
                    harmony.Patch(letterMethod, postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.Postfix_Letter)));

                // Patch Messages
                MethodInfo messageMethod = AccessTools.GetDeclaredMethods(typeof(Messages))
                    .FirstOrDefault(m => m.Name == "Message" &&
                                    m.GetParameters().Length >= 1 &&
                                    m.GetParameters()[0].ParameterType == typeof(Message));
                if (messageMethod != null)
                    harmony.Patch(messageMethod, postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.Postfix_Message)));

                // Patch PlayLog (Social interactions)
                MethodInfo logMethod = AccessTools.Method(typeof(PlayLog), "Add");
                if (logMethod != null)
                    harmony.Patch(logMethod, postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.Postfix_PlayLog)));

                Log.Message("[RimNarrator] Successfully initialized and patched.");
            }
            catch (Exception e)
            {
                Log.Error($"[RimNarrator] Harmony Patch Error: {e}");
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect viewRect = new Rect(0, 0, inRect.width - 20f, 900f);
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            // ─── GENERAL ───
            listing.Label("═══ General ═══");
            listing.CheckboxLabeled("Enable Narration", ref settings.enabled);
            listing.Label($"Volume: {settings.volume:P0}");
            settings.volume = listing.Slider(settings.volume, 0f, 1f);
            listing.Gap();

            // ─── VOICE SELECTION ───
            listing.Label("═══ Voice Selection ═══");
            if (listing.ButtonText($"Selected Voice: {settings.selectedVoice}"))
            {
                List<FloatMenuOption> voiceOptions = new List<FloatMenuOption>();
                foreach (string voice in settings.availableVoices)
                {
                    voiceOptions.Add(new FloatMenuOption(voice, () => settings.selectedVoice = voice));
                }
                Find.WindowStack.Add(new FloatMenu(voiceOptions));
            }

            if (listing.ButtonText("Refresh Voice List"))
            {
                AudioController.Instance?.RefreshVoiceList();
            }
            listing.Gap();

            // ─── SOCIAL INTERACTIONS ───
            listing.Label("═══ Social Interactions ═══");
            listing.CheckboxLabeled("Enable Social Chatter", ref settings.enableSocial);
            if (settings.enableSocial)
            {
                listing.CheckboxLabeled("  Only at 1x Speed", ref settings.only1xSpeed);
                listing.CheckboxLabeled("  Drama Only (No Chitchat)", ref settings.dramaOnly);
                listing.CheckboxLabeled("  Only Visible Pawns", ref settings.onlyOnScreen);
                listing.Label($"  Cooldown: {settings.socialCooldown:F0}s");
                settings.socialCooldown = listing.Slider(settings.socialCooldown, 5f, 120f);
            }
            listing.Gap();

            // ─── PERFORMANCE ───
            listing.Label("═══ Performance ═══");
            listing.Label($"Max Queue Size: {settings.maxQueueSize}");
            settings.maxQueueSize = (int)listing.Slider(settings.maxQueueSize, 1, 20);

            listing.Label($"Max Text Length: {settings.maxTextLength}");
            settings.maxTextLength = (int)listing.Slider(settings.maxTextLength, 50, 500);

            listing.CheckboxLabeled("Auto-Cleanup Old Audio Files", ref settings.cleanupOldFiles);
            listing.Gap();

            // ─── SERVER ───
            listing.Label("═══ Server Configuration ═══");
            listing.Label("Orchestrator URL:");
            settings.serverUrl = listing.TextEntry(settings.serverUrl);

            if (listing.ButtonText("Test Connection"))
            {
                AudioController.Instance?.TestConnection();
            }
            listing.Gap();

            // ─── DEBUG INFO ───
            listing.Label("═══ Status & Debug ═══");
            if (AudioController.Instance != null)
            {
                var controller = AudioController.Instance;
                listing.Label($"Queue: {controller.QueueCount} files waiting");
                listing.Label($"Playing: {(controller.IsPlaying ? "Yes" : "No")}");
                var audioSrc = controller.GetComponent<AudioSource>();
                if (audioSrc != null)
                {
                    listing.Label($"Audio Volume: {audioSrc.volume:F2}");
                    listing.Label($"Audio Muted: {(audioSrc.mute ? "Yes" : "No")}");
                }
                listing.Label($"Last Event: {controller.LastEventTime:F1}s ago");

                if (listing.ButtonText("🔊 Test Beep (Check Audio System)"))
                {
                    controller.PlayTestBeep();
                }

                if (listing.ButtonText("📝 Test Narration (Full Pipeline)"))
                {
                    controller.QueueEvent("This is a test narration to check if everything is working correctly.", "message");
                    Messages.Message("Test narration queued - check console for details", MessageTypeDefOf.NeutralEvent, false);
                }
            }
            else
            {
                listing.Label("AudioController: NOT INITIALIZED");
            }
            listing.Gap();

            listing.End();
            Widgets.EndScrollView();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory() => "RimNarrator";
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. TEXT SANITIZER (Enhanced)
    // ═══════════════════════════════════════════════════════════════
    public static class TextSanitizer
    {
        private static readonly Regex TagRegex = new Regex(@"<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex EmojiRegex = new Regex(@"[\u2600-\u27BF]|[\uE000-\uF8FF]|[\uD83C-\uDBFF\uDC00-\uDFFF]", RegexOptions.Compiled);
        private static readonly Regex SpecialCharsRegex = new Regex(@"[^\w\s\.,!?\-'"":]", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        // Characters that can cause TTS issues
        private static readonly char[] ProblematicChars = { '→', '←', '↑', '↓', '•', '◦', '▪', '▫', '★', '☆' };

        public static string Clean(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            string clean = input;

            // Remove RimWorld tags
            clean = TagRegex.Replace(clean, string.Empty);

            // Remove emojis
            clean = EmojiRegex.Replace(clean, string.Empty);

            // Remove problematic characters
            foreach (char c in ProblematicChars)
                clean = clean.Replace(c.ToString(), "");

            // Remove special symbols (keep basic punctuation)
            clean = SpecialCharsRegex.Replace(clean, string.Empty);

            // Normalize whitespace
            clean = clean.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
            clean = MultiSpaceRegex.Replace(clean, " ");

            // Trim and limit length
            clean = clean.Trim();
            if (clean.Length > RimNarratorMod.settings.maxTextLength)
            {
                clean = clean.Substring(0, RimNarratorMod.settings.maxTextLength);
                // Try to end at a sentence
                int lastPeriod = clean.LastIndexOfAny(new[] { '.', '!', '?' });
                if (lastPeriod > 50)
                    clean = clean.Substring(0, lastPeriod + 1);
                else
                    clean += "...";
            }

            return clean;
        }

        public static bool IsValid(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length < 3) return false;
            if (text.Contains("->") && text.Contains(":")) return false; // Raw log format
            return true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. HARMONY PATCHES
    // ═══════════════════════════════════════════════════════════════
    public static class Patches
    {
        public static void Postfix_Letter(Letter let)
        {
            if (!RimNarratorMod.settings.enabled || let == null) return;

            try
            {
                string body = Traverse.Create(let).Method("GetMouseoverText").GetValue<string>() ?? "";
                string text = $"{let.Label}. {body}";
                string cleaned = TextSanitizer.Clean(text);

                if (TextSanitizer.IsValid(cleaned))
                    AudioController.Instance?.QueueEvent(cleaned, "letter");
            }
            catch (Exception e)
            {
                Log.Warning($"[RimNarrator] Letter patch error: {e.Message}");
            }
        }

        public static void Postfix_Message(Message msg)
        {
            if (!RimNarratorMod.settings.enabled || msg == null) return;

            try
            {
                string cleaned = TextSanitizer.Clean(msg.text);
                if (TextSanitizer.IsValid(cleaned) && cleaned.Length > 10)
                    AudioController.Instance?.QueueEvent(cleaned, "message");
            }
            catch (Exception e)
            {
                Log.Warning($"[RimNarrator] Message patch error: {e.Message}");
            }
        }

        public static void Postfix_PlayLog(LogEntry entry)
        {
            if (!RimNarratorMod.settings.enabled || !RimNarratorMod.settings.enableSocial) return;
            if (entry == null || !(entry is PlayLogEntry_Interaction interaction)) return;

            try
            {
                var controller = AudioController.Instance;
                if (controller == null) return;

                // Check cooldown
                if (Time.time - controller.lastSocialTime < RimNarratorMod.settings.socialCooldown) return;

                // Check speed
                if (RimNarratorMod.settings.only1xSpeed && Find.TickManager.CurTimeSpeed > TimeSpeed.Normal) return;

                var trv = Traverse.Create(interaction);
                Pawn initiator = trv.Field("initiator").GetValue<Pawn>();
                Pawn recipient = trv.Field("recipient").GetValue<Pawn>();
                InteractionDef intDef = trv.Field("intDef").GetValue<InteractionDef>();

                // Drama filter
                if (RimNarratorMod.settings.dramaOnly && intDef != null)
                {
                    string defName = intDef.defName.ToLower();
                    if (defName.Contains("chitchat") || defName.Contains("deeptalk") || defName.Contains("kindwords"))
                        return;
                }

                // Screen position check
                if (RimNarratorMod.settings.onlyOnScreen)
                {
                    if (initiator == null || !initiator.Spawned) return;
                    if (!Find.CameraDriver.CurrentViewRect.Contains(initiator.Position)) return;
                }

                // Extract text
                string bestText = ExtractInteractionText(interaction, initiator, recipient, intDef);
                string cleaned = TextSanitizer.Clean(bestText);

                if (TextSanitizer.IsValid(cleaned))
                    controller.QueueEvent(cleaned, "social");
            }
            catch (Exception e)
            {
                Log.Warning($"[RimNarrator] PlayLog patch error: {e.Message}");
            }
        }

        private static string ExtractInteractionText(PlayLogEntry_Interaction interaction, Pawn initiator, Pawn recipient, InteractionDef intDef)
        {
            // Try POV from initiator
            if (initiator != null)
            {
                string text = interaction.ToGameStringFromPOV(initiator, false);
                if (TextSanitizer.IsValid(text) && !text.Contains("->"))
                    return text;
            }

            // Try POV from recipient
            if (recipient != null)
            {
                string text = interaction.ToGameStringFromPOV(recipient, false);
                if (TextSanitizer.IsValid(text) && !text.Contains("->"))
                    return text;
            }

            // Try tooltip
            string tip = interaction.GetTipString();
            if (!string.IsNullOrEmpty(tip))
            {
                string firstLine = tip.Split('\n')[0];
                if (TextSanitizer.IsValid(firstLine))
                    return firstLine;
            }

            // Fallback to constructed sentence
            if (initiator != null && recipient != null && intDef != null)
                return $"{initiator.LabelShort} {intDef.label} with {recipient.LabelShort}.";

            return "";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. AUDIO CONTROLLER (FIXED - Proper Queue Management)
    // ═══════════════════════════════════════════════════════════════
    public class AudioController : MonoBehaviour
    {
        public static AudioController Instance { get; private set; }

        private AudioSource audioSource;
        private Queue<string> audioPathQueue = new Queue<string>();
        private HashSet<string> recentTexts = new HashSet<string>();
        private bool isPlaying = false;

        public float lastSocialTime = -999f;
        public int QueueCount => audioPathQueue.Count;
        public bool IsPlaying => isPlaying;
        public float LastEventTime => Time.time - lastEventTime;

        private float lastEventTime = 0f;
        private const float DUPLICATE_WINDOW = 3f;
        private const int MAX_RECENT_CACHE = 20;
        private List<string> pendingRequests = new List<string>();

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f; // Force 2D audio (not positional)

            Log.Message("[RimNarrator] AudioController initialized.");
        }

        void OnDestroy()
        {
            if (audioSource != null && audioSource.clip != null)
            {
                Destroy(audioSource.clip);
                audioSource.clip = null;
            }
        }

        void Update()
        {
            if (audioSource != null)
                audioSource.volume = RimNarratorMod.settings.volume;

            // Process queue when not playing and queue has items
            if (!isPlaying && audioPathQueue.Count > 0)
            {
                string path = audioPathQueue.Dequeue();
                StartCoroutine(LoadAndPlayAudio(path));
            }
        }

        public void QueueEvent(string text, string type)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // Check duplicate
            if (recentTexts.Contains(text))
            {
                Log.Message($"[RimNarrator] Skipping duplicate: {text.Substring(0, Math.Min(50, text.Length))}");
                return;
            }

            // Check if we're already processing too many
            if (pendingRequests.Count + audioPathQueue.Count >= RimNarratorMod.settings.maxQueueSize)
            {
                Log.Warning($"[RimNarrator] Queue full ({pendingRequests.Count} pending, {audioPathQueue.Count} queued), dropping event.");
                return;
            }

            // Update tracking
            recentTexts.Add(text);
            if (recentTexts.Count > MAX_RECENT_CACHE)
                recentTexts.Clear();

            lastEventTime = Time.time;
            if (type == "social")
                lastSocialTime = Time.time;

            // Track this request and send to server
            pendingRequests.Add(text);
            Log.Message($"[RimNarrator] Queuing [{type}]: {text.Substring(0, Math.Min(50, text.Length))}...");
            StartCoroutine(SendToServer(text, type));
        }

        private IEnumerator SendToServer(string text, string type)
        {
            string url = $"{RimNarratorMod.settings.serverUrl}/event";

            var payload = new
            {
                text = text,
                type = type,
                voice = RimNarratorMod.settings.selectedVoice
            };

            string json = JsonConvert.SerializeObject(payload);

            using (UnityWebRequest www = new UnityWebRequest(url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                www.downloadHandler = new DownloadHandlerBuffer();
                www.SetRequestHeader("Content-Type", "application/json");
                www.timeout = 30;

                yield return www.SendWebRequest();

                // Remove from pending
                pendingRequests.Remove(text);

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        Log.Message($"[RimNarrator] Server response: {www.downloadHandler.text}");

                        var response = JsonConvert.DeserializeObject<Dictionary<string, string>>(www.downloadHandler.text);
                        if (response != null && response.ContainsKey("audio_path"))
                        {
                            string audioPath = response["audio_path"];

                            Log.Message($"[RimNarrator] Received audio path: {audioPath}");
                            Log.Message($"[RimNarrator] File exists: {File.Exists(audioPath)}");

                            // Validate path before queueing
                            if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
                            {
                                audioPathQueue.Enqueue(audioPath);
                                Log.Message($"[RimNarrator] ✓ Audio ready: {Path.GetFileName(audioPath)} (Queue: {audioPathQueue.Count})");
                            }
                            else
                            {
                                Log.Error($"[RimNarrator] Audio file not found: {audioPath}");
                            }
                        }
                        else
                        {
                            Log.Error($"[RimNarrator] Server response missing 'audio_path': {www.downloadHandler.text}");
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[RimNarrator] Server response parse error: {e.Message}\nResponse: {www.downloadHandler.text}");
                    }
                }
                else
                {
                    Log.Error($"[RimNarrator] Server error ({www.responseCode}): {www.error}");
                }
            }
        }

        private IEnumerator LoadAndPlayAudio(string path)
        {
            isPlaying = true;

            Log.Message($"[RimNarrator] Loading audio: {Path.GetFileName(path)}");

            string uri = "file:///" + path.Replace("\\", "/");

            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV))
            {
                // Disable streaming to fully load file first
                ((DownloadHandlerAudioClip)www.downloadHandler).streamAudio = false;

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip != null && audioSource != null)
                    {
                        // Destroy old clip to prevent memory leak
                        if (audioSource.clip != null)
                        {
                            Destroy(audioSource.clip);
                            audioSource.clip = null;
                        }

                        audioSource.clip = clip;
                        audioSource.volume = RimNarratorMod.settings.volume;
                        audioSource.Play();

                        Log.Message($"[RimNarrator] ♪ Playing: {clip.length:F1}s at volume {audioSource.volume:F2}");

                        // Wait for clip to finish
                        yield return new WaitForSeconds(clip.length + 0.2f);

                        Log.Message($"[RimNarrator] Finished playing.");

                        // Cleanup
                        Destroy(clip);
                        audioSource.clip = null;

                        // Delete file after playing
                        if (RimNarratorMod.settings.cleanupOldFiles)
                        {
                            try
                            {
                                if (File.Exists(path))
                                {
                                    File.Delete(path);
                                    Log.Message($"[RimNarrator] Deleted: {Path.GetFileName(path)}");
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Warning($"[RimNarrator] Could not delete audio file: {e.Message}");
                            }
                        }
                    }
                    else
                    {
                        Log.Error("[RimNarrator] AudioClip is null after download!");
                    }
                }
                else
                {
                    Log.Error($"[RimNarrator] Failed to load audio: {www.error}");
                }
            }

            isPlaying = false;
        }

        // FIXED: Simple test beep using procedural audio generation
        public void PlayTestBeep()
        {
            StartCoroutine(GenerateAndPlayBeep());
        }

        private IEnumerator GenerateAndPlayBeep()
        {
            // Generate a simple 440Hz beep (A note)
            int sampleRate = 44100;
            float frequency = 440f;
            float duration = 0.3f;
            int sampleCount = (int)(sampleRate * duration);

            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / sampleRate) * 0.5f;
            }

            AudioClip beep = AudioClip.Create("TestBeep", sampleCount, 1, sampleRate, false);
            beep.SetData(samples, 0);

            if (audioSource != null)
            {
                audioSource.PlayOneShot(beep);
                Log.Message("[RimNarrator] Playing test beep...");
                Messages.Message("Test beep played! (Did you hear it?)", MessageTypeDefOf.NeutralEvent, false);

                yield return new WaitForSeconds(duration + 0.1f);
                Destroy(beep);
            }
        }

        public void RefreshVoiceList()
        {
            StartCoroutine(FetchVoiceList());
        }

        private IEnumerator FetchVoiceList()
        {
            string url = $"{RimNarratorMod.settings.serverUrl}/voices";

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.timeout = 5;
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(www.downloadHandler.text);
                        if (response != null && response.ContainsKey("voices"))
                        {
                            RimNarratorMod.settings.availableVoices = response["voices"];
                            Log.Message($"[RimNarrator] Refreshed voice list: {response["voices"].Count} voices");
                            Messages.Message($"✓ Voice list refreshed ({response["voices"].Count} voices)", MessageTypeDefOf.PositiveEvent, false);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error($"[RimNarrator] Failed to parse voice list: {e.Message}");
                        Messages.Message($"✗ Failed to parse voices: {e.Message}", MessageTypeDefOf.RejectInput, false);
                    }
                }
                else
                {
                    Log.Error($"[RimNarrator] Voice list fetch failed: {www.error}");
                    Messages.Message($"✗ Connection failed: {www.error}", MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        public void TestConnection()
        {
            StartCoroutine(TestConnectionCoroutine());
        }

        private IEnumerator TestConnectionCoroutine()
        {
            string url = $"{RimNarratorMod.settings.serverUrl}/health";

            Log.Message($"[RimNarrator] Testing connection to {url}...");

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.timeout = 5;
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    Log.Message($"[RimNarrator] ✓ Server connected: {www.downloadHandler.text}");
                    Messages.Message("✓ Server connected!", MessageTypeDefOf.PositiveEvent, false);
                }
                else
                {
                    Log.Error($"[RimNarrator] ✗ Connection failed: {www.error}");
                    Messages.Message($"✗ Connection failed: {www.error}", MessageTypeDefOf.RejectInput, false);
                }
            }
        }
    }
}