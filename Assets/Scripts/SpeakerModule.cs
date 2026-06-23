using ElevenLabs;
using ElevenLabs.Voices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

[System.Serializable]
public class SpeakerModule : MonoBehaviour
{
    public event Action OnRequestCanceled;
    public event Action OnBeginSpeaking;

    // -----------------------------------------------------------------------
    // TTS Provider Selection
    // -----------------------------------------------------------------------
    public enum TTSProvider { ElevenLabs, WindowsLocal }

    [Header("TTS Provider")]
    [Tooltip("ElevenLabs = cloud API (requires credits). WindowsLocal = free built-in Windows SAPI TTS (no internet needed).")]
    public TTSProvider ttsProvider = TTSProvider.ElevenLabs;

    [Header("Windows Local TTS (used when TTSProvider = WindowsLocal)")]
    [Tooltip("Name of the installed Windows voice to use (leave blank for system default). Example: 'Microsoft Zira Desktop'")]
    public string windowsVoiceName = "";
    [Tooltip("Speech rate: -10 (slowest) to +10 (fastest). 0 = normal.")]
    [Range(-10, 10)]
    public int windowsSpeechRate = 0;

    // -----------------------------------------------------------------------
    // Shared
    // -----------------------------------------------------------------------
    public AudioManager AudioManager;
    public bool SplitInputTextToggle;
    [Tooltip("Acceptable time to wait for ElevenLabs to return voice clip. If exceeded, NPC will ask user to repeat.")]
    public int maxResponseWaitTime = 30;

    // -----------------------------------------------------------------------
    // ElevenLabs Configuration
    // -----------------------------------------------------------------------
    [Header("ElevenLabs Configuration")]
    [Tooltip("Drag the ElevenLabsConfiguration asset here from your project window for reliable API key loading.")]
    public ElevenLabsConfiguration elevenLabsConfiguration;

    public ElevenLabsClient client;
    public Voice voice;
    private List<string> audioOutputPaths = new List<string>();
    private ElevenLabs.Models.Model m = new ElevenLabs.Models.Model("eleven_multilingual_v2");

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------
    async void Start() { Init(); }

    public void Init() {
        if (elevenLabsConfiguration != null) {
            client = new ElevenLabsClient(new ElevenLabsAuthentication(elevenLabsConfiguration), new ElevenLabsSettings(elevenLabsConfiguration));
            Debug.Log("[SpeakerModule] ElevenLabs client initialised from configuration asset.");
        } else {
            client = new ElevenLabsClient(ElevenLabsAuthentication.Default);
            Debug.LogWarning("[SpeakerModule] No ElevenLabsConfiguration asset assigned. Falling back to project settings.");
        }
    }

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------
    public void SubmitVoiceRequest(string inputText) {
        if (ttsProvider == TTSProvider.WindowsLocal) {
            RequestAudioLocal(inputText);
        } else {
            if (SplitInputTextToggle) RequestAudioSplitBurst(inputText);
            else RequestAudioFull(inputText);
        }
    }

    // -----------------------------------------------------------------------
    // Windows Local TTS
    // -----------------------------------------------------------------------
    async Task RequestAudioLocal(string text) {
        Debug.Log("[SpeakerModule] Using Windows local TTS.");
        try {
            var clip = await LocalTTSProvider.SynthesiseAsync(text, windowsVoiceName, windowsSpeechRate);
            if (clip != null) {
                AudioManager.AddAudioClip(clip);
                Debug.Log("[SpeakerModule] Local TTS clip queued successfully.");
            } else {
                Debug.LogError("[SpeakerModule] Local TTS returned null — is this a Windows platform?");
                OnRequestCanceled?.Invoke();
            }
        } catch (Exception e) {
            Debug.LogError($"[SpeakerModule] Local TTS failed: {e.Message}");
            OnRequestCanceled?.Invoke();
        }
    }

