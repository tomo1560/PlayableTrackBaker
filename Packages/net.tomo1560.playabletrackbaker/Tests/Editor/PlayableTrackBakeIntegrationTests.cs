using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace PlayableTrackBaking.Tests
{
    /// <summary>AssetDatabase を含む非破壊ビルド経路と、失敗時の状態復元を検証する。</summary>
    [TestFixture]
    public class PlayableTrackBakeIntegrationTests
    {
        const string TestFolder = "Assets/__PlayableTrackBakerTests";

        GameObject _target;
        GameObject _directorObject;
        string _generatedClipPath;
        string _assetSuffix;
        readonly List<string> _createdAssetPaths = new List<string>();
        string TimelinePath => $"{TestFolder}/Source_{_assetSuffix}.playable";
        string UnrelatedTimelinePath => $"{TestFolder}/Unrelated_{_assetSuffix}.playable";
        string TempUserAssetPath => $"{PlayableTrackBakeSceneProcessor.TempFolder}/UserAsset_{_assetSuffix}.playable";
        string TempGeneratedAssetPath => $"{PlayableTrackBakeSceneProcessor.TempFolder}/Generated_{_assetSuffix}.playable";

        [SetUp]
        public void SetUp()
        {
            _assetSuffix = GUID.Generate().ToString();
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
            foreach (string path in _createdAssetPaths.Distinct())
                AssetDatabase.DeleteAsset(path);
            _createdAssetPaths.Clear();
            // テスト自身が作成した既知のアセットだけを削除する。固定フォルダ全体を消すと、
            // 利用者が同じ場所へ置いた無関係なアセットを巻き込むため削除してはならない。
            AssetDatabase.DeleteAsset(TimelinePath);
            AssetDatabase.DeleteAsset(UnrelatedTimelinePath);
            AssetDatabase.DeleteAsset(TempUserAssetPath);
            AssetDatabase.DeleteAsset(TempGeneratedAssetPath);
            if (AssetDatabase.IsValidFolder(TestFolder) &&
                AssetDatabase.FindAssets(string.Empty, new[] { TestFolder }).Length == 0)
                AssetDatabase.DeleteAsset(TestFolder);
            AssetDatabase.Refresh();
            DestroyLeakedTestObjects();
        }

        // NUnit の途中中断や Unity の例外でフィールド参照が失われた場合にも、検証シーンへ
        // テスト用 GameObject を残さないための最後の安全網。利用者オブジェクトには触れない。
        static void DestroyLeakedTestObjects()
        {
            foreach (var name in new[] { "RunBakeTarget", "RunBakeSuccessDirector" })
            {
                var testObject = GameObject.Find(name);
                if (testObject != null)
                    Object.DestroyImmediate(testObject);
            }
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
            _createdAssetPaths.Add(clonePath);
            StringAssert.StartsWith(PlayableTrackBakeSceneProcessor.TempFolder, clonePath);
            Assert.IsTrue(AssetDatabase.LoadAllAssetsAtPath(clonePath).OfType<AnimationClip>().Any(),
                "ベイククリップはクローン Timeline のサブアセットであるべき");
        }

        [Test]
        public void NonDestructiveBake_ConfiguredSignalEventIsAddedToCloneHostClipAndLeavesSourceUnchanged()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            var playableTrack = timeline.CreateTrack<PlayableTrack>(null, "Custom Playable");
            playableTrack.CreateClip<SineMoveTestPlayableAsset>().duration = 1.0;
            timeline.CreateMarkerTrack();
            var signal = ScriptableObject.CreateInstance<SignalAsset>();
            signal.name = "OpenDoor";
            AssetDatabase.AddObjectToAsset(signal, timeline);
            var emitter = timeline.markerTrack.CreateMarker<SignalEmitter>(0.75);
            emitter.asset = signal;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 1.0;
            AssetDatabase.SaveAssets();

            _target = new GameObject("SignalEventHost");
            _directorObject = new GameObject("SignalIntegrationDirector");
            var director = _directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var marker = _directorObject.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { _target };
            marker.highPrecision = true;
            marker.frameRate = 10f;
            marker.bakeSignalEvents = true;
            marker.signalEventHost = _target;
            marker.signalEventRoutes = new[] { new SignalEventRoute(signal, "OnOpenDoor") };

            Assert.IsTrue(PlayableTrackBakeSceneProcessor.BakeNonDestructive(
                director, new[] { marker }));

            var clone = director.playableAsset as TimelineAsset;
            Assert.IsNotNull(clone);
            Assert.AreNotSame(timeline, clone);
            string clonePath = AssetDatabase.GetAssetPath(clone);
            _createdAssetPaths.Add(clonePath);

            var hostClip = clone.GetOutputTracks()
                .OfType<AnimationTrack>()
                .Where(PlayableTrackBakeCore.IsBakedTrackOwnedByTool)
                .SelectMany(track => track.GetClips())
                .Select(timelineClip => (timelineClip.asset as AnimationPlayableAsset)?.clip)
                .Single(clip => clip != null);
            var events = hostClip.events;
            Assert.AreEqual(1, events.Length);
            Assert.AreEqual(0.75f, events[0].time, 0.0001f);
            Assert.AreEqual("SendCustomEvent", events[0].functionName);
            Assert.AreEqual("OnOpenDoor", events[0].stringParameter);

            Assert.IsFalse(timeline.GetOutputTracks().Any(PlayableTrackBakeCore.IsBakedTrackOwnedByTool),
                "非破壊ベイクは元 Timeline にベイク済み Track を追加してはならない");
            Assert.AreSame(signal, timeline.markerTrack.GetMarkers().OfType<SignalEmitter>().Single().asset,
                "非破壊ベイクは元 Timeline の SignalEmitter を複製版へ差し替えてはならない");
            Assert.IsFalse(playableTrack.muted,
                "非破壊ベイクは元 Timeline の PlayableTrack をミュートしてはならない");
        }

        [Test]
        public void NonDestructiveBake_DoesNotRouteSignalFromUnrelatedTimelineWithSameLocalFileId()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            timeline.CreateTrack<PlayableTrack>(null, "Custom Playable")
                .CreateClip<SineMoveTestPlayableAsset>().duration = 1.0;
            timeline.CreateMarkerTrack();
            var emittedSignal = ScriptableObject.CreateInstance<SignalAsset>();
            emittedSignal.name = "EmittedSignal";
            AssetDatabase.AddObjectToAsset(emittedSignal, timeline);
            timeline.markerTrack.CreateMarker<SignalEmitter>(0.5).asset = emittedSignal;

            AssetDatabase.SaveAssets();
            Assert.IsTrue(AssetDatabase.CopyAsset(TimelinePath, UnrelatedTimelinePath),
                "CopyAsset は SignalAsset subasset の local file ID を保持する前提");
            var unrelatedTimeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(UnrelatedTimelinePath);
            var unrelatedSignal = AssetDatabase.LoadAllAssetsAtPath(UnrelatedTimelinePath)
                .OfType<SignalAsset>()
                .Single();
            unrelatedSignal.name = "UnrelatedSignal";
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                emittedSignal, out _, out long emittedId));
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                unrelatedSignal, out _, out long unrelatedId));
            Assert.AreEqual(emittedId, unrelatedId,
                "この回帰テストは異なる Timeline 間で local file ID が衝突する条件を必要とする");

            _target = new GameObject("SignalEventHost");
            _directorObject = new GameObject("SignalCollisionDirector");
            var director = _directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var marker = _directorObject.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { _target };
            marker.bakeSignalEvents = true;
            marker.signalEventHost = _target;
            marker.signalEventRoutes = new[] { new SignalEventRoute(unrelatedSignal, "OnWrongSignal") };

            Assert.IsTrue(PlayableTrackBakeSceneProcessor.BakeNonDestructive(
                director, new[] { marker }));

            var clone = (TimelineAsset)director.playableAsset;
            _createdAssetPaths.Add(AssetDatabase.GetAssetPath(clone));
            var hostClip = clone.GetOutputTracks()
                .OfType<AnimationTrack>()
                .Where(PlayableTrackBakeCore.IsBakedTrackOwnedByTool)
                .SelectMany(track => track.GetClips())
                .Select(timelineClip => (timelineClip.asset as AnimationPlayableAsset)?.clip)
                .Single(clip => clip != null);
            Assert.IsEmpty(hostClip.events,
                "別 Timeline の同一 local file ID を持つ Signal route を clone emitter に誤適用してはならない");
        }

        [Test]
        public void BakeNonDestructive_RejectsInvalidInputsWithoutCreatingAssets()
        {
            Assert.IsFalse(PlayableTrackBakeSceneProcessor.BakeNonDestructive(null,
                new TimelineBakeMarker[0]));

            _directorObject = new GameObject("InvalidDirector");
            var director = _directorObject.AddComponent<PlayableDirector>();
            Assert.IsFalse(PlayableTrackBakeSceneProcessor.BakeNonDestructive(director,
                new TimelineBakeMarker[0]), "playableAsset 未設定なら処理しないはず");

            var noPlayableTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
            director.playableAsset = noPlayableTimeline;
            Assert.IsFalse(PlayableTrackBakeSceneProcessor.BakeNonDestructive(director,
                new TimelineBakeMarker[0]), "PlayableTrack がなければ処理しないはず");
            Object.DestroyImmediate(noPlayableTimeline);
        }

        [Test]
        public void SceneProcessor_EmptySceneDoesNotCreateTemporaryAssets()
        {
            var scene = SceneManager.GetActiveScene();
            Assert.IsFalse(scene.GetRootGameObjects().Any(root =>
                root.GetComponentInChildren<TimelineBakeMarker>(true) != null));

            new PlayableTrackBakeSceneProcessor().OnProcessScene(scene, null);

            Assert.AreEqual(0, AssetDatabase.FindAssets(
                $"l:{PlayableTrackBakeTempCleanup.OwnershipLabel}",
                new[] { PlayableTrackBakeSceneProcessor.TempFolder }).Length);
        }

        [Test]
        public void BakeNonDestructive_RejectsUnsavedTimelineAndEmptyRoots()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.CreateTrack<PlayableTrack>().CreateClip<SineMoveTestPlayableAsset>().duration = 1;
            _directorObject = new GameObject("UnsavedDirector");
            var director = _directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var marker = _directorObject.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new GameObject[0];

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("recordRoots"));
            Assert.IsFalse(PlayableTrackBakeSceneProcessor.BakeNonDestructive(director,
                new[] { marker }));

            marker.recordRoots = new[] { _directorObject };
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("保存済みアセット"));
            Assert.IsFalse(PlayableTrackBakeSceneProcessor.BakeNonDestructive(director,
                new[] { marker }));
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void RunBake_HandlesMissingDirectorsAndNonBakeableTimelines()
        {
            Assert.AreEqual(0, PlayableTrackBaker.RunBake(new TimelineBakeMarker[0]));

            _directorObject = new GameObject("RunBakeDirector");
            var marker = _directorObject.AddComponent<TimelineBakeMarker>();
            marker.recordRoots = new[] { _directorObject };
            Assert.AreEqual(0, PlayableTrackBaker.RunBake(new[] { marker }),
                "Timeline 未設定のDirectorはベイク成功に数えないはず");

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _directorObject.GetComponent<PlayableDirector>().playableAsset = timeline;
            Assert.AreEqual(0, PlayableTrackBaker.RunBake(new[] { marker }),
                "PlayableTrack のないTimelineはベイク成功に数えないはず");
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void RunBake_SuccessfullyPersistsClipAndBakedTrack()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            timeline.CreateTrack<PlayableTrack>().CreateClip<SineMoveTestPlayableAsset>().duration = 1;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 0.1;

            _target = new GameObject("RunBakeTarget");
            _directorObject = new GameObject("RunBakeSuccessDirector");
            var director = _directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var marker = _directorObject.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { _target };
            marker.frameRate = 10;

            PlayableTrackBakeCore.EnsureFolder();
            _generatedClipPath = PlayableTrackBakeCore.BuildClipAssetPath(
                director, timeline, _target, 0);
            AssetDatabase.DeleteAsset(_generatedClipPath);

            Assert.AreEqual(1, PlayableTrackBaker.RunBake(new[] { marker }));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimationClip>(_generatedClipPath));
            Assert.IsTrue(timeline.GetOutputTracks().Any(
                PlayableTrackBakeCore.IsBakedTrackOwnedByTool));
        }

        [Test]
        public void TempCleanup_PostprocessEntryDeletesOwnedAssetOnly()
        {
            var userAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            var generatedAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(userAsset, TempUserAssetPath);
            AssetDatabase.CreateAsset(generatedAsset, TempGeneratedAssetPath);
            AssetDatabase.SetLabels(generatedAsset,
                new[] { PlayableTrackBakeTempCleanup.OwnershipLabel });
            AssetDatabase.SaveAssets();

            new PlayableTrackBakeTempCleanup().OnPostprocessBuild(null);

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TimelineAsset>(TempUserAssetPath));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<TimelineAsset>(TempGeneratedAssetPath));
        }

        [Test]
        public void TempCleanup_DeletesOnlyToolOwnedAssets()
        {
            var userAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            var generatedAsset = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(userAsset, TempUserAssetPath);
            AssetDatabase.CreateAsset(generatedAsset, TempGeneratedAssetPath);
            AssetDatabase.SetLabels(generatedAsset, new[] { PlayableTrackBakeTempCleanup.OwnershipLabel });
            AssetDatabase.SaveAssets();

            PlayableTrackBakeTempCleanup.Cleanup();

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TimelineAsset>(TempUserAssetPath),
                "固定一時フォルダ内のユーザー資産を削除してはならない");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<TimelineAsset>(TempGeneratedAssetPath),
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
        public void DestructiveBake_UndoRestoresPlayableTrackMuteState()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            var playableTrack = timeline.CreateTrack<PlayableTrack>(null, "Custom Playable");
            playableTrack.CreateClip<SineMoveTestPlayableAsset>().duration = 1.0;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 1.0;

            _target = new GameObject("UndoMuteTarget");
            _directorObject = new GameObject("UndoMuteDirector");
            var director = _directorObject.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            var marker = _directorObject.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { _target };
            marker.frameRate = 10f;
            marker.mutePlayableTracksAfterBake = true;
            _generatedClipPath = PlayableTrackBakeCore.BuildClipAssetPath(
                director, timeline, _target, 0);

            Assert.IsTrue(PlayableTrackBaker.BakeDestructive(marker));
            Assert.IsTrue(playableTrack.muted, "Bake 後は PlayableTrack がミュートされるはず");

            Undo.PerformUndo();

            Assert.IsFalse(playableTrack.muted,
                "Bake の Undo で PlayableTrack の元の mute 状態を復元すべき");
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
