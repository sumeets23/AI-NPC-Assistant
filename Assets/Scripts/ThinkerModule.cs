using OpenAI.Chat;
using OpenAI;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Unity.Collections.LowLevel.Unsafe;
using System;
using static UnityEngine.Rendering.DebugUI;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;
using UnityEngine.Networking;
using OpenAI.Models;

[System.Serializable]
public class ThinkerModule : MonoBehaviour
{
    public enum LLMProvider
    {
        OpenAI,
        LMStudio,
        Ollama
    }

    public event Action<string> OnChatGPTInputReceived;

    [Header("LLM Provider Configuration")]
    public LLMProvider llmProvider = LLMProvider.OpenAI;

    [Header("Local LLM Settings")]
    [Tooltip("Endpoint URL for LM Studio or Ollama (e.g. http://localhost:1234/v1/chat/completions)")]
    public string localEndpointUrl = "http://localhost:1234/v1/chat/completions";
    [Tooltip("Optional API Token for local LLMs (required by newer versions of LM Studio)")]
    public string localEndpointToken = "lm-studio";
    public string localModelName = "local-model";

    public PersonalityType Personality;
    public bool preprompt;

    // open ai api request variables
    private OpenAIClient openAI;
    private readonly List<Message> chatMessages = new List<Message>();
    private CancellationTokenSource lifetimeCancellationTokenSource;

    private static bool isChatPending;

    private void Awake() {
        Init();
    }
    private void OnDestroy() {
        this.Dipose();
    }

    
    private void Init() {
        lifetimeCancellationTokenSource = new CancellationTokenSource();        

        if (Personality == null) chatMessages.Add(new Message(Role.System, "You are a helpful assistant."));
        else chatMessages.Add(new Message(Role.System, $"Pretend you are {Personality.Name}. Please respond to all proceeding messages as this character in tune with this personality description {Personality.Description}. "));
    }

    public void SetClientID(OpenAIClient client) => openAI = client;

    /// <summary>
    /// entry point from other script to submit api request
    /// </summary>
    /// <param name="input"></param>
    public void GenerateResponse(string input) => SubmitChat(input);
    

    public string PrepromptQueryText(string text) {
        string complete = String.Empty;
        string preprompt = "Briefly and uniquely, as shortly as possible, please use no more than 4 sentences to respond to the following statement/question: ";
        complete = preprompt + text;
        return complete;
    }    


    public async void SubmitChat(string userInput) {
        if (isChatPending || string.IsNullOrWhiteSpace(userInput)) { return; }
        isChatPending = true;

        if (preprompt) userInput = PrepromptQueryText(userInput);
        var userMessage = new Message(Role.User, userInput);
        chatMessages.Add(userMessage);

        // SLIDING WINDOW: Keep only last 10 messages to prevent loops/memory bloat
        if (chatMessages.Count > 11) { // 1 system + 10 conversation
            chatMessages.RemoveAt(1); // Keep the system prompt at index 0
        }

        if (llmProvider == LLMProvider.OpenAI)
        {
            try {
                var chatRequest = new ChatRequest(chatMessages, Model.GPT3_5_Turbo);
                var result = await openAI.ChatEndpoint.GetCompletionAsync(chatRequest);
                var response = result.ToString();
                
                Debug.Log(response);
                OnChatGPTInputReceived?.Invoke(response);
            } catch (Exception e) {
                Debug.LogError(e);
            } finally {
                //if (lifetimeCancellationTokenSource != null) {}
                isChatPending = false;
            }
        }
        else
        {
            StartCoroutine(SendLocalLLMRequest());
        }
    }

    private IEnumerator SendLocalLLMRequest()
    {
        string jsonPayload = "";
        try 
        {
            var req = new LocalChatRequest();
            req.model = localModelName;
            foreach (var msg in chatMessages)
            {
                req.messages.Add(new LocalChatMessage { 
                    role = msg.Role.ToString().ToLower(), 
                    content = msg.ToString() 
                });
            }
            jsonPayload = JsonUtility.ToJson(req);
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            isChatPending = false;
            yield break;
        }

        using (var request = new UnityWebRequest(localEndpointUrl, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            
            if (!string.IsNullOrEmpty(localEndpointToken))
            {
                request.SetRequestHeader("Authorization", "Bearer " + localEndpointToken);
            }

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"[SendLocalLLM] Error: {request.error}");
                Debug.LogError($"[SendLocalLLM] Tried to hit URL: {request.url}");
                Debug.LogError($"[SendLocalLLM] Response Body: {request.downloadHandler.text}");
            }
            else
            {
                try
                {
                    var responseJson = request.downloadHandler.text;
                    var res = JsonUtility.FromJson<LocalChatResponse>(responseJson);
                    if (res != null && res.choices != null && res.choices.Count > 0)
                    {
                        var responseText = res.choices[0].message.content;
                        chatMessages.Add(new Message(Role.Assistant, responseText));
                        Debug.Log(responseText);
                        OnChatGPTInputReceived?.Invoke(responseText);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
        }
        isChatPending = false;
    }

    [Serializable]
    private class LocalChatRequest
    {
        public string model;
        public List<LocalChatMessage> messages = new List<LocalChatMessage>();
        public float temperature = 0.7f;
        public int max_tokens = 150; // Safety limit
    }

    [Serializable]
    private class LocalChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class LocalChatResponse
    {
        public List<LocalChatChoice> choices;
    }

    [Serializable]
    private class LocalChatChoice
    {
        public LocalChatMessage message;
    }

    private void Dipose() {
        lifetimeCancellationTokenSource.Cancel();
        lifetimeCancellationTokenSource.Dispose();
        lifetimeCancellationTokenSource = null;
    }
}


[System.Serializable]
public class PersonalityType {
    public string Name;
    public string Description;

    public PersonalityType(string name, string description) {
        Name = name;
        Description = description;
    }
}