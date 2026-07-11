using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PlayableTrackBaking.Tests
{
    /// <summary>
    /// テスト用のカスタム PlayableAsset。
    /// 対象 Transform を ExposedReference で受け取り、SineMoveTestBehaviour を生成する。
    /// </summary>
    public class SineMoveTestPlayableAsset : PlayableAsset, ITimelineClipAsset
    {
        public ExposedReference<Transform> target;
        public float amplitude = 1.5f;
        public float frequency = 0.5f;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<SineMoveTestBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.target = target.Resolve(graph.GetResolver());
            behaviour.amplitude = amplitude;
            behaviour.frequency = frequency;
            return playable;
        }
    }

    /// <summary>
    /// 時刻 t に対して localPosition.y = amplitude * sin(2π * frequency * t) を
    /// 「絶対値で」書き込む決定論的な PlayableBehaviour。
    /// 内部状態（基準位置のキャプチャ等）を持たないので、同じ時刻を何度評価しても同じ結果になる。
    /// </summary>
    [System.Serializable]
    public class SineMoveTestBehaviour : PlayableBehaviour
    {
        public Transform target;
        public float amplitude = 1.5f;
        public float frequency = 0.5f; // Hz

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (target == null)
                return;
            float t = (float)playable.GetTime();
            var p = target.localPosition;
            p.y = amplitude * Mathf.Sin(2f * Mathf.PI * frequency * t);
            target.localPosition = p;
        }
    }
}
