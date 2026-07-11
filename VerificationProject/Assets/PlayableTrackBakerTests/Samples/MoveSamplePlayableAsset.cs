using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PlayableTrackBaking.Samples
{
    /// <summary>
    /// MoveSampleBehaviour を PlayableTrack のクリップとして載せるための PlayableAsset。
    ///
    /// PlayableAsset は ScriptableObject を継承し Timeline のサブアセットとして直接シリアライズされるため、
    /// クラス名とファイル名を一致させないと「The associated script can not be loaded」になる。
    /// </summary>
    public class MoveSamplePlayableAsset : PlayableAsset, ITimelineClipAsset
    {
        public ExposedReference<Transform> target;
        public float amplitude = 1.5f;
        public float frequency = 0.5f;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<MoveSampleBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.target = target.Resolve(graph.GetResolver());
            behaviour.amplitude = amplitude;
            behaviour.frequency = frequency;
            return playable;
        }
    }
}
