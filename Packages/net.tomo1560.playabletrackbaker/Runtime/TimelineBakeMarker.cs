using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VRC.SDKBase;

namespace PlayableTrackBaking
{
    /// <summary>SignalAsset と event host 上の Udon custom event 名の明示的な対応。</summary>
    [System.Serializable]
    public sealed class SignalEventRoute
    {
        public SignalAsset signal;
        public string udonEventName;

        public SignalEventRoute(SignalAsset signal, string udonEventName)
        {
            this.signal = signal;
            this.udonEventName = udonEventName;
        }
    }

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

        /// <summary>ベイクのサンプリングレートとして許容する下限（fps）。</summary>
        public const float MinFrameRate = 1f;

        /// <summary>ベイクのサンプリングレートとして許容する上限（fps）。極端な値による総サンプル数の爆発を防ぐ。</summary>
        public const float MaxFrameRate = 240f;

        [Range(MinFrameRate, MaxFrameRate)]
        [Tooltip("ベイクのサンプリングレート")]
        public float frameRate = 60f;

        [Tooltip("true: 精度優先。キーフレーム削減を行わず、サンプルした全フレームをそのままキー化する（サンプル点で厳密一致・線形補間）。クリップは大きくなる。\nfalse: 軽量。GameObjectRecorder の既定のキーフレーム削減（圧縮）を使う")]
        public bool highPrecision = false;

        [Range(0f, 0.05f)]
        [Tooltip("精度優先モード時のキー削減量。各カーブの値域に対する最大許容誤差の割合（0 = 無削減で全フレームを残す）。上げるほどキーを間引いてクリップを軽くする（最大誤差はこの割合以内に収まる）。\n軽量モードや Record All Properties の追加プロパティ（BlendShape 等）には影響しない")]
        public float highPrecisionReduction = 0f;

        [Tooltip("true: ベイク後に元の PlayableTrack を自動でミュートし、エディタプレビューと VRChat の挙動を一致させる")]
        public bool mutePlayableTracksAfterBake = true;

        [Header("VRChat Worlds Signal PoC")]
        [Tooltip("有効にすると、明示した SignalAsset-to-Udon event route を event host のベイク済み clip に SendCustomEvent AnimationEvent として追加する。Worlds 専用。")]
        public bool bakeSignalEvents = false;

        [Tooltip("Signal AnimationEvent を載せる Record Root。常時有効で、この GameObject 上の UdonBehaviour が SendCustomEvent を受け取れる必要がある。")]
        public GameObject signalEventHost;

        [Tooltip("SignalAsset と event host の Udon custom event 名の対応。未登録 Signal は安全に無視される。")]
        public SignalEventRoute[] signalEventRoutes;

        void Reset()
        {
            director = GetComponent<PlayableDirector>();
        }
    }
}
