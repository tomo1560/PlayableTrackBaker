#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PlayableTrackBaking
{
    /// <summary>Timeline 評価と baked clip を非破壊のゴースト上で比較する editor-only runner。</summary>
    internal static class TimelineBakePrecisionValidationRunner
    {
        const int MaxSamples = 10001;

        internal static TimelineBakePrecisionReport ValidatePosition(
            PlayableDirector director, TimelineAsset timeline, AnimationClip clip, GameObject root, float fps)
        {
            if (director == null || timeline == null || clip == null || root == null)
                throw new ArgumentNullException("Precision validation に必要な入力がありません。");
            if (AnimationMode.InAnimationMode())
                throw new InvalidOperationException("AnimationMode が使用中のため精度検証できません。Preview を停止して再試行してください。");

            double originalTime = director.time;
            GameObject container = null;
            try
            {
                // active なオブジェクトを直接 Instantiate すると ExecuteAlways 等の Awake/OnEnable が
                // 複製直後に走る。先に inactive container を作り、その子として一度も active にせず
                // 複製・評価・破棄することで、ユーザースクリプトのライフサイクル副作用を避ける。
                container = new GameObject("[PrecisionValidation] Container");
                container.hideFlags = HideFlags.HideAndDontSave;
                container.SetActive(false);

                var ghost = UnityEngine.Object.Instantiate(root, container.transform, false);
                ghost.name = "[PrecisionValidation] " + root.name;
                ghost.hideFlags = HideFlags.HideAndDontSave;
                foreach (var directorInGhost in ghost.GetComponentsInChildren<PlayableDirector>(true))
                    UnityEngine.Object.DestroyImmediate(directorInGhost);
                foreach (var animator in ghost.GetComponentsInChildren<Animator>(true))
                    UnityEngine.Object.DestroyImmediate(animator);
                foreach (var behaviour in ghost.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null)
                        behaviour.enabled = false;

                var original = root.GetComponentsInChildren<Transform>(true);
                var baked = ghost.GetComponentsInChildren<Transform>(true);
                if (original.Length != baked.Length)
                    throw new InvalidOperationException("精度検証用ゴーストの Transform 構造が一致しません。");

                var sampleTimes = BuildSampleTimes(timeline.duration, fps);
                var sourcePositions = new Vector3[original.Length];
                AnimationMode.StartAnimationMode();
                return TimelineBakePrecisionValidator.ValidatePosition(
                    sampleTimes,
                    time =>
                    {
                        director.time = time;
                        director.Evaluate();
                        CopyLocalPositions(original, sourcePositions);
                        // baked callback では sourcePositions との差分を返すため、ここは原点でよい。
                        return Vector3.zero;
                    },
                    time =>
                    {
                        AnimationMode.BeginSampling();
                        try
                        {
                            AnimationMode.SampleAnimationClip(ghost, clip, time);
                        }
                        finally
                        {
                            AnimationMode.EndSampling();
                        }
                        return MaximumPositionDelta(sourcePositions, baked);
                    });
            }
            finally
            {
                try
                {
                    if (AnimationMode.InAnimationMode())
                        AnimationMode.StopAnimationMode();
                    director.time = originalTime;
                    director.Evaluate();
                }
                finally
                {
                    if (container != null)
                        UnityEngine.Object.DestroyImmediate(container);
                }
            }
        }

        static void CopyLocalPositions(Transform[] transforms, Vector3[] positions)
        {
            for (int i = 0; i < transforms.Length; i++)
                positions[i] = transforms[i].localPosition;
        }

        static Vector3 MaximumPositionDelta(Vector3[] sourcePositions, Transform[] baked)
        {
            // ValidatePosition は Vector3 距離を集計するため、各 Transform 差分のうち最大の
            // 差分ベクトルを返す。これにより位置誤差の最大値を1回の Timeline 評価で求められる。
            Vector3 maximum = Vector3.zero;
            float maxSqr = -1f;
            for (int i = 0; i < baked.Length; i++)
            {
                var delta = sourcePositions[i] - baked[i].localPosition;
                if (delta.sqrMagnitude > maxSqr)
                {
                    maximum = delta;
                    maxSqr = delta.sqrMagnitude;
                }
            }
            return maximum;
        }

        static IEnumerable<float> BuildSampleTimes(double duration, float fps)
        {
            int intervals = Mathf.Max(1, Mathf.CeilToInt((float)duration * Mathf.Max(1f, fps)) * 2);
            if (intervals + 1 > MaxSamples)
                Debug.LogWarning($"[PlayableTrackBaker] 精度検証のサンプル数を {MaxSamples:N0} 点に制限しました。" +
                    "長尺 Timeline では半フレーム刻みより粗くなります。");
            int samples = Mathf.Min(MaxSamples, intervals + 1);
            for (int i = 0; i < samples; i++)
                yield return samples == 1 ? 0f : (float)(duration * i / (samples - 1));
        }
    }
}
#endif
