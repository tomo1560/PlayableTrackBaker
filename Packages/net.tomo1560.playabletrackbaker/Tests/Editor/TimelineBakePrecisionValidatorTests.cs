using NUnit.Framework;
using UnityEngine;

namespace PlayableTrackBaking.Tests
{
    [TestFixture]
    public class TimelineBakePrecisionValidatorTests
    {
        [Test]
        public void ValidatePosition_ExactMatchReportsZeroError()
        {
            var report = TimelineBakePrecisionValidator.ValidatePosition(
                new[] { 0f, 0.25f, 0.5f, 0.75f, 1f },
                time => new Vector3(time * 2f, 0f, 0f),
                time => new Vector3(time * 2f, 0f, 0f));

            Assert.AreEqual(5, report.SampleCount);
            Assert.That(report.MaxPositionError, Is.EqualTo(0f).Within(0.00001f));
            Assert.That(report.MaxPositionErrorTime, Is.EqualTo(0f).Within(0.00001f));
        }

        [Test]
        public void ValidatePosition_NonZeroErrorReportsFirstWorstSampleTime()
        {
            var report = TimelineBakePrecisionValidator.ValidatePosition(
                new[] { 0f, 0.25f, 0.5f, 0.75f, 1f },
                time => new Vector3(Mathf.Sin(Mathf.PI * 2f * time), 0f, 0f),
                _ => Vector3.zero);

            Assert.AreEqual(5, report.SampleCount);
            Assert.That(report.MaxPositionError, Is.GreaterThan(0.9f));
            Assert.That(report.MaxPositionErrorTime, Is.EqualTo(0.25f).Within(0.00001f),
                "同値の最大誤差では、最初に観測したサンプル時刻を返すべき");
        }
    }
}
