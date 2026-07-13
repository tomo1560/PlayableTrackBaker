using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PlayableTrackBaking.Tests
{
    /// <summary>
    /// 手動ベイクのキャンセル境界と、ベイク済みクリップの軽量な性能分析 API を固定する。
    /// 実装は Editor UI に依存させず、コールバックと AnimationClip だけで検証可能にする。
    /// </summary>
    [TestFixture]
    public class PlayableTrackBakeProgressAndPerformanceTests
    {
        const string TestFolder = "Assets/__PlayableTrackBakerProgressTests";
        readonly List<UnityEngine.Object> _cleanup = new List<UnityEngine.Object>();
        readonly List<string> _assetPaths = new List<string>();

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.CreateFolder("Assets", "__PlayableTrackBakerProgressTests");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var assetPath in _assetPaths)
                AssetDatabase.DeleteAsset(assetPath);
            _assetPaths.Clear();

            foreach (var obj in _cleanup)
                if (obj != null)
                    UnityEngine.Object.DestroyImmediate(obj);
            _cleanup.Clear();

            if (AssetDatabase.IsValidFolder(TestFolder) &&
                AssetDatabase.FindAssets(string.Empty, new[] { TestFolder }).Length == 0)
                AssetDatabase.DeleteAsset(TestFolder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void PerformanceAnalysis_CountsFloatAndObjectReferenceCurvesAndKeys()
        {
            var clip = new AnimationClip { name = "PerformanceAnalysis" };
            _cleanup.Add(clip);
            // Unity 組み込み Component の scalar プロパティを使う。テストアセンブリ内の
            // MonoBehaviour は MonoScript として解決されず、AnimationClip binding に保存できない。
            clip.SetCurve("", typeof(AudioSource), "m_Volume", new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f)));
            clip.SetCurve("Child", typeof(AudioSource), "m_Pitch", new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 2f)));
            AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"),
                new[]
                {
                    new ObjectReferenceKeyframe { time = 0f, value = null },
                    new ObjectReferenceKeyframe { time = 1f, value = null },
                });

            var report = AnimationClipPerformanceAnalyzer.Analyze(clip);

            Assert.AreEqual(2, report.FloatCurveCount);
            Assert.AreEqual(1, report.ObjectReferenceCurveCount);
            Assert.AreEqual(3, report.TotalCurveCount);
            Assert.AreEqual(5, report.FloatKeyCount);
            Assert.AreEqual(2, report.ObjectReferenceKeyCount);
            Assert.AreEqual(7, report.TotalKeyCount);
            Assert.Greater(report.EstimatedSizeBytes, 0,
                "キーを含むクリップの推定サイズは正の値であるべき");
        }

        [Test]
        public void PerformanceAnalysis_EmptyClipHasZeroCountsAndSize()
        {
            var clip = new AnimationClip { name = "EmptyPerformanceAnalysis" };
            _cleanup.Add(clip);

            var report = AnimationClipPerformanceAnalyzer.Analyze(clip);

            Assert.AreEqual(0, report.FloatCurveCount);
            Assert.AreEqual(0, report.ObjectReferenceCurveCount);
            Assert.AreEqual(0, report.TotalKeyCount);
            Assert.AreEqual(0, report.EstimatedSizeBytes);
        }

        [Test]
        public void PerformanceAnalysis_RejectsNullClip()
        {
            Assert.Throws<ArgumentNullException>(() => AnimationClipPerformanceAnalyzer.Analyze(null));
        }

        [Test]
        public void RunBake_StopsBeforeNextTimelineWhenProgressCallbackCancels()
        {
            var first = CreateBakeableMarker("First");
            var second = CreateBakeableMarker("Second");
            var progress = new List<BakeProgress>();

            int baked = PlayableTrackBaker.RunBake(new[] { first.marker, second.marker }, update =>
            {
                progress.Add(update);
                return update.CompletedCount < 1;
            });

            // RunBake が作る固定出力アセットも、このテストだけの生成物として掃除する。
            _assetPaths.Add(PlayableTrackBakeCore.BuildClipAssetPath(
                first.marker.director, first.timeline, first.marker.recordRoots[0], 0));
            _assetPaths.Add(PlayableTrackBakeCore.BuildClipAssetPath(
                second.marker.director, second.timeline, second.marker.recordRoots[0], 0));

            Assert.AreEqual(1, baked, "最初の Timeline の成功後にキャンセルした場合、後続を成功数に含めない");
            Assert.AreEqual(1, progress.Count, "完了済み Timeline ごとに進捗を通知する");
            Assert.AreEqual(1, progress[0].CompletedCount);
            Assert.AreEqual(2, progress[0].TotalCount);
            Assert.IsTrue(first.timeline.GetOutputTracks().Any(PlayableTrackBakeCore.IsBakedTrackOwnedByTool),
                "キャンセル前に完了した Timeline のベイク結果は保持する");
            Assert.IsFalse(second.timeline.GetOutputTracks().Any(PlayableTrackBakeCore.IsBakedTrackOwnedByTool),
                "キャンセル後の Timeline を変更してはならない");
        }

        [Test]
        public void Record_CancelsBeforeSamplingAndRestoresPlayableTrackMuteState()
        {
            var rig = CreateBakeableMarker("RecordCancellation");
            var track = rig.timeline.GetOutputTracks().OfType<PlayableTrack>().Single();
            track.muted = true;

            Assert.Throws<BakeCancelledException>(() =>
                PlayableTrackBakeCore.Record(rig.marker.director, rig.timeline, rig.marker, _ => false));

            Assert.IsTrue(track.muted,
                "サンプル中止時にも、記録のため一時的に解除した mute 状態を復元するべき");
        }

        (TimelineBakeMarker marker, TimelineAsset timeline) CreateBakeableMarker(string suffix)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            string timelinePath = $"{TestFolder}/{suffix}_{GUID.Generate()}.playable";
            AssetDatabase.CreateAsset(timeline, timelinePath);
            _assetPaths.Add(timelinePath);
            timeline.CreateTrack<PlayableTrack>(null, "Custom Playable")
                .CreateClip<SineMoveTestPlayableAsset>().duration = 1.0;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 0.1;

            var target = new GameObject($"ProgressTarget_{suffix}");
            _cleanup.Add(target);
            var directorObject = new GameObject($"ProgressDirector_{suffix}");
            _cleanup.Add(directorObject);
            var director = directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var marker = directorObject.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { target };
            marker.frameRate = 10f;
            marker.highPrecision = true;
            return (marker, timeline);
        }
    }
}
