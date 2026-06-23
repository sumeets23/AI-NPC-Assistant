using System;
using System.Collections.Concurrent;
using UnityEngine;

[System.Serializable]
public class AudioManager : MonoBehaviour {
    public AudioSource audioSource;
    private ConcurrentQueue<AudioClip> audioClips;

    protected bool conversing;

    public delegate void TalkingComplete();
    public event TalkingComplete OnTalkingComplete;

    // Fires when a clip ACTUALLY starts playing (not just when it is queued)
    public event Action OnStartedSpeaking;

    private System.Random randomGenerator = new System.Random();

    private void Awake() {
        audioClips = new ConcurrentQueue<AudioClip>();
        if (audioSource == null) audioSource = GetComponentInChildren<AudioSource>();
    }

    // Play the next audio clip in the queue
    private void PlayNextAudioClip() {
        if (audioClips.TryDequeue(out var clip)) {
            if (clip == null) {
                Debug.LogWarning("[AudioManager] Dequeued a null clip.");
                return;
            }
            Debug.Log($"[AudioManager] Playing clip: {clip.name} (Length: {clip.length}s)");
            audioSource.loop = false;
            audioSource.clip = clip;
            audioSource.Play();
            OnStartedSpeaking?.Invoke();  // fires when audio actually begins
        }
    }

    protected void Update() {
        // Check if there are more audio clips to play
        if (audioClips.Count > 0 && !audioSource.isPlaying) {
            conversing = true;
            PlayNextAudioClip();
        }
        if (conversing && audioClips.Count == 0 && !audioSource.isPlaying) {
            conversing = false;
            OnTalkingComplete?.Invoke();
        }
    }

    // Set the audio clips for responses
    public void AddAudioClip(AudioClip clip) {
        if (clip != null) {
            Debug.Log($"[AudioManager] Adding clip to queue: {clip.name}");
            audioClips.Enqueue(clip);
        } else {
            Debug.LogWarning("[AudioManager] Attempted to add a null clip.");
        }
    }
}
