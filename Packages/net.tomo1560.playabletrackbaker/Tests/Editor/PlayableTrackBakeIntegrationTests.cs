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
        string _generatedClipPath;

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
            if (!string.IsNullOrEmpty(_generatedClipPath))
                AssetDatabase.DeleteAsset(_generatedClipPath);
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
        public void TempCleanup_DeletesOnlyToolOwnedAssets()
        {
            string userPath = PlayableTrackBakeSceneProcessor.TempFolder + "/UserAsset.playable";
            string generatedPath = PlayableTrackBakeSceneProcessor.TempFolder + "/Generated.playable";
            var userAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            var generatedAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(userAsset, userPath);
            AssetDatabase.CreateAsset(generatedAsset, generatedPath);
            AssetDatabase.SetLabels(generatedAsset, new[] { PlayableTrackBakeTempCleanup.OwnershipLabel });
            AssetDatabase.SaveAssets();

            PlayableTrackBakeTempCleanup.Cleanup();

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TimelineAsset>(userPath),
                "固定一時フォルダ内のユーザー資産を削除してはならない");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<TimelineAsset>(generatedPath),
                "所有ラベル付きのツール生成物は削除されるべき");
            Assert.IsTrue(AssetDatabase.IsValidFolder(PlayableTrackBakeSceneProcessor.TempFolder));
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

        [Test]
        public void DestructiveBake_FailureRollsBackAssetsTimelineAnimatorAndBinding()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            var playableTrack = timeline.CreateTrack<PlayableTrack>(null, "Custom Playable");
            playableTrack.CreateClip<SineMoveTestPlayableAsset>().duration = 1.0;
            var previousBakedTrack = timeline.CreateTrack<AnimationTrack>(null, "[Baked] Previous");
            var previousClip = new AnimationClip { name = PlayableTrackBakeCore.OwnershipSignature + "Previous" };
            previousBakedTrack.CreateClip(previousClip);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 1.0;

            _target = new GameObject("RollbackTarget");
            _directorObject = new GameObject("RollbackDirector");
            var director = _directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var marker = _directorObject.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { _target };
            marker.frameRate = 10f;

            PlayableTrackBakeCore.EnsureFolder();
            string clipPath = PlayableTrackBakeCore.BuildClipAssetPath(director, timeline, _target, 0);
            _generatedClipPath = clipPath;
            AssetDatabase.DeleteAsset(clipPath);
            var existing = new AnimationClip();
            existing.SetCurve("", typeof(Transform), "m_LocalPosition.x",
                AnimationCurve.Constant(0f, 1f, 123f));
            AssetDatabase.CreateAsset(existing, clipPath);

            PlayableTrackBaker.BakeDestructiveFailureInjection = () =>
                throw new System.InvalidOperationException("injected failure");
            try
            {
                Assert.Throws<System.InvalidOperationException>(() =>
                    PlayableTrackBaker.BakeDestructive(marker));
            }
            finally
            {
                PlayableTrackBaker.BakeDestructiveFailureInjection = null;
            }

            var restoredCurve = AnimationUtility.GetEditorCurve(existing,
                EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"));
            Assert.AreEqual(123f, restoredCurve.Evaluate(0.5f), 0.001f,
                "既存 AnimationClip の内容を復元すべき");
            var restoredBakedTracks = timeline.GetOutputTracks()
                .Where(PlayableTrackBakeCore.IsBakedTrackOwnedByTool).ToArray();
            Assert.AreEqual(1, restoredBakedTracks.Length,
                "途中追加したトラックを除去し、既存ベイクトラックを復元すべき");
            Assert.AreEqual("[Baked] Previous", restoredBakedTracks[0].name);
            Assert.IsNull(_target.GetComponent<Animator>(), "途中追加した Animator を除去すべき");
            Assert.IsFalse(playableTrack.muted, "元トラックの mute 状態を復元すべき");

        }
    }
}
