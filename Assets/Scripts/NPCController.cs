using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using OpenAI;
using TMPro;
using UnityEngine;

public class NPCController : MonoBehaviour
{
    [Tooltip("Should avatar introduce self when others comes in proximity?")]
    public bool introduceSelf;

    // References to modules
    [SerializeField] public ListenerModule listenerModule;
    [SerializeField] public ThinkerModule thinkerModule;
    [SerializeField] public SpeakerModule speakerModule;
    [SerializeField] public VisionModule visionModule;
    [SerializeField] public MotionControllerModule motionControllerModule;

    public KeywordEvent[] keywordEvents;

    // open ai api request variables
    private OpenAIClient openAI;
    private string userInput;

    [Header("Microphone Input")]
    [Tooltip("Displays the latest transcribed text from the microphone.")]
    public string micinput;

    [Header("Testing")]
    [Tooltip("Enter text here and right-click the component -> 'Send Test Text' to test the NPC manually.")]
    public string testInputText = "Hello! How are you?";

    public TextMeshProUGUI micInputText;
    public TMP_InputField manualInputText;
    // -----------------------------------------------------------------------
    // Main-thread dispatcher
    // Wit.ai, LLM APIs, and ElevenLabs all fire events on background threads.
    // Unity Animator/AudioSource APIs MUST only be called from the main thread.
    // Every event callback below enqueues its work here; Update() drains it.
    // -----------------------------------------------------------------------
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

    private void Update() {
        while (_mainThreadQueue.TryDequeue(out var action))
            action?.Invoke();
    }

    private void Dispatch(Action action) => _mainThreadQueue.Enqueue(action);

    // -----------------------------------------------------------------------
    // Named delegate fields — required so we can unsubscribe lambdas in OnDestroy
    // -----------------------------------------------------------------------
    private Action<string>              _onUserInput;
    private Action<string>              _onPartialTranscript;
    private Action<string>              _onChatInput;
    private Action<string>              _onVisionInput;
    private Action                       _onStartedSpeaking;  // fires when audio actually plays
    private AudioManager.TalkingComplete _onTalkingComplete;
    private Action                       _onRequestCanceled;
    private Action                       _onSpeechStarted;

    void Awake() {
        listenerModule         = GetComponent<ListenerModule>();
        thinkerModule          = GetComponent<ThinkerModule>();
        speakerModule          = GetComponent<SpeakerModule>();
        visionModule           = GetComponent<VisionModule>();
        motionControllerModule = GetComponent<MotionControllerModule>();

        thinkerModule.SetClientID(openAI);
        visionModule.SetClientID(openAI);
        visionModule.SetPersonality(thinkerModule.Personality);

        // Wire all callbacks through Dispatch() so they always run on main thread
        _onUserInput       = s  => Dispatch(() => HandleUserInput(s));
        _onPartialTranscript = s => Dispatch(() => micinput = s);
        _onChatInput       = s  => Dispatch(() => HandleChatResponse(s));
        _onVisionInput     = s  => Dispatch(() => HandleChatResponse(s));
        _onStartedSpeaking = () => Dispatch(() => {
            motionControllerModule.SetAnimatorSpeaking();
            listenerModule.ToggleDictation(false); // Double-safe: mute ears when audio starts
        });
        _onTalkingComplete  = () => Dispatch(() => {
            motionControllerModule.SetAnimatorListening();
            BeginListening();
        });
        _onRequestCanceled = () => Dispatch(() => OnSpeakerModuleCancel());
        _onSpeechStarted = () => Dispatch(() => HandleInterrupt());

        listenerModule.OnUserInputReceived           += _onUserInput;
        listenerModule.OnPartialTranscription        += _onPartialTranscript;
        listenerModule.OnSpeechStarted               += _onSpeechStarted;
        thinkerModule.OnChatGPTInputReceived         += _onChatInput;
        visionModule.OnChatGPTVisionInputReceived    += _onVisionInput;
        speakerModule.AudioManager.OnStartedSpeaking += _onStartedSpeaking;  // accurate: fires on playback start
        speakerModule.AudioManager.OnTalkingComplete += _onTalkingComplete;
        speakerModule.OnRequestCanceled              += _onRequestCanceled;
    }

