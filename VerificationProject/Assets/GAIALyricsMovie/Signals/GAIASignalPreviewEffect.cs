using UnityEngine;

namespace GAIALyricsMovie
{
    /// <summary>Unity Play で SignalEmitter の発火を目視確認する EditorOnly 演出。</summary>
    public sealed class GAIASignalPreviewEffect : MonoBehaviour
    {
        [SerializeField] GameObject chorusHalo;
        [SerializeField] GameObject finaleBloom;

        [HideInInspector] public int eventCount;
        [HideInInspector] public string lastEventName;

        public void OnChorusPulse()
        {
            if (Application.isPlaying)
                ShowEffect(chorusHalo, finaleBloom, nameof(OnChorusPulse));
        }

        public void OnFinaleBloom()
        {
            if (Application.isPlaying)
                ShowEffect(finaleBloom, chorusHalo, nameof(OnFinaleBloom));
        }

        void ShowEffect(GameObject enabledEffect, GameObject disabledEffect, string eventName)
        {
            eventCount++;
            lastEventName = eventName;
            enabledEffect.SetActive(true);
            disabledEffect.SetActive(false);
            Debug.Log($"[GAIA Signal Preview] {eventName} ({eventCount})", this);
        }
    }
}
