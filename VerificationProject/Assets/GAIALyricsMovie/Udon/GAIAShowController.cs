using UdonSharp;
using UnityEngine;
using UnityEngine.Playables;
using VRC.SDKBase;

namespace GAIALyricsMovie
{
    /// <summary>
    /// ▶ボタンのInteractでGAIAの再生をインスタンス全体に同期開始するVRChat Worlds用コントローラー。
    /// サーバー時刻を基準に途中参加者も同じ経過秒へ自動追従し、再生中に再度押すと全員最初から再生し直す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public sealed class GAIAShowController : UdonSharpBehaviour
    {
        const float DriftCheckIntervalSeconds = 5f;
        const float DriftToleranceSeconds = 0.1f;

        [SerializeField] PlayableDirector director;
        [SerializeField] GAIASignalEventReceiver signalReceiver;
        [SerializeField] double songDuration = 221.504d;

        [UdonSynced] bool showStarted;
        [UdonSynced] double showStartServerTime;

        // SendCustomEventDelayedSecondsは取り消せないため、未消化イベント数で最新のループだけを生かす。
        int pendingDriftChecks;

        public override void Interact()
        {
            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            // 再生中に押した場合も基準時刻を現在へ書き直すため、全員が最初から再生し直す。
            showStarted = true;
            showStartServerTime = Networking.GetServerTimeInSeconds();
            RequestSerialization();
            ApplySyncedShowState();
        }

        // Manual syncでは所有者のRequestSerialization時と途中参加者の初回受信時に届くため、
        // 後から入った参加者もこの1箇所で自動追従する。
        public override void OnDeserialization()
        {
            ApplySyncedShowState();
        }

        void ApplySyncedShowState()
        {
            if (director == null || !showStarted)
                return;

            double elapsed = Networking.GetServerTimeInSeconds() - showStartServerTime;
            if (elapsed < 0d)
                elapsed = 0d;

            if (elapsed >= songDuration)
            {
                FinishShow((float)elapsed);
                return;
            }

            SeekAndPlay(elapsed);
            ScheduleDriftCheck();
        }

        void ScheduleDriftCheck()
        {
            pendingDriftChecks++;
            SendCustomEventDelayedSeconds(nameof(_CheckDrift), DriftCheckIntervalSeconds);
        }

        /// <summary>再生中に5秒周期でサーバー時刻とdirector.timeを比較し、閾値超過をハードシークで補正する。</summary>
        public void _CheckDrift()
        {
            pendingDriftChecks--;
            if (pendingDriftChecks > 0)
                return;
            if (director == null || !showStarted)
                return;

            double expected = Networking.GetServerTimeInSeconds() - showStartServerTime;
            if (expected < 0d)
                expected = 0d;

            if (expected >= songDuration)
            {
                FinishShow((float)expected);
                return;
            }

            if (Mathf.Abs((float)(director.time - expected)) > DriftToleranceSeconds)
            {
                SeekAndPlay(expected);
                Debug.Log($"[GAIA Show / Udon] Drift corrected to {expected:F2}s");
            }

            ScheduleDriftCheck();
        }

        void SeekAndPlay(double elapsed)
        {
            // 再生中にtimeを直接巻き戻すとTimelineがループ扱いで通過済みマーカー/AnimationEventを
            // 再発火させることがあるため、Stopでグラフを破棄してから目的位置で再構築する。
            // AudioTrackはPlayableDirectorにバインド済みなので、timeへのシークだけで音も追従する。
            director.Stop();
            director.time = elapsed;
            director.Play();
            ResyncSignals((float)elapsed);
            // 再構築したグラフは最初の評価で[0, elapsed]のベイク済みAnimationEventを一括再生し、
            // beaconFireCount等の内部カウンタを二重加算するため、数フレーム後に改めて正規化する。
            SendCustomEventDelayedFrames(nameof(_PostSeekResync), 3);
        }

        /// <summary>シーク直後のAnimationEvent一括再生が終わった後に、演出状態を経過秒へ正規化する。</summary>
        public void _PostSeekResync()
        {
            if (director == null || !showStarted)
                return;

            double elapsed = Networking.GetServerTimeInSeconds() - showStartServerTime;
            if (elapsed < 0d)
                elapsed = 0d;
            if (elapsed > songDuration)
                elapsed = songDuration;
            ResyncSignals((float)elapsed);
        }

        void FinishShow(float elapsedSeconds)
        {
            director.Stop();
            // 曲終了後の途中参加者にもフィナーレ演出の最終状態を見せる。
            ResyncSignals(elapsedSeconds);
        }

        void ResyncSignals(float elapsedSeconds)
        {
            // シークで飛ばしたAnimationEventは発火しないため、経過秒から演出状態を明示的に復元する。
            if (signalReceiver != null)
                signalReceiver.ResyncToTime(elapsedSeconds);
        }
    }
}
