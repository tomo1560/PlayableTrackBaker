using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PlayableTrackBaking.Tests
{
    /// <summary>AssetDatabase を含む非破壊ビルド経路と、失敗時の状態復元を検証する。</summary>
    [TestFixture]
    public class PlayableTrackBakeIntegrationTests
    {
        const string TestFolder = "Assets/__PlayableTrackBakerTests";
        const string TimelinePath = TestFolder + "/Source.playable";

        GameObject _target;
        GameObject _directorObject;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
                AssetDatabase.CreateFolder("Assets", "__PlayableTrackBakerTests");
            PlayableTrackBakeSceneProcessor.EnsureTempFolder();
        }

        [TearDown]
        public void TearDown()
        {
            if (_directorObject != null)
                Object.DestroyImmediate(_directorObject);
            if (_target != null)
                Object.DestroyImmediate(_target);
            AssetDatabase.DeleteAsset(TestFolder);
            AssetDatabase.DeleteAsset(PlayableTrackBakeSceneProcessor.TempFolder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void NonDestructiveBake_ClonesTimelineAndLeavesSourceUnchanged()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            var playableTrack = timeline.CreateTrack<PlayableTrack>(null, "Custom Playable");
            var playableClip = playableTrack.CreateClip<SineMoveTestPlayableAsset>();
            playableClip.duration = 1.5;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 1.0;
            AssetDatabase.SaveAssets();

            _target = new GameObject("IntegrationTarget");
            _directorObject = new GameObject("IntegrationDirector");
            var director = _directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var marker = _directorObject.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { _target };
            marker.highPrecision = true;
            marker.frameRate = 10f;

            Assert.IsTrue(PlayableTrackBakeSceneProcessor.BakeNonDestructive(
                director, new[] { marker }));

            var clone = director.playableAsset as TimelineAsset;
            Assert.IsNotNull(clone);
            Assert.AreNotSame(timeline, clone);
            Assert.IsFalse(timeline.GetOutputTracks().Any(t =>
                t.name.StartsWith(PlayableTrackBakeCore.BakedTrackPrefix)));
            Assert.IsTrue(clone.GetOutputTracks().Any(t =>
                t.name.StartsWith(PlayableTrackBakeCore.BakedTrackPrefix)));
            Assert.IsFalse(playableTrack.muted, "元 Timeline の mute 状態を変更してはならない");

            string clonePath = AssetDatabase.GetAssetPath(clone);
            StringAssert.StartsWith(PlayableTrackBakeSceneProcessor.TempFolder, clonePath);
            Assert.IsTrue(AssetDatabase.LoadAllAssetsAtPath(clonePath).OfType<AnimationClip>().Any(),
                "ベイククリップはクローン Timeline のサブアセットであるべき");
        }

        [Test]
        public void RecordFailure_RestoresPlayableTrackMuteState()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var track = timeline.CreateTrack<PlayableTrack>();
            track.muted = true;
            _directorObject = new GameObject("FailureDirector");
            var marker = _directorObject.AddComponent<TimelineBakeMarker>();
            marker.recordRoots = new GameObject[0];

            Assert.Throws<System.NullReferenceException>(() =>
                PlayableTrackBakeCore.Record(null, timeline, marker));
            Assert.IsTrue(track.muted, "記録失敗時は元の mute 状態へ戻すべき");

            Object.DestroyImmediate(timeline);
        }
    }
}
