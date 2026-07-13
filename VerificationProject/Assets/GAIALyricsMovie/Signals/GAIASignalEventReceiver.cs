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
        [SerializeField] GameObject introSparkShards;
        [SerializeField] GameObject[] beaconSegments;
        [SerializeField] GameObject rapidTwinA;
        [SerializeField] GameObject rapidTwinB;
        [SerializeField] GameObject bridgeVeil;
        [SerializeField] GameObject outroRing;

        [SerializeField] float chorusPulseTime = 56.52f;
        [SerializeField] float finaleBloomTime = 153.24f;
        [SerializeField] float introSparkTime = 12.5f;
        [SerializeField] float[] beaconTimes;
        [SerializeField] float rapidPulseATime = 100f;
        [SerializeField] float rapidPulseBTime = 100.4f;
        [SerializeField] float bridgeDimTime = 130f;
        [SerializeField] float outroFadeTime = 210f;

        [HideInInspector] public int eventCount;
        [HideInInspector] public string lastEventName;
        [HideInInspector] public int beaconFireCount;

        public void OnIntroSpark()
        {
            RegisterEvent(nameof(OnIntroSpark));
            introSparkShards.SetActive(true);
        }

        public void OnVerseBeacon()
        {
            RegisterEvent(nameof(OnVerseBeacon));
            beaconFireCount++;
            int segmentIndex = beaconFireCount - 1;
            if (segmentIndex >= 0 && segmentIndex < beaconSegments.Length)
                beaconSegments[segmentIndex].SetActive(true);
        }

        public void OnRapidPulseA()
        {
            RegisterEvent(nameof(OnRapidPulseA));
            rapidTwinA.SetActive(true);
        }

        public void OnRapidPulseB()
        {
            RegisterEvent(nameof(OnRapidPulseB));
            rapidTwinB.SetActive(true);
        }

        public void OnBridgeDim()
        {
            RegisterEvent(nameof(OnBridgeDim));
            bridgeVeil.SetActive(true);
            // ブリッジで暗転する間は、序盤のイントロスパークを退避させる。
            introSparkShards.SetActive(false);
        }

        public void OnOutroFade()
        {
            RegisterEvent(nameof(OnOutroFade));
            outroRing.SetActive(true);
        }

        public void OnChorusPulse()
        {
            RegisterEvent(nameof(OnChorusPulse));
            chorusHalo.SetActive(true);
        }

        public void OnFinaleBloom()
        {
            RegisterEvent(nameof(OnFinaleBloom));
            finaleBloom.SetActive(true);
        }

        /// <summary>
        /// シークで踏まなかったAnimationEventは発火しないため、経過秒からSignal演出の状態を復元する。
        /// 実イベント計測用のeventCount / lastEventNameはここでは変更しない。
        /// </summary>
        public void ResyncToTime(float elapsedSeconds)
        {
            bool bridgeDimActive = elapsedSeconds >= bridgeDimTime;
            // ブリッジ暗転以降はイントロスパークを退避させたままにする。
            introSparkShards.SetActive(elapsedSeconds >= introSparkTime && !bridgeDimActive);
            chorusHalo.SetActive(elapsedSeconds >= chorusPulseTime);

            int firedBeaconCount = 0;
            for (int index = 0; index < beaconTimes.Length; index++)
            {
                if (elapsedSeconds >= beaconTimes[index])
                    firedBeaconCount++;
            }
            beaconFireCount = firedBeaconCount;
            for (int index = 0; index < beaconSegments.Length; index++)
                beaconSegments[index].SetActive(index < firedBeaconCount);

            rapidTwinA.SetActive(elapsedSeconds >= rapidPulseATime);
            rapidTwinB.SetActive(elapsedSeconds >= rapidPulseBTime);
            bridgeVeil.SetActive(bridgeDimActive);
            finaleBloom.SetActive(elapsedSeconds >= finaleBloomTime);
            outroRing.SetActive(elapsedSeconds >= outroFadeTime);
            Debug.Log($"[GAIA Signal / Udon] ResyncToTime({elapsedSeconds:F2}s)");
        }

        // 演出は加算式: 発火済みの演出は以降のイベントを経ても点灯したままにする(Bridge Dimによる
        // イントロスパーク退避のみ例外)。
        void RegisterEvent(string eventName)
        {
            eventCount++;
            lastEventName = eventName;
            Debug.Log($"[GAIA Signal / Udon] {eventName} ({eventCount})");
        }
    }
}
