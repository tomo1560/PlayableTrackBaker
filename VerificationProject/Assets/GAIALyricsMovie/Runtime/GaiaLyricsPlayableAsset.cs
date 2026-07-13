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

        void AnimateVisuals(float time)
        {
            if (VisualRoot == null)
                return;

            float beat = time * 1.42f;
            for (int index = 0; index < VisualRoot.childCount; index++)
            {
                Transform layer = VisualRoot.GetChild(index);
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
    }
}
