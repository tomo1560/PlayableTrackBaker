using UnityEngine;

namespace GAIALyricsMovie
{
    /// <summary>Unity Play で SignalEmitter の発火を目視確認する EditorOnly 演出。</summary>
    public sealed class GAIASignalPreviewEffect : MonoBehaviour
    {
        [SerializeField] GameObject chorusHalo;
        [SerializeField] GameObject finaleBloom;
        [SerializeField] GameObject introSparkShards;
        [SerializeField] GameObject[] beaconSegments;
        [SerializeField] GameObject rapidTwinA;
        [SerializeField] GameObject rapidTwinB;
        [SerializeField] GameObject bridgeVeil;
        [SerializeField] GameObject outroRing;

        [HideInInspector] public int eventCount;
        [HideInInspector] public string lastEventName;

        int beaconFireCount;

        public void OnIntroSpark()
        {
            if (Application.isPlaying)
            {
                RegisterEvent(nameof(OnIntroSpark));
                introSparkShards.SetActive(true);
            }
        }

        public void OnVerseBeacon()
        {
            if (Application.isPlaying)
            {
                RegisterEvent(nameof(OnVerseBeacon));
                beaconFireCount++;
                int segmentIndex = beaconFireCount - 1;
                if (segmentIndex >= 0 && segmentIndex < beaconSegments.Length)
                    beaconSegments[segmentIndex].SetActive(true);
            }
        }

        public void OnRapidPulseA()
        {
            if (Application.isPlaying)
            {
                RegisterEvent(nameof(OnRapidPulseA));
                rapidTwinA.SetActive(true);
            }
        }

        public void OnRapidPulseB()
        {
            if (Application.isPlaying)
            {
                RegisterEvent(nameof(OnRapidPulseB));
                rapidTwinB.SetActive(true);
            }
        }

        public void OnBridgeDim()
        {
            if (Application.isPlaying)
            {
                RegisterEvent(nameof(OnBridgeDim));
                bridgeVeil.SetActive(true);
                introSparkShards.SetActive(false);
            }
        }

        public void OnOutroFade()
        {
            if (Application.isPlaying)
            {
                RegisterEvent(nameof(OnOutroFade));
                outroRing.SetActive(true);
            }
        }

        public void OnChorusPulse()
        {
            if (Application.isPlaying)
            {
                RegisterEvent(nameof(OnChorusPulse));
                chorusHalo.SetActive(true);
            }
        }

        public void OnFinaleBloom()
        {
            if (Application.isPlaying)
            {
                RegisterEvent(nameof(OnFinaleBloom));
                finaleBloom.SetActive(true);
            }
        }

        // 演出は加算式: 本番のGAIASignalEventReceiverと同じく、発火済みの演出は点灯したままにする
        // (Bridge Dimによるイントロスパーク退避のみ例外)。
        void RegisterEvent(string eventName)
        {
            eventCount++;
            lastEventName = eventName;
            Debug.Log($"[GAIA Signal Preview] {eventName} ({eventCount})", this);
        }
    }
}
