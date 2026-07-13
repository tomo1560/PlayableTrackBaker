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
                ShowEffect(chorusHalo, nameof(OnChorusPulse));
        }

        public void OnFinaleBloom()
        {
            if (Application.isPlaying)
                ShowEffect(finaleBloom, nameof(OnFinaleBloom));
        }

        // 演出は加算式: 本番のGAIASignalEventReceiverと同じく、発火済みの演出は点灯したままにする。
        void ShowEffect(GameObject effect, string eventName)
        {
            eventCount++;
            lastEventName = eventName;
            effect.SetActive(true);
            Debug.Log($"[GAIA Signal Preview] {eventName} ({eventCount})", this);
        }
    }
}