    // -----------------------------------------------------------------------
    // ElevenLabs — full (single request)
    // -----------------------------------------------------------------------
    async Task RequestAudioFull(string result) {
        if (client == null) { Debug.LogError("[SpeakerModule] ElevenLabs client is null!"); OnRequestCanceled?.Invoke(); return; }
        if (voice  == null) { Debug.LogError("[SpeakerModule] No voice assigned on SpeakerModule!"); OnRequestCanceled?.Invoke(); return; }
        try {
            var _vs       = await client.VoicesEndpoint.GetVoiceSettingsAsync(voice.Id);
            var voiceClip = await client.TextToSpeechEndpoint.TextToSpeechAsync(result, voice, _vs);
            if (voiceClip?.AudioClip != null) {
                AudioManager.AddAudioClip(voiceClip.AudioClip);
                Debug.Log("[SpeakerModule] ElevenLabs clip queued.");
            } else {
                Debug.LogError("[SpeakerModule] ElevenLabs returned a null AudioClip! Check your API key / quota.");
                OnRequestCanceled?.Invoke();
            }
        } catch (Exception e) {
            Debug.LogError($"[SpeakerModule] RequestAudioFull failed: {e.Message}");
            OnRequestCanceled?.Invoke();
        }
    }

    // -----------------------------------------------------------------------
    // ElevenLabs — split burst (parallel per-sentence requests)
    // -----------------------------------------------------------------------
    async Task RequestAudioSplitBurst(string result) {
        if (client == null) { Debug.LogError("[SpeakerModule] ElevenLabs client is null!"); OnRequestCanceled?.Invoke(); return; }
        if (voice  == null) { Debug.LogError("[SpeakerModule] No voice assigned on SpeakerModule!"); OnRequestCanceled?.Invoke(); return; }

        var sentences = SplitInputText(result).ToList();
        VoiceSettings _vs;
        try {
            _vs = await client.VoicesEndpoint.GetVoiceSettingsAsync(voice.Id);
        } catch (Exception e) {
            Debug.LogError($"[SpeakerModule] Failed to get voice settings: {e.Message}");
            OnRequestCanceled?.Invoke();
            return;
        }

        var tasks = new List<Task<VoiceClip>>();
        foreach (string sentence in sentences) {
            var capturedSentence = sentence;
            var sw = new Stopwatch();
            sw.Start();
            var task = client.TextToSpeechEndpoint.TextToSpeechAsync(capturedSentence, voice, _vs, m)
                .ContinueWith(t => {
                    sw.Stop();
                    Debug.Log($"[SpeakerModule] Sentence done in {sw.ElapsedMilliseconds}ms: '{capturedSentence.Substring(0, Mathf.Min(capturedSentence.Length, 30))}...'");

                    if (t.IsFaulted) {
                        Debug.LogError($"[SpeakerModule] ElevenLabs TTS faulted: {t.Exception?.GetBaseException()?.Message}");
                        return (VoiceClip)null;
                    }
                    if (t.IsCanceled) {
                        Debug.LogWarning("[SpeakerModule] ElevenLabs TTS task cancelled.");
                        return (VoiceClip)null;
                    }
                    return t.Result;
                });
            tasks.Add(task);
        }

        var timeout       = Task.Delay(TimeSpan.FromSeconds(maxResponseWaitTime));
        var completedTask = await Task.WhenAny(Task.WhenAll(tasks), timeout);
        if (completedTask == timeout) {
            Debug.LogError("[SpeakerModule] ElevenLabs request timed out.");
            OnRequestCanceled?.Invoke();
            return;
        }

        var results    = await Task.WhenAll(tasks);
        int clipsAdded = 0;
        foreach (var voiceClip in results) {
            if (voiceClip?.AudioClip != null) {
                audioOutputPaths.Add(voiceClip.CachedPath);
                AudioManager.AddAudioClip(voiceClip.AudioClip);
                clipsAdded++;
            } else {
                Debug.LogWarning("[SpeakerModule] A split sentence clip was null (API error or quota exceeded).");
            }
        }

        if (clipsAdded == 0) {
            Debug.LogError("[SpeakerModule] All ElevenLabs clips failed — quota exceeded or API error. Switch TTSProvider to WindowsLocal.");
            OnRequestCanceled?.Invoke();
            return;
        }

        Debug.Log($"[SpeakerModule] {clipsAdded}/{results.Length} clips queued.");
        OnBeginSpeaking?.Invoke();
    }

    // -----------------------------------------------------------------------
    // Utility
    // -----------------------------------------------------------------------
    public string[] SplitInputText(string inputText) {
        string[] sentences = Regex.Split(inputText, @"(?<=[.!?])\s+");
        return sentences.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
    }
}
