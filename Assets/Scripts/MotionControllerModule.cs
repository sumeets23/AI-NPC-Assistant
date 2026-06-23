using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MotionControllerModule : MonoBehaviour
{
    public int NUM_TALKING_ANIMATIONS = 3;
    public int NUM_THINKING_ANIMATIONS = 3;
    public int NUM_LISTENING_ANIMATIONS = 1;

    public Animator animator;
    private System.Random randomGenerator = new System.Random();

    private void Awake() {
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    private void SetAnimator(string state) {
        Debug.Log($"[MotionControllerModule] SetAnimator called with state: {state}");
        switch (state) {
            case "ListeningTrigger":
                animator.ResetTrigger("TalkingTrigger");
                animator.ResetTrigger("ThinkingTrigger");
                int listeningIdx = randomGenerator.Next(0, NUM_LISTENING_ANIMATIONS);
                animator.SetInteger("ListeningIndex", listeningIdx);
                animator.SetTrigger("ListeningTrigger");
                Debug.Log($"[MotionControllerModule] Set ListeningTrigger, Index: {listeningIdx}");
                break;
            case "ThinkingTrigger":
                animator.ResetTrigger("ListeningTrigger");
                animator.ResetTrigger("TalkingTrigger");
                int thinkingIdx = randomGenerator.Next(0, NUM_THINKING_ANIMATIONS);
                animator.SetInteger("ThinkingIndex", thinkingIdx);
                animator.SetTrigger("ThinkingTrigger");
                Debug.Log($"[MotionControllerModule] Set ThinkingTrigger, Index: {thinkingIdx}");
                break;
            case "TalkingTrigger":
                animator.ResetTrigger("ListeningTrigger");
                animator.ResetTrigger("ThinkingTrigger");
                int talkingIdx = randomGenerator.Next(0, NUM_TALKING_ANIMATIONS);
                animator.SetInteger("TalkingIndex", talkingIdx);
                animator.SetTrigger("TalkingTrigger");
                Debug.Log($"[MotionControllerModule] Set TalkingTrigger, Index: {talkingIdx}");
                break;
        }
    }

    public void SetAnimatorIntro() => animator.Play("Intro");

    public void SetAnimatorOutro() { }

    // Force-transition to Listening from ANY state (triggers alone can't do this
    // because not all states have a ListeningTrigger transition wired up).
    public void SetAnimatorListening() {
        animator.ResetTrigger("TalkingTrigger");
        animator.ResetTrigger("ThinkingTrigger");
        animator.ResetTrigger("ListeningTrigger");
        animator.CrossFade("Listening", 0.2f);
    }

    public void SetAnimatorThinking() => SetAnimator("ThinkingTrigger");
    public void SetAnimatorSpeaking() => SetAnimator("TalkingTrigger");

}
