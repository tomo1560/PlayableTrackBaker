using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace GAIALyricsMovie
{
    /// <summary>ベイク済みAnimationEventからGAIAのSignal演出を再生するVRChat Worlds用受信先。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public sealed class GAIASignalEventReceiver : UdonSharpBehaviour
    {
        [SerializeField] GameObject chorusHalo;
        [SerializeField] GameObject finaleBloom;

        [HideInInspector] public int eventCount;
        [HideInInspector] public string lastEventName;

        public void OnChorusPulse()
        {
            ShowEffect(chorusHalo, finaleBloom, nameof(OnChorusPulse));
        }

        public void OnFinaleBloom()
        {
            ShowEffect(finaleBloom, chorusHalo, nameof(OnFinaleBloom));
        }

        void ShowEffect(GameObject enabledEffect, GameObject disabledEffect, string eventName)
        {
            eventCount++;
            lastEventName = eventName;
            enabledEffect.SetActive(true);
            disabledEffect.SetActive(false);
            Debug.Log($"[GAIA Signal / Udon] {eventName} ({eventCount})");
        }
    }
}
