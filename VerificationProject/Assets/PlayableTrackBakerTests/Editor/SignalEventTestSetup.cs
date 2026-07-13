#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UdonSharp;
using UdonSharpEditor;
using PlayableTrackBaking;
using PlayableTrackBaking.Samples;
using PlayableTrackBaking.Verification;

namespace PlayableTrackBaking.Tests
{
    /// <summary>Build & Test で Signal AnimationEvent を確認する最小セットを生成する。</summary>
    static class SignalEventTestSetup
    {
        const string RootName = "SignalEventTest_Root";
        const string Folder = "Assets/PlayableTrackBakerTests/Generated";
        const string TimelinePath = Folder + "/SignalEventTest_Timeline.playable";
        const string ReceiverScriptPath = "Assets/PlayableTrackBakerTests/SignalEventReceiver.cs";
        const string ReceiverProgramPath = "Assets/PlayableTrackBakerTests/SignalEventReceiver.asset";
        const string OwnershipSignature = "PlayableTrackBaker.SignalEventTestSetup/v1:3f84fe19";
        const string OwnershipMarkerName = "__PlayableTrackBaker_SignalEventTestSetup_3f84fe19__";

        [MenuItem("Tools/Timeline/Create Signal Event Test Setup")]
        static void Create()
        {
            // Validate every fixed-name output before changing anything. Artifacts made by an
            // older setup intentionally have no signature and must be handled by the user.
            var existingRoots = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(candidate => candidate.name == RootName && candidate.scene.IsValid())
                .ToArray();
            foreach (var existingRoot in existingRoots)
                RequireOwnedSceneRoot(existingRoot);
            RequireOwnedAssetIfPresent(TimelinePath);
            RequireOwnedAssetIfPresent(ReceiverProgramPath);

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Signal Event Test Setup");
            foreach (var existingRoot in existingRoots)
                Undo.DestroyObjectImmediate(existingRoot);

            EnsureFolder();
            EnsureReceiverProgramAsset();
            if (AssetDatabase.LoadMainAssetAtPath(TimelinePath) != null && !AssetDatabase.DeleteAsset(TimelinePath))
                throw new InvalidOperationException($"所有済み TimelineAsset を削除できませんでした: {TimelinePath}");

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Signal Event Test Setup Root");
            var ownershipMarker = new GameObject(OwnershipMarkerName)
            {
                hideFlags = HideFlags.HideInHierarchy
            };
            Undo.RegisterCreatedObjectUndo(ownershipMarker, "Create Signal Event Test Setup Ownership Marker");
            Undo.SetTransformParent(ownershipMarker.transform, root.transform, "Parent Ownership Marker");

            var host = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(host, "Create Signal Event Host");
            host.name = "SignalEventHost";
            Undo.SetTransformParent(host.transform, root.transform, "Parent Signal Event Host");
            host.transform.localPosition = new Vector3(0f, 1f, 2f);
            // UdonSharpBehaviour を通常の AddComponent で追加すると backing UdonBehaviour が作られず、
            // AnimationEvent の SendCustomEvent を受信できない。UdonSharp の Undo 対応 API を使う。
            var receiver = UdonSharpUndo.AddComponent<SignalEventReceiver>(host);
            var serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("indicator").objectReferenceValue = host.GetComponent<Renderer>();
            serializedReceiver.ApplyModifiedProperties();

            var directorObject = new GameObject("SignalEventDirector");
            Undo.RegisterCreatedObjectUndo(directorObject, "Create Signal Event Director");
            Undo.SetTransformParent(directorObject.transform, root.transform, "Parent Signal Event Director");
            var director = Undo.AddComponent<PlayableDirector>(directorObject);
            director.playOnAwake = true;
            director.extrapolationMode = DirectorWrapMode.Hold;

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            SetAssetOwnership(TimelinePath);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 2d;

            var playableTrack = timeline.CreateTrack<PlayableTrack>(null, "Required Custom Playable");
            var playableClip = playableTrack.CreateClip<MoveSamplePlayableAsset>();
            playableClip.start = 0d;
            playableClip.duration = 2d;
            var playableAsset = (MoveSamplePlayableAsset)playableClip.asset;
            playableAsset.amplitude = 0f;
            playableAsset.frequency = 1f;
            var exposedName = new PropertyName(GUID.Generate().ToString());
            playableAsset.target = new ExposedReference<Transform> { exposedName = exposedName };
            director.SetReferenceValue(exposedName, host.transform);

            timeline.CreateMarkerTrack();
            var openSignal = CreateSignal(timeline, "OpenDoor");
            var unlockSignal = CreateSignal(timeline, "UnlockPuzzle");
            CreateEmitter(timeline.markerTrack, 0.5d, openSignal);
            CreateEmitter(timeline.markerTrack, 1.5d, unlockSignal);

            director.playableAsset = timeline;
            var marker = Undo.AddComponent<TimelineBakeMarker>(directorObject);
            marker.director = director;
            marker.recordRoots = new[] { host };
            marker.frameRate = 30f;
            marker.mutePlayableTracksAfterBake = true;
            marker.bakeSignalEvents = true;
            marker.signalEventHost = host;
            marker.signalEventRoutes = new[]
            {
                new SignalEventRoute(openSignal, nameof(SignalEventReceiver.OnOpenDoor)),
                new SignalEventRoute(unlockSignal, nameof(SignalEventReceiver.OnUnlockPuzzle)),
            };

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = directorObject;
            Debug.Log(
                "[SignalEventTestSetup] 生成完了。Build & Test 後、Console に OnOpenDoor (1)、OnUnlockPuzzle (2) の順で各1回出れば成功です。",
                directorObject);
        }

