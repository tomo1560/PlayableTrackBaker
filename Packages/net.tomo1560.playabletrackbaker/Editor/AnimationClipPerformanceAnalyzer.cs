#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace PlayableTrackBaking
{
    /// <summary>
    /// AnimationClip の複雑さを、VRChat 固有の未検証な上限値に依存せず可視化する。
    /// EstimatedSizeBytes は Unity の実シリアライズサイズではなく、比較用の概算値。
    /// </summary>
    internal static class AnimationClipPerformanceAnalyzer
    {
        internal static AnimationClipPerformanceReport Analyze(AnimationClip clip)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));

            var floatBindings = AnimationUtility.GetCurveBindings(clip);
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            int floatKeys = 0;
            int objectKeys = 0;

            foreach (var binding in floatBindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                floatKeys += curve != null ? curve.length : 0;
            }
            foreach (var binding in objectBindings)
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                objectKeys += keys != null ? keys.Length : 0;
            }

            // float key: time/value/tangents/weights の概算、object key: time + object 参照の概算。
            // 曲線ごとのヘッダも含め、同一プロジェクト内のクリップ比較に利用できるようにする。
            long estimate = floatBindings.Length * 48L + objectBindings.Length * 48L
                + floatKeys * 32L + objectKeys * 16L;
            return new AnimationClipPerformanceReport(
                floatBindings.Length, objectBindings.Length, floatKeys, objectKeys, estimate);
        }
    }

    internal readonly struct AnimationClipPerformanceReport
    {
        public int FloatCurveCount { get; }
        public int ObjectReferenceCurveCount { get; }
        public int TotalCurveCount => FloatCurveCount + ObjectReferenceCurveCount;
        public int FloatKeyCount { get; }
        public int ObjectReferenceKeyCount { get; }
        public int TotalKeyCount => FloatKeyCount + ObjectReferenceKeyCount;
        public long EstimatedSizeBytes { get; }

        public AnimationClipPerformanceReport(
            int floatCurveCount, int objectReferenceCurveCount,
            int floatKeyCount, int objectReferenceKeyCount, long estimatedSizeBytes)
        {
            FloatCurveCount = floatCurveCount;
            ObjectReferenceCurveCount = objectReferenceCurveCount;
            FloatKeyCount = floatKeyCount;
            ObjectReferenceKeyCount = objectReferenceKeyCount;
            EstimatedSizeBytes = estimatedSizeBytes;
        }

        public override string ToString()
            => $"curves={TotalCurveCount} (float {FloatCurveCount}, object {ObjectReferenceCurveCount}), " +
               $"keys={TotalKeyCount} (float {FloatKeyCount}, object {ObjectReferenceKeyCount}), " +
               $"estimated={EstimatedSizeBytes:N0} B";
    }
}
#endif
