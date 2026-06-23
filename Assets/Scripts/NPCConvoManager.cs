using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NPCConvoManager : MonoBehaviour
{
    public NPCController starterNPC;
    private string starterResponse;
    public NPCController receiverNPC;
    private string receiverResponse;

    private void Awake() {        
        FormatReceiver();
    }

    protected void ToggleUserInput(bool status) {
        starterNPC.listenerModule.ToggleDictation(status);
        receiverNPC.listenerModule.ToggleDictation(status);
    }

    [Tooltip("When true, user microphone input is blocked (used for NPC-vs-NPC conversation mode).")]
    public bool blockUserInput = false;

    private bool _lastBlockState = false;
    private void Update() {
        // Only trigger deactivation when the state CHANGES to blocked.
        // This prevents 'hammering' the DictationService every frame.
        if (blockUserInput && !_lastBlockState) {
            ToggleUserInput(false);
        }
        _lastBlockState = blockUserInput;
    }

    // Start is called before the first frame update
    void Start()
    {
        starterResponse = $"Hello, my name is {starterNPC.thinkerModule.Personality.Name}. My bio is as follows {starterNPC.thinkerModule.Personality.Description}. What would you like to discuss? Ask a question or share a thought.";
        
        starterNPC.thinkerModule.OnChatGPTInputReceived += SaveStarterResponse;
        starterNPC.speakerModule.AudioManager.OnTalkingComplete += StarterFinishedSpeaking;

        receiverNPC.thinkerModule.OnChatGPTInputReceived += SaveReceiverResponse;
        receiverNPC.speakerModule.AudioManager.OnTalkingComplete += ReceiverFinishedSpeaking;
    }

    public void FormatReceiver() {
        receiverNPC.gameObject.GetComponentInChildren<Collider>().isTrigger = false;
    }

    [Tooltip("If true, NPCs will automatically talk to each other in a loop.")]
    public bool enableAutoConversation = false;

    private void StarterFinishedSpeaking() {
        if (!enableAutoConversation) return;
        
        receiverNPC.motionControllerModule.SetAnimatorThinking();
        receiverNPC.thinkerModule.GenerateResponse(starterResponse);
    }

    private void ReceiverFinishedSpeaking() {
        if (!enableAutoConversation) return;

        starterNPC.motionControllerModule.SetAnimatorThinking();
        starterNPC.thinkerModule.GenerateResponse(receiverResponse);
    }

    private void OnDestroy() {
        starterNPC.thinkerModule.OnChatGPTInputReceived -= SaveStarterResponse;
        starterNPC.speakerModule.AudioManager.OnTalkingComplete -= StarterFinishedSpeaking;
        receiverNPC.thinkerModule.OnChatGPTInputReceived -= SaveReceiverResponse;
        receiverNPC.speakerModule.AudioManager.OnTalkingComplete -= ReceiverFinishedSpeaking;
    }

    public void SaveStarterResponse(string response) {
        starterResponse = response;
    }

    public void SaveReceiverResponse(string response) {
        receiverResponse = response;
    }

}
