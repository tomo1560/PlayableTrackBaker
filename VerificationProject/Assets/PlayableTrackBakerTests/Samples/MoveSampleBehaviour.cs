using UnityEngine;
using UnityEngine.Playables;

namespace PlayableTrackBaking.Samples
{
    /// <summary>
    /// 検証用のサンプル「自作 PlayableBehaviour」。
    /// 対象 Transform を時間に応じて上下（正弦波）に動かすだけ。
    /// C# コードで直接 Transform を書き換えるため VRChat 上では動作しない ＝ ベイク対象そのもの。
    ///
    /// PlayableBehaviour は UnityEngine.Object を継承しないため、ファイル名一致の制約は無い
    /// （PlayableAsset 側はファイル名＝クラス名にする必要がある点に注意）。
    /// </summary>
    [System.Serializable]
    public class MoveSampleBehaviour : PlayableBehaviour
    {
        public Transform target;
        public float amplitude = 1.5f;
        public float frequency = 0.5f; // Hz

        bool _captured;
        Vector3 _basePos;

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            if (target != null && !_captured)
            {
                _basePos = target.localPosition;
                _captured = true;
            }
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (target == null)
                return;
            if (!_captured)
            {
                _basePos = target.localPosition;
                _captured = true;
            }

            double t = playable.GetTime();
            float y = amplitude * Mathf.Sin(2f * Mathf.PI * frequency * (float)t);
            target.localPosition = _basePos + new Vector3(0f, y, 0f);
        }
    }
}
