#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayableTrackBaking
{
    /// <summary>位置誤差の全区間検証結果。呼び出し側が Timeline／clip の評価方法を提供する。</summary>
    internal readonly struct TimelineBakePrecisionReport
    {
        public int SampleCount { get; }
        public float MaxPositionError { get; }
        public float MaxPositionErrorTime { get; }

        public TimelineBakePrecisionReport(int sampleCount, float maxPositionError, float maxPositionErrorTime)
        {
            SampleCount = sampleCount;
            MaxPositionError = maxPositionError;
            MaxPositionErrorTime = maxPositionErrorTime;
        }
    }

    /// <summary>Timeline とベイク clip の同時評価を行う呼び出し側から使う、純粋な誤差集計コア。</summary>
    internal static class TimelineBakePrecisionValidator
    {
        internal static TimelineBakePrecisionReport ValidatePosition(
            IEnumerable<float> sampleTimes,
            Func<float, Vector3> referencePosition,
            Func<float, Vector3> bakedPosition)
        {
            if (sampleTimes == null)
                throw new ArgumentNullException(nameof(sampleTimes));
            if (referencePosition == null)
                throw new ArgumentNullException(nameof(referencePosition));
            if (bakedPosition == null)
                throw new ArgumentNullException(nameof(bakedPosition));

            int samples = 0;
            float maxError = 0f;
            float worstTime = 0f;
            foreach (float time in sampleTimes)
            {
                float error = Vector3.Distance(referencePosition(time), bakedPosition(time));
                // 同値では更新せず、最初に観測した最大値の時刻を安定して返す。
                if (samples == 0 || error > maxError)
                {
                    maxError = error;
                    worstTime = time;
                }
                samples++;
            }
            return new TimelineBakePrecisionReport(samples, maxError, worstTime);
        }
    }
}
#endif