    private void OnDestroy() {
        listenerModule.OnUserInputReceived           -= _onUserInput;
        listenerModule.OnPartialTranscription        -= _onPartialTranscript;
        listenerModule.OnSpeechStarted               -= _onSpeechStarted;
        thinkerModule.OnChatGPTInputReceived         -= _onChatInput;
        visionModule.OnChatGPTVisionInputReceived    -= _onVisionInput;
        speakerModule.AudioManager.OnStartedSpeaking -= _onStartedSpeaking;
        speakerModule.AudioManager.OnTalkingComplete -= _onTalkingComplete;
        speakerModule.OnRequestCanceled              -= _onRequestCanceled;
    }

    private void Start() {
        if (introduceSelf) {
            IntroduceSelf();
        }
    }

    public void IntroduceSelf() {
        StartCoroutine(AutoIntroduceRoutine());
    }

    private IEnumerator AutoIntroduceRoutine() {
        // Wait for connections and systems to initialize
        yield return new WaitForSeconds(2.0f);
        
        Debug.Log($"[NPCController] 🤖 {thinkerModule.Personality.Name} is introducing themselves...");

        string prompt = $"Hello! Please introduce yourself to the user based on your personality. " +
                       $"Your name is {thinkerModule.Personality.Name}. " +
                       $"Personality: {thinkerModule.Personality.Description}. " +
                       $"Keep it short and stay in character.";

        motionControllerModule.SetAnimatorThinking();
        thinkerModule.GenerateResponse(prompt);
    }

    /// <summary>Restart conversational pipeline — begin listening again.</summary>
    private void BeginListening() => listenerModule.ToggleDictation(true);

    private void OnSpeakerModuleCancel() {
        motionControllerModule.SetAnimatorListening();
        BeginListening();
    }

    private void HandleInterrupt() {
        // If we are currently talking or waiting for audio, stop it!
        if (speakerModule.AudioManager.audioSource.isPlaying) {
            Debug.Log("[NPCController] 🛑 Interrupting NPC because user started speaking.");
            speakerModule.AudioManager.audioSource.Stop();
            motionControllerModule.SetAnimatorListening();
        }
    }

    /// <summary>Feed LLM text response to ElevenLabs TTS.</summary>
    private void HandleChatResponse(string chatResponse) => speakerModule.SubmitVoiceRequest(chatResponse);

    [ContextMenu("Send Test Text")]
    public void SendTestText() {
        if (Application.isPlaying) {
            Debug.Log($"Sending Test Text: {testInputText}");
            HandleUserInput(testInputText);
        } else {
            Debug.LogWarning("You must be in Play Mode to test the NPC.");
        }
    }

    public void SendManualTextResponse()
    {
        if (Application.isPlaying)
        {
           
            HandleUserInput(manualInputText.text.ToString());
        }
    }

    // Handle user input — always called on main thread via Dispatch()
    public void HandleUserInput(string input) {
        micinput   = input;
        micInputText.text = input;
        userInput  = input;

        if (IsInputIncomplete(input)) {
            RequestUserToRepeat();
            return;
        }
        if (IsKeyword(input)) {
            return;
        }

        // Stop listening immediately to prevent feedback loops
        listenerModule.ToggleDictation(false);

        // Stop any current audio to prevent playback state from being stuck
        speakerModule.AudioManager.audioSource.Stop();

        // Switch to thinking animation immediately on main thread
        motionControllerModule.SetAnimatorThinking();
        thinkerModule.GenerateResponse(input);
    }

    // You can add length/punctuation checks here if needed
    private bool IsInputIncomplete(string input) => false;

    private bool IsKeyword(string input) {
        foreach (KeywordEvent k in keywordEvents) {
            if (input.Contains(k.Keyword)) {
                Debug.Log($"Keyword matched: {k.Keyword}");
                k.keywordEvent.Invoke();
                return true;
            }
        }
        return false;
    }

    private void RequestUserToRepeat() {
        // No timeout audio played as per user request to remove premade dialogues
    }

    public void SetUserInputToVisionModule() => visionModule.ExternalSetImageGeneratePrompt(userInput);
}


[System.Serializable]
public class KeywordEvent {
    public string Keyword;
    public UnityEngine.Events.UnityEvent keywordEvent;
}