        static SignalAsset CreateSignal(TimelineAsset timeline, string name)
        {
            var signal = ScriptableObject.CreateInstance<SignalAsset>();
            signal.name = name;
            AssetDatabase.AddObjectToAsset(signal, timeline);
            return signal;
        }

        static void CreateEmitter(TrackAsset track, double time, SignalAsset signal)
        {
            var emitter = track.CreateMarker<SignalEmitter>(time);
            emitter.asset = signal;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PlayableTrackBakerTests"))
                AssetDatabase.CreateFolder("Assets", "PlayableTrackBakerTests");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/PlayableTrackBakerTests", "Generated");
        }

        static void EnsureReceiverProgramAsset()
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ReceiverScriptPath);
            if (script == null)
                throw new System.InvalidOperationException($"SignalEventReceiver script が見つかりません: {ReceiverScriptPath}");

            var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ReceiverProgramPath);
            if (program == null)
            {
                program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                AssetDatabase.CreateAsset(program, ReceiverProgramPath);
                SetAssetOwnership(ReceiverProgramPath);
                program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ReceiverProgramPath);
                if (program == null)
                    throw new InvalidOperationException($"生成した UdonSharp program asset を再読込できませんでした: {ReceiverProgramPath}");
            }
            program.sourceCsScript = script;
            EditorUtility.SetDirty(program);
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.CompileAllCsPrograms(forceCompile: true);

            if (UdonSharpProgramAsset.GetProgramAssetForClass(typeof(SignalEventReceiver)) == null)
                throw new System.InvalidOperationException(
                    "SignalEventReceiver の UdonSharp program asset を生成できませんでした。Console の UdonSharp コンパイルエラーを解消してから再試行してください。");
        }

        // Kept separate from Unity object lookup so exact signature matching can be unit-tested.
        internal static bool HasOwnershipSignature(string signature)
        {
            return string.Equals(signature, OwnershipSignature, StringComparison.Ordinal);
        }

        internal static bool HasOwnershipMarkerName(string markerName)
        {
            return string.Equals(markerName, OwnershipMarkerName, StringComparison.Ordinal);
        }

        static void RequireOwnedSceneRoot(GameObject root)
        {
            if (!IsOwnedSceneRoot(root))
                throw new InvalidOperationException(
                    $"同名の非所有 GameObject があるため中断しました: {RootName}。名前を変更するか手動で削除してから再試行してください。");
        }

        static void RequireOwnedAssetIfPresent(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                return;

            if (!IsOwnedAsset(path))
                throw new InvalidOperationException(
                    $"固定パスに非所有アセットがあるため中断しました: {path}。移動または手動削除してから再試行してください。");
        }

        internal static bool IsOwnedSceneRoot(GameObject root)
            => root != null && root.transform.Cast<Transform>()
                .Any(child => HasOwnershipMarkerName(child.name));

        internal static bool IsOwnedAsset(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadMainAssetAtPath(path) == null)
                return false;
            var importer = AssetImporter.GetAtPath(path);
            return importer != null && HasOwnershipSignature(importer.userData);
        }

        static void SetAssetOwnership(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null)
                throw new InvalidOperationException($"生成アセットの importer を取得できませんでした: {path}");

            importer.userData = OwnershipSignature;
            importer.SaveAndReimport();
        }
    }
}
#endif
