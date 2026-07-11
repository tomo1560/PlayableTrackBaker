using UnityEngine;
using UnityEngine.Playables;
using VRC.SDKBase;

namespace PlayableTrackBaking
{
    /// <summary>
    /// PlayableTrack をベイクしたい PlayableDirector と同じオブジェクトに付けるマーカー。
    /// IEditorOnly を実装しているので、VRChat アップロード時にコンポーネント自体は自動除去される。
    /// </summary>
    [RequireComponent(typeof(PlayableDirector))]
    public class TimelineBakeMarker : MonoBehaviour, IEditorOnly
    {
        [Tooltip("ベイク対象の PlayableDirector（未指定なら同じオブジェクトから自動取得）")]
        public PlayableDirector director;

        [Tooltip("PlayableTrack が動かしているオブジェクトのルート。ここ以下の動きが AnimationClip に記録される")]
        public GameObject[] recordRoots;

        [Tooltip("true: Transform 以外（BlendShape 等の全アニメーション可能プロパティ）も記録する")]
        public bool recordAllProperties = false;

        [Tooltip("ベイクのサンプリングレート")]
        public float frameRate = 60f;

        [Tooltip("true: 精度優先。キーフレーム削減を行わず、サンプルした全フレームをそのままキー化する（サンプル点で厳密一致・線形補間）。クリップは大きくなる。\nfalse: 軽量。GameObjectRecorder の既定のキーフレーム削減（圧縮）を使う")]
        public bool highPrecision = false;

        [Range(0f, 0.05f)]
        [Tooltip("精度優先モード時のキー削減量。各カーブの値域に対する最大許容誤差の割合（0 = 無削減で全フレームを残す）。上げるほどキーを間引いてクリップを軽くする（最大誤差はこの割合以内に収まる）。\n軽量モードや Record All Properties の追加プロパティ（BlendShape 等）には影響しない")]
        public float highPrecisionReduction = 0f;

        [Tooltip("true: ベイク後に元の PlayableTrack を自動でミュートし、エディタプレビューと VRChat の挙動を一致させる")]
        public bool mutePlayableTracksAfterBake = true;

        void Reset()
        {
            director = GetComponent<PlayableDirector>();
        }
    }
}
