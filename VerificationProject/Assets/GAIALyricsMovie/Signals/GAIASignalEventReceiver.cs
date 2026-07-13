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
        [SerializeField] float chorusPulseTime = 56.52f;
        [SerializeField] float finaleBloomTime = 153.24f;

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

        /// <summary>
        /// シークで踏まなかったAnimationEventは発火しないため、経過秒からSignal演出の状態を復元する。
        /// 実イベント計測用のeventCount / lastEventNameはここでは変更しない。
        /// </summary>
        public void ResyncToTime(float elapsedSeconds)
        {
            chorusHalo.SetActive(elapsedSeconds >= chorusPulseTime && elapsedSeconds < finaleBloomTime);
            finaleBloom.SetActive(elapsedSeconds >= finaleBloomTime);
            Debug.Log($"[GAIA Signal / Udon] ResyncToTime({elapsedSeconds:F2}s)");
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
