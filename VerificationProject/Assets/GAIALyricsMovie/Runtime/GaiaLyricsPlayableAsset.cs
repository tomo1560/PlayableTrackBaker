using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GAIALyricsMovie
{
    [Serializable]
    public sealed class GaiaLyricsPlayableAsset : PlayableAsset, ITimelineClipAsset
    {
        public ExposedReference<Transform> lyricsRoot;
        public ExposedReference<Transform> visualRoot;
        public double[] cueStartTimes = Array.Empty<double>();
        public double[] cueEndTimes = Array.Empty<double>();

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<GaiaLyricsPlayableBehaviour>.Create(graph);
            GaiaLyricsPlayableBehaviour behaviour = playable.GetBehaviour();
            behaviour.LyricsRoot = lyricsRoot.Resolve(graph.GetResolver());
            behaviour.VisualRoot = visualRoot.Resolve(graph.GetResolver());
            behaviour.CueStartTimes = cueStartTimes ?? Array.Empty<double>();
            behaviour.CueEndTimes = cueEndTimes ?? Array.Empty<double>();
            return playable;
        }
    }

    public sealed class GaiaLyricsPlayableBehaviour : PlayableBehaviour
    {
        const float EnterDuration = 0.34f;
        const float ExitDuration = 0.28f;

        public Transform LyricsRoot { private get; set; }
        public Transform VisualRoot { private get; set; }
        public double[] CueStartTimes { private get; set; } = Array.Empty<double>();
        public double[] CueEndTimes { private get; set; } = Array.Empty<double>();

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            float time = (float)playable.GetTime();
            AnimateLyrics(time);
            AnimateVisuals(time);
        }

        void AnimateLyrics(float time)
        {
            if (LyricsRoot == null)
                return;

            int cueCount = Mathf.Min(LyricsRoot.childCount, Mathf.Min(CueStartTimes.Length, CueEndTimes.Length));
            for (int index = 0; index < cueCount; index++)
            {
                Transform cue = LyricsRoot.GetChild(index);
                float start = (float)CueStartTimes[index];
                float end = (float)CueEndTimes[index];
                float visibility = CalculateVisibility(time, start, end);
                if (visibility <= 0f)
                {
                    cue.localScale = Vector3.zero;
                    cue.localPosition = Vector3.zero;
                    cue.localRotation = Quaternion.identity;
                    continue;
                }

                float eased = visibility * visibility * (3f - 2f * visibility);
                float direction = index % 2 == 0 ? -1f : 1f;

                cue.localScale = Vector3.one * eased;
                cue.localPosition = new Vector3(direction * (1f - eased) * 1.2f, 0.12f * Mathf.Sin(time * 1.7f), 0f);
                cue.localRotation = Quaternion.Euler(0f, direction * (1f - eased) * 8f, direction * (1f - eased) * 2.5f);
            }
        }

        static float CalculateVisibility(float time, float start, float end)
        {
            if (time < start || time >= end)
                return 0f;

            float fadeIn = Mathf.Clamp01((time - start) / EnterDuration);
            float fadeOut = Mathf.Clamp01((end - time) / ExitDuration);
            return Mathf.Min(fadeIn, fadeOut);
        }

        // "Fidelity Probes" レイヤーの子Transformインデックス。初回のAnimateVisualsで名前引きし、
        // 以降は毎フレームの文字列比較を避けてインデックス一致だけで判定する。
        int probesLayerChildIndex = -1;

        void AnimateVisuals(float time)
        {
            if (VisualRoot == null)
                return;

            int childCount = VisualRoot.childCount;
            if (probesLayerChildIndex < 0)
            {
                for (int i = 0; i < childCount; i++)
                {
                    if (VisualRoot.GetChild(i).name == "Fidelity Probes")
                    {
                        probesLayerChildIndex = i;
                        break;
                    }
                }
            }

            float beat = time * 1.42f;
            for (int index = 0; index < childCount; index++)
            {
                Transform layer = VisualRoot.GetChild(index);
                if (index == probesLayerChildIndex)
                {
                    AnimateProbes(layer, time);
                    continue;
                }

                float direction = index % 2 == 0 ? 1f : -1f;
                float speed = 2.2f + index * 0.37f;
                layer.localRotation = Quaternion.Euler(
                    7f * Mathf.Sin(beat * 0.17f + index),
                    direction * time * speed,
                    direction * time * (5.5f + index));
                float pulse = 1f + Mathf.Sin(beat * (1f + index * 0.08f) + index) * (0.025f + index * 0.004f);
                layer.localScale = Vector3.one * pulse;
            }
        }

        // ---- Fidelity Probes: 30fpsベイクの忠実度を目視確認するための決定論的サブ演出 ----
        // 参照は初回のみ名前引きでキャッシュし、以降は毎フレームのTransform参照だけで動かす
        // (アロケーションなし)。

        bool probesCached;
        Transform cometProbe;
        Transform driftMonolith;
        Transform driftReferenceTwin;
        Transform gyroSpinner;
        Transform teleportBeacon;
        Transform orbitArm;
        Transform orbitCounter;
        readonly Transform[] phaseChoirPillars = new Transform[8];
        Transform scaleBeatCube;

        // Teleport Beacons: 4つの固定スロット位置。Editor.GAIALyricsMovieSceneBuilderが生成する
        // スロット目印(Slot 1..4)と同じ値を使うこと。
        static readonly Vector3[] TeleportSlotLocalPositions =
        {
            new Vector3(-0.6f, 0.4f, 0f),
            new Vector3(-0.2f, 0.4f, 0f),
            new Vector3(0.2f, 0.4f, 0f),
            new Vector3(0.6f, 0.4f, 0f),
        };

        void AnimateProbes(Transform probesLayer, float time)
        {
            if (!probesCached)
                CacheProbeReferences(probesLayer);

            AnimateCometCircuit(time);
            AnimateDriftMonolith(time);
            AnimateGyroSpinner(time);
            AnimateTeleportBeacons(time);
            AnimateOrbitPair(time);
            AnimatePhaseChoir(time);
            AnimateScaleBeat(time);
        }

        void CacheProbeReferences(Transform probesLayer)
        {
            cometProbe = probesLayer.Find("Comet Circuit/Comet");
            driftMonolith = probesLayer.Find("Drift Monolith/Monolith");
            driftReferenceTwin = probesLayer.Find("Drift Monolith/Reference Twin");
            gyroSpinner = probesLayer.Find("Gyro Spinner/Spinner Cube");
            teleportBeacon = probesLayer.Find("Teleport Beacons/Beacon Sphere");
            orbitArm = probesLayer.Find("Orbit Pair/Arm");
            orbitCounter = probesLayer.Find("Orbit Pair/Arm/Counter");

            Transform phaseChoirRoot = probesLayer.Find("Phase Choir");
            for (int index = 0; index < phaseChoirPillars.Length; index++)
                phaseChoirPillars[index] = phaseChoirRoot != null ? phaseChoirRoot.Find($"Pillar {index + 1}") : null;

            scaleBeatCube = probesLayer.Find("Scale Beat/Beat Cube");
            probesCached = true;
        }

        // Comet Circuit: 30fpsのサンプリングでリサージュ8の字曲線がエイリアシング/過度な平滑化を
        // 起こしていないかを見る。
        void AnimateCometCircuit(float time)
        {
            if (cometProbe == null)
                return;

            float tau = time * 1.5f;
            cometProbe.localPosition = new Vector3(
                0.9f * Mathf.Sin(2f * Mathf.PI * tau),
                0.45f * Mathf.Sin(4f * Mathf.PI * tau),
                0f);
        }

        // Drift Monolith: 0.002のreduction閾値付近の極小振幅ドリフト。参照の等身大分身(100倍振幅)と
        // 見比べることで、キー削減による動き消失を判別しやすくする。
        void AnimateDriftMonolith(float time)
        {
            float wave60 = Mathf.Sin(2f * Mathf.PI * time / 60f);
            float wave08 = Mathf.Sin(2f * Mathf.PI * time * 0.8f);

            if (driftMonolith != null)
                driftMonolith.localPosition = new Vector3(-0.3f + 0.004f * wave60, 0.5f + 0.003f * wave08, 0f);

            if (driftReferenceTwin != null)
                driftReferenceTwin.localPosition = new Vector3(0.3f + 0.4f * wave60, 0.5f + 0.3f * wave08, 0f);
        }

        // Gyro Spinner: 540°/秒(30fpsで1フレームあたり18°)の高速回転。四元数ベイクの精度/
        // エイリアシングを見る。
        void AnimateGyroSpinner(float time)
        {
            if (gyroSpinner == null)
                return;

            gyroSpinner.localRotation = Quaternion.Euler(time * 90f, time * 540f, 0f);
        }

        // Teleport Beacons: 4スロットを瞬間移動する(区間内は補間なし)。ベイクが忠実なら移動は
        // 1フレーム以内でスナップし、スミアが出ればカーブ補間の誤りを示す。
        void AnimateTeleportBeacons(float time)
        {
            if (teleportBeacon == null)
                return;

            int slot = (int)Mathf.Floor(time / 3.457f) % TeleportSlotLocalPositions.Length;
            if (slot < 0)
                slot += TeleportSlotLocalPositions.Length;
            teleportBeacon.localPosition = TeleportSlotLocalPositions[slot];
        }

        // Orbit Pair (nested): 親Armがゆっくり回転し、子Counterが逆回転しつつ上下に振動する。
        // 深い階層のTransformパス記録を検証する。
        void AnimateOrbitPair(float time)
        {
            if (orbitArm != null)
                orbitArm.localRotation = Quaternion.Euler(0f, time * 45f, 0f);

            if (orbitCounter != null)
            {
                orbitCounter.localRotation = Quaternion.Euler(0f, -time * 90f, 0f);
                Vector3 position = orbitCounter.localPosition;
                position.y = 0.3f * Mathf.Sin(2f * Mathf.PI * time * 0.5f);
                orbitCounter.localPosition = position;
            }
        }

        // Phase Choir: 8本のピラーが位相をずらして波打つ。タイミングのズレは波の乱れとして見える。
        void AnimatePhaseChoir(float time)
        {
            float phaseBase = 2f * Mathf.PI * (time * 0.9f);
            for (int k = 0; k < phaseChoirPillars.Length; k++)
            {
                Transform pillar = phaseChoirPillars[k];
                if (pillar == null)
                    continue;

                float wave = Mathf.Sin(phaseBase + k * Mathf.PI / 4f);
                Vector3 position = pillar.localPosition;
                position.y = 0.5f * wave;
                pillar.localPosition = position;

                Vector3 scale = pillar.localScale;
                scale.y = 1f + 0.35f * wave;
                pillar.localScale = scale;
            }
        }

        // Scale Beat: ビートに合わせた鋭いアタック/ディケイ。非正弦(指数)エンベロープが
        // キー削減を生き延びるかを見る。
        void AnimateScaleBeat(float time)
        {
            if (scaleBeatCube == null)
                return;

            float scaledTime = time * 1.42f;
            float phase = scaledTime - Mathf.Floor(scaledTime);
            float s = 1f + 0.6f * Mathf.Exp(-8f * phase);
            scaleBeatCube.localScale = Vector3.one * s;
        }
    }
}
