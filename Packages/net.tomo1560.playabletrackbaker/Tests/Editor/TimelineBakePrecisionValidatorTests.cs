using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

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

        [Test]
        public void ValidationRunner_UsesNeverActivatedGhostAndRestoresDirectorState()
        {
            var root = new GameObject("PrecisionValidationLifecycleRoot");
            var directorObject = new GameObject("PrecisionValidationLifecycleDirector");
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var clip = new AnimationClip();
            try
            {
                timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
                timeline.fixedDuration = 1.0;
                var director = directorObject.AddComponent<PlayableDirector>();
                director.playableAsset = timeline;
                root.AddComponent<PrecisionValidationLifecycleProbe>();
                clip.SetCurve(string.Empty, typeof(Transform), "m_LocalPosition.x",
                    AnimationCurve.Linear(0f, 0f, 1f, 1f));
                director.time = 0.375;
                director.Evaluate();
                Vector3 originalPosition = root.transform.localPosition;
                PrecisionValidationLifecycleProbe.ResetCounters();
                int ghostCountBefore = CountValidationGhostObjects();

                var report = TimelineBakePrecisionValidationRunner.ValidatePosition(
                    director, timeline, clip, root, 10f);

                Assert.Greater(report.SampleCount, 1);
                Assert.AreEqual(0, PrecisionValidationLifecycleProbe.AwakeCount,
                    "ghost の Awake を発生させてはならない");
                Assert.AreEqual(0, PrecisionValidationLifecycleProbe.EnableCount,
                    "ghost の OnEnable を発生させてはならない");
                Assert.AreEqual(0, PrecisionValidationLifecycleProbe.DisableCount,
                    "ghost の OnDisable を発生させてはならない");
                Assert.AreEqual(0, PrecisionValidationLifecycleProbe.DestroyCount,
                    "一度も active にしていない ghost の OnDestroy を発生させてはならない");
                Assert.That(director.time, Is.EqualTo(0.375).Within(0.000001),
                    "検証後に Director の時刻を復元するべき");
                Assert.That(root.transform.localPosition, Is.EqualTo(originalPosition),
                    "検証後に source root の状態を復元するべき");
                Assert.AreEqual(ghostCountBefore, CountValidationGhostObjects(),
                    "検証用 container/ghost をリークしてはならない");
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(directorObject);
                Object.DestroyImmediate(root);
            }
        }

        static int CountValidationGhostObjects()
            => Resources.FindObjectsOfTypeAll<GameObject>()
                .Count(gameObject => gameObject != null &&
                    gameObject.name.StartsWith("[PrecisionValidation]"));
    }

    [ExecuteAlways]
    sealed class PrecisionValidationLifecycleProbe : MonoBehaviour
    {
        internal static int AwakeCount { get; private set; }
        internal static int EnableCount { get; private set; }
        internal static int DisableCount { get; private set; }
        internal static int DestroyCount { get; private set; }

        internal static void ResetCounters()
        {
            AwakeCount = 0;
            EnableCount = 0;
            DisableCount = 0;
            DestroyCount = 0;
        }

        void Awake() => AwakeCount++;
        void OnEnable() => EnableCount++;
        void OnDisable() => DisableCount++;
        void OnDestroy() => DestroyCount++;
    }
}
