#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UdonSharp;
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

        [MenuItem("Tools/Timeline/Create Signal Event Test Setup")]
        static void Create()
        {
            var existing = GameObject.Find(RootName);
            if (existing != null)
                Object.DestroyImmediate(existing);
            EnsureFolder();
            EnsureReceiverProgramAsset();
            AssetDatabase.DeleteAsset(TimelinePath);

            var root = new GameObject(RootName);
            var host = GameObject.CreatePrimitive(PrimitiveType.Cube);
            host.name = "SignalEventHost";
            host.transform.SetParent(root.transform, false);
            host.transform.localPosition = new Vector3(0f, 1f, 2f);
            var receiver = host.AddComponent<SignalEventReceiver>();
            var serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("indicator").objectReferenceValue = host.GetComponent<Renderer>();
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();

            var directorObject = new GameObject("SignalEventDirector");
            directorObject.transform.SetParent(root.transform, false);
            var director = directorObject.AddComponent<PlayableDirector>();
            director.playOnAwake = true;
            director.extrapolationMode = DirectorWrapMode.Hold;

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
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
            var marker = directorObject.AddComponent<TimelineBakeMarker>();
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
            }
            program.sourceCsScript = script;
            EditorUtility.SetDirty(program);
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.CompileAllCsPrograms(forceCompile: true);

            if (UdonSharpProgramAsset.GetProgramAssetForClass(typeof(SignalEventReceiver)) == null)
                throw new System.InvalidOperationException(
                    "SignalEventReceiver の UdonSharp program asset を生成できませんでした。Console の UdonSharp コンパイルエラーを解消してから再試行してください。");
        }
    }
}
#endif
