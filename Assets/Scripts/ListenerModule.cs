using UnityEngine;
using System;
using Meta.WitAi;
using Meta.WitAi.Dictation;
using UnityEngine.Serialization;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Whisper; 

public enum TranscriptionSource { WitAi, Whisper }

[System.Serializable]
public class ListenerModule : MonoBehaviour {
    public event Action<string> OnUserInputReceived;
    public event Action<string> OnPartialTranscription;
    public event Action OnSpeechStarted; // New event for interruption

    [Header("Transcription Settings")]
    public TranscriptionSource transcriptionSource = TranscriptionSource.WitAi;
    [Tooltip("Leave empty to use the default system microphone.")]
    public string microphoneDeviceName = "";
    [Tooltip("If device name is empty, use this index in the list of devices.")]
    public int microphoneIndex = 0;
    
    [Header("Whisper VAD Settings")]
    [Tooltip("Volume to START recording (Higher = more noise rejection)")]
    public float startThreshold = 0.08f;
    [Tooltip("Volume to CONTINUE recording (Lower = don't cut off speech)")]
    public float stopThreshold = 0.04f;
    [Tooltip("Seconds of silence before processing")]
    public float silenceDuration = 1.5f;

    [Header("Dependencies")]
    [SerializeField] private DictationService witDictation;
    [SerializeField] private WhisperManager whisperManager;
    protected Animator mAnimator;

    private float _currentVolume = 0f;
    private bool _isListeningActive = false; // Is the module allowed to listen?
    private bool _isSpeechDetected = false;  // Is the user currently talking?
    private float _silenceTimer = 0f;
    private float _speechStartTime = 0f;
    
    private float _maxVolInCurrentRecording = 0f;
    private AudioClip _micLoop;
    private string _micName;
    private const int MicBufferSec = 30;
    private const int SampleRate = 16000;

    public void Start() {
        if (transcriptionSource == TranscriptionSource.WitAi) {
            SetupWit();
        } else {
            SetupWhisper();
        }
    }

    private void SetupWhisper() {
        if (Microphone.devices.Length == 0) {
            Debug.LogError("[ListenerModule] No mic found!");
            return;
        }
        
        
        if (!string.IsNullOrEmpty(microphoneDeviceName)) {
            _micName = microphoneDeviceName;
        } else if (microphoneIndex >= 0 && microphoneIndex < Microphone.devices.Length) {
            _micName = Microphone.devices[microphoneIndex];
        } else {
            _micName = Microphone.devices[0];
        }

        _micLoop = Microphone.Start(_micName, true, MicBufferSec, SampleRate);
        
        if (whisperManager == null) whisperManager = FindObjectOfType<WhisperManager>();
        if (whisperManager == null) {
            GameObject go = new GameObject("WhisperManager");
            whisperManager = go.AddComponent<WhisperManager>();
            whisperManager.ModelPath = "ggml-tiny.bin";
        }

        ToggleDictation(true);
        Debug.Log($"[ListenerModule] 🎙 Passive Listening Active on {_micName}. Waiting for speech...");
    }

    private void SetupWit() {
        if (witDictation == null) witDictation = FindObjectOfType<DictationService>();
        if (witDictation == null) return;

        witDictation.DictationEvents.OnFullTranscription.AddListener(HandleFullTranscription);
        witDictation.DictationEvents.OnPartialTranscription.AddListener(HandlePartialTranscription);
        witDictation.DictationEvents.OnMicLevelChanged.AddListener((level) => _currentVolume = level);
        
        if (!witDictation.Active) witDictation.Activate();
    }

    private void HandlePartialTranscription(string text) => OnPartialTranscription?.Invoke(text);

    private void HandleFullTranscription(string text) {
        if (string.IsNullOrWhiteSpace(text)) return;
        Debug.Log($"[ListenerModule] ✅ Final: \"{text}\"");
        OnUserInputReceived?.Invoke(text);
        if (transcriptionSource == TranscriptionSource.WitAi) ToggleDictation(false);
    }

    public void ToggleDictation(bool state) {
        _isListeningActive = state;
        _isSpeechDetected = false;
        _silenceTimer = 0f;

        if (state) Debug.Log("[ListenerModule] 👂 Ears OPEN. Listening...");
        else Debug.Log("[ListenerModule] 🔇 Ears CLOSED. Muted.");

        if (transcriptionSource == TranscriptionSource.WitAi && witDictation != null) {
            if (state) witDictation.Activate();
            else witDictation.Deactivate();
        }
    }

    private void Update() {
        if (transcriptionSource == TranscriptionSource.Whisper && _isListeningActive) {
            UpdateWhisperLogic();
        }
    }

