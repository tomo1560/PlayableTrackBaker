using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using PlayableTrackBaking;

namespace GAIALyricsMovie.Tests
{
    public sealed class GeneratedSceneTests
    {
        const string ScenePath = "Assets/GAIALyricsMovie/Scenes/GAIA_LyricsMovie.unity";
        const string AudioPath = "Assets/GAIALyricsMovie/Audio/GAIA.ogg";
        const string SignalProgramPath = "Assets/GAIALyricsMovie/Signals/GAIASignalEventReceiver.asset";
        const string ShowControllerProgramPath = "Assets/GAIALyricsMovie/Udon/GAIAShowController.asset";
        const string TempBakeFolder = "Assets/BakedTimelineClips/__ndbake_temp__";
        static readonly double[] SignalTimes = { 56.52d, 153.24d };
        static readonly string[] SignalEventNames = { "OnChorusPulse", "OnFinaleBloom" };

        [Test]
        public void GeneratedScene_IsBuildReadyForAutomaticNonDestructiveBake()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            PlayableDirector director = Object.FindObjectOfType<PlayableDirector>(true);
            Assert.That(director, Is.Not.Null);
            Assert.That(director.playOnAwake, Is.False,
                "再生開始は▶ボタンのGAIAShowControllerが同期制御するため、playOnAwakeは無効のはずです。");

            var timeline = director.playableAsset as TimelineAsset;
            Assert.That(timeline, Is.Not.Null);
            Assert.That(timeline.fixedDuration, Is.EqualTo(221.504d).Within(0.001d));
            Assert.That(timeline.GetOutputTracks().OfType<AudioTrack>().Count(), Is.EqualTo(1));
            Assert.That(timeline.GetOutputTracks().OfType<PlayableTrack>().Single().muted, Is.False);
            Assert.That(timeline.GetOutputTracks().OfType<AnimationTrack>()
                .Count(track => track.name.StartsWith("[Baked]")), Is.Zero);

            TrackAsset signalTrack = timeline.markerTrack;
            Assert.That(signalTrack, Is.Not.Null);
            Assert.That(signalTrack.name, Is.EqualTo("GAIA Signal Cues"));
            SignalEmitter[] signalEmitters = signalTrack.GetMarkers().OfType<SignalEmitter>()
                .OrderBy(emitter => emitter.time)
                .ToArray();
            Assert.That(signalEmitters.Select(emitter => emitter.time), Is.EqualTo(SignalTimes));
            Assert.That(signalEmitters.Select(emitter => emitter.asset).Distinct().Count(), Is.EqualTo(2));
            Assert.That(signalEmitters.All(emitter => !emitter.retroactive && !emitter.emitOnce), Is.True);

            var signalReceiver = director.GetComponent<SignalReceiver>();
            Assert.That(signalReceiver, Is.Not.Null);
            Assert.That(signalReceiver.Count(), Is.EqualTo(2));
            SignalAsset[] emittedSignals = signalEmitters.Select(emitter => emitter.asset).ToArray();
            for (int index = 0; index < emittedSignals.Length; index++)
            {
                var reaction = signalReceiver.GetReaction(emittedSignals[index]);
                Assert.That(reaction, Is.Not.Null);
                Assert.That(reaction.GetPersistentEventCount(), Is.EqualTo(1));
                Assert.That(reaction.GetPersistentTarget(0).GetType().Name, Is.EqualTo("GAIASignalPreviewEffect"));
                Assert.That(reaction.GetPersistentMethodName(0), Is.EqualTo(SignalEventNames[index]));
            }

            TimelineBakeMarker marker = director.GetComponent<TimelineBakeMarker>();
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.director, Is.SameAs(director));
            Assert.That(marker.recordRoots, Has.Length.EqualTo(2));
            Assert.That(marker.recordRoots.All(root => root != null), Is.True);
            Assert.That(marker.frameRate, Is.EqualTo(30f));
            Assert.That(marker.highPrecision, Is.True);
            Assert.That(marker.highPrecisionReduction, Is.EqualTo(0.002f));
            Assert.That(marker.bakeSignalEvents, Is.True);
            Assert.That(marker.signalEventHost, Is.Not.Null);
            Assert.That(marker.signalEventHost.name, Is.EqualTo("Baked Visuals"));
            Assert.That(marker.recordRoots.Count(root => root == marker.signalEventHost), Is.EqualTo(1));
            Assert.That(marker.signalEventRoutes, Has.Length.EqualTo(2));
            Assert.That(marker.signalEventRoutes.Select(route => route.udonEventName),
                Is.EqualTo(SignalEventNames));
            Assert.That(marker.signalEventRoutes.Select(route => route.signal), Is.EqualTo(emittedSignals));
            Assert.That(AssetDatabase.FindAssets("l:GAIALyricsMovie.OwnedBake.v1"), Is.Empty);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(SignalProgramPath), Is.Not.Null);

            MonoBehaviour eventReceiver = marker.signalEventHost.GetComponents<MonoBehaviour>()
                .Single(component => component.GetType().Name == "GAIASignalEventReceiver");
            var serializedEventReceiver = new SerializedObject(eventReceiver);
            var chorusHalo = serializedEventReceiver.FindProperty("chorusHalo").objectReferenceValue as GameObject;
            var finaleBloom = serializedEventReceiver.FindProperty("finaleBloom").objectReferenceValue as GameObject;
            Assert.That(chorusHalo, Is.Not.Null);
            Assert.That(finaleBloom, Is.Not.Null);
            Assert.That(serializedEventReceiver.FindProperty("chorusPulseTime").floatValue,
                Is.EqualTo((float)SignalTimes[0]));
            Assert.That(serializedEventReceiver.FindProperty("finaleBloomTime").floatValue,
                Is.EqualTo((float)SignalTimes[1]));
            Component backingUdon = marker.signalEventHost.GetComponents<Component>()
                .Single(component => component.GetType().FullName == "VRC.Udon.UdonBehaviour");
            var serializedBackingUdon = new SerializedObject(backingUdon);
            Assert.That(serializedBackingUdon.FindProperty("programSource").objectReferenceValue, Is.Not.Null);
            SerializedProperty backingObjectReferences =
                serializedBackingUdon.FindProperty("publicVariablesUnityEngineObjects");
            Assert.That(backingObjectReferences.arraySize, Is.EqualTo(2));
            Assert.That(Enumerable.Range(0, backingObjectReferences.arraySize)
                    .Select(index => backingObjectReferences.GetArrayElementAtIndex(index).objectReferenceValue),
                Is.EquivalentTo(new UnityEngine.Object[] { chorusHalo, finaleBloom }));
            Assert.That(chorusHalo.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.False);

            GameObject playButton = GameObject.Find("GAIA Play Button");
            Assert.That(playButton, Is.Not.Null);
            Assert.That(playButton.GetComponent<Collider>(), Is.Not.Null,
                "▶ボタンはVRChatのInteract対象になるコライダーが必要です。");
            MonoBehaviour showController = playButton.GetComponents<MonoBehaviour>()
                .Single(component => component.GetType().Name == "GAIAShowController");
            var serializedShowController = new SerializedObject(showController);
            Assert.That(serializedShowController.FindProperty("director").objectReferenceValue,
                Is.SameAs(director));
            Assert.That(serializedShowController.FindProperty("signalReceiver").objectReferenceValue,
                Is.SameAs(eventReceiver));
            Assert.That(serializedShowController.FindProperty("songDuration").doubleValue,
                Is.EqualTo(221.504d).Within(0.001d));
            Component buttonBackingUdon = playButton.GetComponents<Component>()
                .Single(component => component.GetType().FullName == "VRC.Udon.UdonBehaviour");
            var serializedButtonUdon = new SerializedObject(buttonBackingUdon);
            Assert.That(serializedButtonUdon.FindProperty("programSource").objectReferenceValue, Is.Not.Null);
            Assert.That(serializedButtonUdon.FindProperty("interactText").stringValue,
                Is.EqualTo("Start GAIA Show"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(ShowControllerProgramPath), Is.Not.Null);

            GameObject lyrics = GameObject.Find("Lyrics");
            Assert.That(lyrics, Is.Not.Null);
            Assert.That(lyrics.transform.childCount, Is.EqualTo(44));
            Assert.That(GameObject.Find("VRCWorld"), Is.Not.Null);
            Assert.That(EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == ScenePath), Is.True);

            var audioImporter = AssetImporter.GetAtPath(AudioPath) as AudioImporter;
            Assert.That(audioImporter, Is.Not.Null);
            Assert.That(audioImporter.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.Streaming));

            foreach (string materialName in new[] { "GAIA_Floor", "GAIA_Cyan", "GAIA_Magenta", "GAIA_Violet" })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(
                    $"Assets/GAIALyricsMovie/Generated/{materialName}.mat");
                Assert.That(material, Is.Not.Null, materialName);
                Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True,
                    $"{materialName} の _EMISSION キーワードが無効です。エミッションが消えてVRChatで真っ黒に見えます。");
                Assert.That(material.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(0f), materialName);
            }
        }

        [Test]
        public void SignalReceiver_ResyncToTime_RestoresSkippedSignalStateWithoutCountingEvents()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            MonoBehaviour receiver = Object.FindObjectsOfType<MonoBehaviour>(true)
                .Single(component => component.GetType().Name == "GAIASignalEventReceiver");
            var serializedReceiver = new SerializedObject(receiver);
            var chorusHalo = (GameObject)serializedReceiver.FindProperty("chorusHalo").objectReferenceValue;
            var finaleBloom = (GameObject)serializedReceiver.FindProperty("finaleBloom").objectReferenceValue;
            System.Reflection.MethodInfo resync = receiver.GetType().GetMethod("ResyncToTime");
            Assert.That(resync, Is.Not.Null);

            resync.Invoke(receiver, new object[] { 0f });
            Assert.That(chorusHalo.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.False);

            resync.Invoke(receiver, new object[] { 60f });
            Assert.That(chorusHalo.activeSelf, Is.True);
            Assert.That(finaleBloom.activeSelf, Is.False);

            resync.Invoke(receiver, new object[] { 200f });
            Assert.That(chorusHalo.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.True);

            // 保存シーンの初期状態(両方非表示)へ戻してから、実イベント計測が汚れていないことを確認する。
            resync.Invoke(receiver, new object[] { 0f });
            Assert.That(chorusHalo.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.False);
            serializedReceiver.Update();
            Assert.That(serializedReceiver.FindProperty("eventCount").intValue, Is.Zero,
                "ResyncToTimeは実イベント計測のeventCountを増やしてはいけません。");
            Assert.That(serializedReceiver.FindProperty("lastEventName").stringValue, Is.Empty);
        }

        [Test]
        public void EmissionGuard_RestoresDisabledEmissionKeywordBeforeBuild()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var cyanMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/GAIALyricsMovie/Generated/GAIA_Cyan.mat");
            Assert.That(cyanMaterial, Is.Not.Null);

            System.Type guardType = System.Type.GetType(
                "GAIALyricsMovie.Editor.GAIAEmissionKeywordGuard, Assembly-CSharp-Editor");
            Assert.That(guardType, Is.Not.Null);

            cyanMaterial.DisableKeyword("_EMISSION");
            Assert.That(cyanMaterial.IsKeywordEnabled("_EMISSION"), Is.False);

            object guard = System.Activator.CreateInstance(guardType);
            guardType.GetMethod("OnProcessScene").Invoke(
                guard, new object[] { SceneManager.GetActiveScene(), null });
            Assert.That(cyanMaterial.IsKeywordEnabled("_EMISSION"), Is.False,
                "report が null (通常Playなど非ビルド経路) では書き換えないはず。");

            guardType.GetMethod("RestoreEmission").Invoke(
                null, new object[] { SceneManager.GetActiveScene() });
            Assert.That(cyanMaterial.IsKeywordEnabled("_EMISSION"), Is.True,
                "ビルド直前の強制復元でエミッションキーワードが戻っていません。");
        }

        [Test]
        public void BuildProcessor_BakesGaiaTimelineCopyWithoutChangingSourceAsset()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PlayableDirector director = Object.FindObjectOfType<PlayableDirector>(true);
            var sourceTimeline = (TimelineAsset)director.playableAsset;
            string sourcePath = AssetDatabase.GetAssetPath(sourceTimeline);
            string buildTimelinePath = null;

            try
            {
                new PlayableTrackBakeSceneProcessor().OnProcessScene(director.gameObject.scene, null);

                var buildTimeline = (TimelineAsset)director.playableAsset;
                buildTimelinePath = AssetDatabase.GetAssetPath(buildTimeline);
                Assert.That(buildTimeline, Is.Not.SameAs(sourceTimeline));
                Assert.That(buildTimelinePath, Does.StartWith(TempBakeFolder));
                Assert.That(buildTimeline.GetOutputTracks().OfType<PlayableTrack>().Single().muted, Is.True);

                AnimationTrack[] bakedTracks = buildTimeline.GetOutputTracks().OfType<AnimationTrack>()
                    .Where(track => track.name.StartsWith("[Baked]"))
                    .ToArray();
                Assert.That(bakedTracks, Has.Length.EqualTo(2));
                foreach (AnimationTrack bakedTrack in bakedTracks)
                {
                    Assert.That(director.GetGenericBinding(bakedTrack), Is.TypeOf<Animator>());
                    TimelineClip bakedTimelineClip = bakedTrack.GetClips().Single();
                    var bakedPlayable = bakedTimelineClip.asset as AnimationPlayableAsset;
                    Assert.That(bakedPlayable, Is.Not.Null);
                    Assert.That(bakedPlayable.clip, Is.Not.Null);
                    Assert.That(bakedTimelineClip.duration, Is.EqualTo(221.504d).Within(0.05d));
                    Assert.That(AnimationUtility.GetCurveBindings(bakedPlayable.clip), Is.Not.Empty);
                }

                AnimationTrack signalHostTrack = bakedTracks.Single(track =>
                    track.name == "[Baked] Baked Visuals");
                var signalHostPlayable = (AnimationPlayableAsset)signalHostTrack.GetClips().Single().asset;
                AnimationEvent[] signalEvents = AnimationUtility.GetAnimationEvents(signalHostPlayable.clip);
                Assert.That(signalEvents.Select(animationEvent => animationEvent.time),
                    Is.EqualTo(SignalTimes.Select(time => (float)time)).Within(0.001f));
                Assert.That(signalEvents.Select(animationEvent => animationEvent.functionName),
                    Is.All.EqualTo("SendCustomEvent"));
                Assert.That(signalEvents.Select(animationEvent => animationEvent.stringParameter),
                    Is.EqualTo(SignalEventNames));
                AnimationTrack lyricsTrack = bakedTracks.Single(track => track.name == "[Baked] Lyrics");
                var lyricsPlayable = (AnimationPlayableAsset)lyricsTrack.GetClips().Single().asset;
                Assert.That(AnimationUtility.GetAnimationEvents(lyricsPlayable.clip), Is.Empty);

                Assert.That(AssetDatabase.GetAssetPath(sourceTimeline), Is.EqualTo(sourcePath));
                Assert.That(sourceTimeline.GetOutputTracks().OfType<PlayableTrack>().Single().muted, Is.False);
                Assert.That(sourceTimeline.GetOutputTracks().OfType<AnimationTrack>()
                    .Any(track => track.name.StartsWith("[Baked]")), Is.False);
                Assert.That(sourceTimeline.markerTrack.GetMarkers().OfType<SignalEmitter>().Count(),
                    Is.EqualTo(SignalTimes.Length));

                System.Type stripperType = System.Type.GetType(
                    "GAIALyricsMovie.Editor.GAIASignalPreviewBuildStripper, Assembly-CSharp-Editor");
                Assert.That(stripperType, Is.Not.Null);
                object stripper = System.Activator.CreateInstance(stripperType);
                stripperType.GetMethod("OnProcessScene").Invoke(
                    stripper,
                    new object[] { director.gameObject.scene, null });
                Assert.That(Object.FindObjectOfType<SignalReceiver>(true), Is.Not.Null);
                stripperType.GetMethod("StripPreview").Invoke(
                    null,
                    new object[] { director.gameObject.scene });
                Assert.That(Object.FindObjectOfType<SignalReceiver>(true), Is.Null);
            }
            finally
            {
                director.playableAsset = sourceTimeline;
                if (!string.IsNullOrEmpty(buildTimelinePath))
                    AssetDatabase.DeleteAsset(buildTimelinePath);
                if (AssetDatabase.IsValidFolder(TempBakeFolder) &&
                    AssetDatabase.FindAssets(string.Empty, new[] { TempBakeFolder }).Length == 0)
                    AssetDatabase.DeleteAsset(TempBakeFolder);
                AssetDatabase.Refresh();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        [UnityTest]
        public IEnumerator SignalRuntime_ExecutesBakedUdonEventsInPlayMode()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            yield return new EnterPlayMode();

            PlayableDirector director = Object.FindObjectOfType<PlayableDirector>(true);
            var runtimeTimeline = (TimelineAsset)director.playableAsset;
            Assert.That(AssetDatabase.GetAssetPath(runtimeTimeline), Does.StartWith(TempBakeFolder));
            Assert.That(runtimeTimeline.GetOutputTracks().OfType<PlayableTrack>().Single().muted, Is.True);
            AnimationTrack runtimeEventTrack = runtimeTimeline.GetOutputTracks().OfType<AnimationTrack>()
                .Single(track => track.name == "[Baked] Baked Visuals");
            var runtimeEventPlayable = (AnimationPlayableAsset)runtimeEventTrack.GetClips().Single().asset;
            AnimationEvent[] runtimeEvents = AnimationUtility.GetAnimationEvents(runtimeEventPlayable.clip);
            Assert.That(runtimeEvents.Select(animationEvent => animationEvent.functionName),
                Is.All.EqualTo("SendCustomEvent"));
            Assert.That(runtimeEvents.Select(animationEvent => animationEvent.stringParameter),
                Is.EqualTo(SignalEventNames));

            TrackAsset signalTrack = runtimeTimeline.markerTrack;
            Assert.That(signalTrack.GetMarkers().OfType<SignalEmitter>().Count(), Is.EqualTo(2));
            var signalReceiver = director.GetComponent<SignalReceiver>();
            Assert.That(signalReceiver, Is.Not.Null);
            MonoBehaviour runtimeReceiver = Object.FindObjectsOfType<MonoBehaviour>(true)
                .Single(component => component.GetType().Name == "GAIASignalEventReceiver");
            var serializedRuntimeReceiver = new SerializedObject(runtimeReceiver);
            var chorusHalo = (GameObject)serializedRuntimeReceiver.FindProperty("chorusHalo").objectReferenceValue;
            var finaleBloom = (GameObject)serializedRuntimeReceiver.FindProperty("finaleBloom").objectReferenceValue;

            Assert.That(chorusHalo.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.False);

            director.Stop();
            director.time = SignalTimes[0] - 0.02d;
            director.Evaluate();
            director.Play();
            director.time = SignalTimes[0] + 0.02d;
            director.Evaluate();
            yield return null;

            Assert.That(chorusHalo.activeSelf, Is.True);
            Assert.That(finaleBloom.activeSelf, Is.False);

            director.time = SignalTimes[1] - 0.02d;
            director.Evaluate();
            director.time = SignalTimes[1] + 0.02d;
            director.Evaluate();
            yield return null;

            Assert.That(chorusHalo.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.True);
            serializedRuntimeReceiver.Update();
            Assert.That(serializedRuntimeReceiver.FindProperty("eventCount").intValue, Is.EqualTo(2));
            Assert.That(serializedRuntimeReceiver.FindProperty("lastEventName").stringValue,
                Is.EqualTo(SignalEventNames[1]));

            yield return new ExitPlayMode();
        }
    }
}