    private void UpdateWhisperLogic() {
        float vol = GetCurrentVol();
        _currentVolume = vol;

        if (!_isSpeechDetected) {
            // PASSIVE MODE: Waiting for volume to cross START threshold
            if (vol > startThreshold) {
                _isSpeechDetected = true;
                _maxVolInCurrentRecording = vol;
                _speechStartTime = Time.time;
                _silenceTimer = 0f;
                OnSpeechStarted?.Invoke(); // Trigger interrupt
                Debug.Log("[ListenerModule] 📢 Speech Detected! Recording...");
            }
        } else {
            // Track the loudest point of this recording
            if (vol > _maxVolInCurrentRecording) _maxVolInCurrentRecording = vol;

            // ACTIVE MODE: User is talking, waiting for SILENCE
            if (vol < stopThreshold) {
                _silenceTimer += Time.deltaTime;
                if (_silenceTimer > silenceDuration) {
                    
                    // NOISE REJECTION: If we never heard a real voice peak, ignore it
                    float voicePeakRequired = startThreshold * 1.5f;
                    if (_maxVolInCurrentRecording < voicePeakRequired) {
                        Debug.Log($"[ListenerModule] 💨 Discarded background noise (Peak: {_maxVolInCurrentRecording:F4} < {voicePeakRequired:F4})");
                        ResetVADState();
                        return;
                    }

                    ProcessDetectedSpeech();
                    ResetVADState();
                }
            } else {
                _silenceTimer = 0f;
            }

            // Safety timeout (30s max recording)
            if (Time.time - _speechStartTime > 28f) {
                ProcessDetectedSpeech();
            }
        }
    }

    private void ResetVADState() {
        _isSpeechDetected = false;
        _silenceTimer = 0f;
        _maxVolInCurrentRecording = 0f;
    }

    private void ProcessDetectedSpeech() {
        _isSpeechDetected = false;
        float duration = (Time.time - _speechStartTime) - silenceDuration;
        
        if (duration < 0.3f) {
            Debug.Log("[ListenerModule] 🔇 Noise/Short sound ignored (too brief).");
            return;
        }

        // Calculate sample range from the looping buffer
        int endPos = Microphone.GetPosition(_micName);
        int totalSamples = (int)(duration * SampleRate);
        int startPos = endPos - totalSamples - (int)(silenceDuration * SampleRate);
        
        // Handle wrap-around
        if (startPos < 0) startPos += MicBufferSec * SampleRate;

        StartCoroutine(ExtractAndTranscribe(startPos, totalSamples + (int)(silenceDuration * SampleRate)));
    }

    private IEnumerator ExtractAndTranscribe(int startSample, int count) {
        Debug.Log("[ListenerModule] 🧠 Processing speech...");
        
        float[] samples = new float[count];
        _micLoop.GetData(samples, startSample);

        var task = whisperManager.GetTextAsync(samples, SampleRate, 1);
        while (!task.IsCompleted) yield return null;

        if (task.IsFaulted) {
            Debug.LogError($"[ListenerModule] Whisper Error: {task.Exception}");
        } else if (task.Result != null && !string.IsNullOrEmpty(task.Result.Result)) {
            // HARDENED CHECK: If we were muted while this was processing, DISCARD IT.
            if (!_isListeningActive) {
                Debug.Log($"[ListenerModule] 🚫 Discarding late transcription: \"{task.Result.Result}\"");
                yield break;
            }

            string text = task.Result.Result.Trim();
            if (!IsHallucination(text)) {
                HandleFullTranscription(text);
            } else {
                Debug.Log($"[ListenerModule] 🚫 Noise filtered: \"{text}\"");
            }
        }
        
        // Re-enable passive listening if we are still active
        if (_isListeningActive) {
            _isSpeechDetected = false;
            _silenceTimer = 0f;
        }
    }

    private bool IsHallucination(string text) {
        string lower = text.ToLower();
        if (lower == "thank you." || lower == "thanks for watching." || lower == "thank you for watching." || 
               lower == "bye." || lower == "please subscribe." || lower == "you" || text.Length < 2) return true;

        // NEW: Garbage Detection (Character Diversity)
        // If the text is mostly one character (dots, spaces, repetitive vowels), it's noise.
        int uniqueChars = 0;
        HashSet<char> charSet = new HashSet<char>();
        foreach(char c in lower) if(char.IsLetterOrDigit(c)) charSet.Add(c);
        if (charSet.Count < 2 && text.Length > 5) return true; // e.g. "aaaaaaaaa" or "........"

        return false;
    }

    private float GetCurrentVol() {
        int pos = Microphone.GetPosition(_micName);
        if (pos < 256) return 0f;
        float[] samples = new float[256];
        _micLoop.GetData(samples, pos - 256);
        float sum = 0;
        foreach (var s in samples) sum += s * s;
        return Mathf.Sqrt(sum / 256f);
    }
}
