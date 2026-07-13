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
        const double ChorusSignalTestTime = 56.52d;
        const double FinaleSignalTestTime = 153.24d;

        // 全10個のSignalEmitterの時刻(昇順)と、各時刻でベイクされるべきUdonイベント名。
        // "GAIA Verse Beacon" は同じSignalAssetから3回発火するため、名前が3回登場する。
        static readonly double[] SignalTimes =
        {
            12.5d, 30.0d, 56.52d, 90.0d, 100.0d, 100.4d, 120.0d, 130.0d, 153.24d, 210.0d,
        };
        static readonly string[] SignalEventNames =
        {
            "OnIntroSpark", "OnVerseBeacon", "OnChorusPulse", "OnVerseBeacon", "OnRapidPulseA",
            "OnRapidPulseB", "OnVerseBeacon", "OnBridgeDim", "OnFinaleBloom", "OnOutroFade",
        };

        // 8個の一意なSignalAssetそれぞれについて、時刻昇順で最初に登場した際のイベント名
        // (= Distinct()が保持する出現順)。SignalReceiverの反応やTimelineBakeMarkerの
        // signalEventRoutesの並びと一致する。
        static readonly string[] DistinctSignalEventNames =
        {
            "OnIntroSpark", "OnVerseBeacon", "OnChorusPulse", "OnRapidPulseA", "OnRapidPulseB",
            "OnBridgeDim", "OnFinaleBloom", "OnOutroFade",
        };

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
            Assert.That(signalEmitters.Select(emitter => emitter.asset).Distinct().Count(), Is.EqualTo(8));
            Assert.That(signalEmitters.All(emitter => !emitter.retroactive && !emitter.emitOnce), Is.True);

            var signalReceiver = director.GetComponent<SignalReceiver>();
            Assert.That(signalReceiver, Is.Not.Null);
            Assert.That(signalReceiver.Count(), Is.EqualTo(8));
            SignalAsset[] distinctSignalsInFirstOccurrenceOrder = signalEmitters
                .Select(emitter => emitter.asset)
                .Distinct()
                .ToArray();
            Assert.That(distinctSignalsInFirstOccurrenceOrder, Has.Length.EqualTo(8));
            for (int index = 0; index < distinctSignalsInFirstOccurrenceOrder.Length; index++)
            {
                var reaction = signalReceiver.GetReaction(distinctSignalsInFirstOccurrenceOrder[index]);
                Assert.That(reaction, Is.Not.Null);
                Assert.That(reaction.GetPersistentEventCount(), Is.EqualTo(1));
                Assert.That(reaction.GetPersistentTarget(0).GetType().Name, Is.EqualTo("GAIASignalPreviewEffect"));
                Assert.That(reaction.GetPersistentMethodName(0), Is.EqualTo(DistinctSignalEventNames[index]));
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
            Assert.That(marker.signalEventRoutes, Has.Length.EqualTo(8));
            Assert.That(marker.signalEventRoutes.Select(route => route.udonEventName),
                Is.EqualTo(DistinctSignalEventNames));
            Assert.That(marker.signalEventRoutes.Select(route => route.signal),
                Is.EqualTo(distinctSignalsInFirstOccurrenceOrder));
            Assert.That(AssetDatabase.FindAssets("l:GAIALyricsMovie.OwnedBake.v1"), Is.Empty);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(SignalProgramPath), Is.Not.Null);

            // Fidelity Probes: Baked VisualsはPlayableTrackベイクの忠実度プローブ用に5レイヤーになる。
            Transform visualsRootTransform = marker.signalEventHost.transform;
            Assert.That(visualsRootTransform.childCount, Is.EqualTo(5));
            Transform probesLayer = visualsRootTransform.Find("Fidelity Probes");
            Assert.That(probesLayer, Is.Not.Null);
            string[] expectedProbeNames =
            {
                "Comet Circuit", "Drift Monolith", "Gyro Spinner", "Teleport Beacons",
                "Orbit Pair", "Phase Choir", "Scale Beat",
            };
            Assert.That(
                Enumerable.Range(0, probesLayer.childCount).Select(index => probesLayer.GetChild(index).name),
                Is.EqualTo(expectedProbeNames));

            MonoBehaviour eventReceiver = marker.signalEventHost.GetComponents<MonoBehaviour>()
                .Single(component => component.GetType().Name == "GAIASignalEventReceiver");
            var serializedEventReceiver = new SerializedObject(eventReceiver);
            var chorusHalo = serializedEventReceiver.FindProperty("chorusHalo").objectReferenceValue as GameObject;
            var finaleBloom = serializedEventReceiver.FindProperty("finaleBloom").objectReferenceValue as GameObject;
            var introSparkShards =
                serializedEventReceiver.FindProperty("introSparkShards").objectReferenceValue as GameObject;
            var rapidTwinA = serializedEventReceiver.FindProperty("rapidTwinA").objectReferenceValue as GameObject;
            var rapidTwinB = serializedEventReceiver.FindProperty("rapidTwinB").objectReferenceValue as GameObject;
            var bridgeVeil = serializedEventReceiver.FindProperty("bridgeVeil").objectReferenceValue as GameObject;
            var outroRing = serializedEventReceiver.FindProperty("outroRing").objectReferenceValue as GameObject;
            SerializedProperty beaconSegmentsProperty = serializedEventReceiver.FindProperty("beaconSegments");
            Assert.That(beaconSegmentsProperty.arraySize, Is.EqualTo(3));
            GameObject[] beaconSegments = Enumerable.Range(0, beaconSegmentsProperty.arraySize)
                .Select(index => beaconSegmentsProperty.GetArrayElementAtIndex(index).objectReferenceValue as GameObject)
                .ToArray();
            GameObject[] allEffectObjects = new[]
                {
                    chorusHalo, finaleBloom, introSparkShards, rapidTwinA, rapidTwinB, bridgeVeil, outroRing,
                }
                .Concat(beaconSegments)
                .ToArray();
            Assert.That(allEffectObjects, Has.All.Not.Null);
            Assert.That(allEffectObjects.All(effect => !effect.activeSelf), Is.True);

            Assert.That(serializedEventReceiver.FindProperty("chorusPulseTime").floatValue,
                Is.EqualTo((float)ChorusSignalTestTime));
            Assert.That(serializedEventReceiver.FindProperty("finaleBloomTime").floatValue,
                Is.EqualTo((float)FinaleSignalTestTime));
            Assert.That(serializedEventReceiver.FindProperty("introSparkTime").floatValue, Is.EqualTo(12.5f));
            Assert.That(serializedEventReceiver.FindProperty("rapidPulseATime").floatValue, Is.EqualTo(100.0f));
            Assert.That(serializedEventReceiver.FindProperty("rapidPulseBTime").floatValue, Is.EqualTo(100.4f));
            Assert.That(serializedEventReceiver.FindProperty("bridgeDimTime").floatValue, Is.EqualTo(130.0f));
            Assert.That(serializedEventReceiver.FindProperty("outroFadeTime").floatValue, Is.EqualTo(210.0f));
            SerializedProperty beaconTimesProperty = serializedEventReceiver.FindProperty("beaconTimes");
            Assert.That(beaconTimesProperty.arraySize, Is.EqualTo(3));
            Assert.That(
                Enumerable.Range(0, beaconTimesProperty.arraySize)
                    .Select(index => beaconTimesProperty.GetArrayElementAtIndex(index).floatValue),
                Is.EqualTo(new[] { 30f, 90f, 120f }));

            Component backingUdon = marker.signalEventHost.GetComponents<Component>()
                .Single(component => component.GetType().FullName == "VRC.Udon.UdonBehaviour");
            var serializedBackingUdon = new SerializedObject(backingUdon);
            Assert.That(serializedBackingUdon.FindProperty("programSource").objectReferenceValue, Is.Not.Null);
            SerializedProperty backingObjectReferences =
                serializedBackingUdon.FindProperty("publicVariablesUnityEngineObjects");
            Assert.That(backingObjectReferences.arraySize, Is.EqualTo(allEffectObjects.Length));
            Assert.That(Enumerable.Range(0, backingObjectReferences.arraySize)
                    .Select(index => backingObjectReferences.GetArrayElementAtIndex(index).objectReferenceValue),
                Is.EquivalentTo(allEffectObjects));

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
            var introSparkShards = (GameObject)serializedReceiver.FindProperty("introSparkShards").objectReferenceValue;
            var rapidTwinA = (GameObject)serializedReceiver.FindProperty("rapidTwinA").objectReferenceValue;
            var rapidTwinB = (GameObject)serializedReceiver.FindProperty("rapidTwinB").objectReferenceValue;
            var bridgeVeil = (GameObject)serializedReceiver.FindProperty("bridgeVeil").objectReferenceValue;
            var outroRing = (GameObject)serializedReceiver.FindProperty("outroRing").objectReferenceValue;
            SerializedProperty beaconSegmentsProperty = serializedReceiver.FindProperty("beaconSegments");
            GameObject[] beaconSegments = Enumerable.Range(0, beaconSegmentsProperty.arraySize)
                .Select(index => (GameObject)beaconSegmentsProperty.GetArrayElementAtIndex(index).objectReferenceValue)
                .ToArray();
            System.Reflection.MethodInfo resync = receiver.GetType().GetMethod("ResyncToTime");
            Assert.That(resync, Is.Not.Null);

            void AssertBeaconSegments(int firedCount)
            {
                for (int index = 0; index < beaconSegments.Length; index++)
                    Assert.That(beaconSegments[index].activeSelf, Is.EqualTo(index < firedCount),
                        $"beaconSegments[{index}] (firedCount={firedCount})");
            }

            resync.Invoke(receiver, new object[] { 0f });
            Assert.That(introSparkShards.activeSelf, Is.False);
            Assert.That(chorusHalo.activeSelf, Is.False);
            AssertBeaconSegments(0);
            Assert.That(rapidTwinA.activeSelf, Is.False);
            Assert.That(rapidTwinB.activeSelf, Is.False);
            Assert.That(bridgeVeil.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.False);
            Assert.That(outroRing.activeSelf, Is.False);

            // t=35: イントロスパーク点灯、Verse Beaconはセグメント1個だけ点灯、ベールはまだ無し。
            resync.Invoke(receiver, new object[] { 35f });
            Assert.That(introSparkShards.activeSelf, Is.True);
            AssertBeaconSegments(1);
            Assert.That(bridgeVeil.activeSelf, Is.False);

            // t=100.2: Rapid Pulse Aだけ点灯(Bは100.4なのでまだ)。
            resync.Invoke(receiver, new object[] { 100.2f });
            Assert.That(rapidTwinA.activeSelf, Is.True);
            Assert.That(rapidTwinB.activeSelf, Is.False);

            // t=101: Rapid Pulse A/Bともに点灯。
            resync.Invoke(receiver, new object[] { 101f });
            Assert.That(rapidTwinA.activeSelf, Is.True);
            Assert.That(rapidTwinB.activeSelf, Is.True);

            // t=140: Bridge Dimでベールが点灯し、イントロスパークは退避する。Verse Beaconは3個とも点灯済み。
            resync.Invoke(receiver, new object[] { 140f });
            Assert.That(bridgeVeil.activeSelf, Is.True);
            Assert.That(introSparkShards.activeSelf, Is.False);
            AssertBeaconSegments(3);

            resync.Invoke(receiver, new object[] { 60f });
            Assert.That(chorusHalo.activeSelf, Is.True);
            Assert.That(finaleBloom.activeSelf, Is.False);

            // 演出は加算式のため、フィナーレ以降もハローは点灯したまま。
            resync.Invoke(receiver, new object[] { 200f });
            Assert.That(chorusHalo.activeSelf, Is.True);
            Assert.That(finaleBloom.activeSelf, Is.True);

            // t=215: 全演出が終端状態(アウトロリングも含む)。
            resync.Invoke(receiver, new object[] { 215f });
            Assert.That(introSparkShards.activeSelf, Is.False);
            Assert.That(chorusHalo.activeSelf, Is.True);
            AssertBeaconSegments(3);
            Assert.That(rapidTwinA.activeSelf, Is.True);
            Assert.That(rapidTwinB.activeSelf, Is.True);
            Assert.That(bridgeVeil.activeSelf, Is.True);
            Assert.That(finaleBloom.activeSelf, Is.True);
            Assert.That(outroRing.activeSelf, Is.True);

            // 保存シーンの初期状態(すべて非表示)へ戻してから、実イベント計測が汚れていないことを確認する。
            resync.Invoke(receiver, new object[] { 0f });
            Assert.That(chorusHalo.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.False);
            AssertBeaconSegments(0);
            serializedReceiver.Update();
            Assert.That(serializedReceiver.FindProperty("eventCount").intValue, Is.Zero,
                "ResyncToTimeは実イベント計測のeventCountを増やしてはいけません。");
            Assert.That(serializedReceiver.FindProperty("lastEventName").stringValue, Is.Empty);
            Assert.That(serializedReceiver.FindProperty("beaconFireCount").intValue, Is.Zero);
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
            Assert.That(signalTrack.GetMarkers().OfType<SignalEmitter>().Count(), Is.EqualTo(10));
            var signalReceiver = director.GetComponent<SignalReceiver>();
            Assert.That(signalReceiver, Is.Not.Null);
            MonoBehaviour runtimeReceiver = Object.FindObjectsOfType<MonoBehaviour>(true)
                .Single(component => component.GetType().Name == "GAIASignalEventReceiver");
            var serializedRuntimeReceiver = new SerializedObject(runtimeReceiver);
            var chorusHalo = (GameObject)serializedRuntimeReceiver.FindProperty("chorusHalo").objectReferenceValue;
            var finaleBloom = (GameObject)serializedRuntimeReceiver.FindProperty("finaleBloom").objectReferenceValue;
            var introSparkShards =
                (GameObject)serializedRuntimeReceiver.FindProperty("introSparkShards").objectReferenceValue;
            var bridgeVeil = (GameObject)serializedRuntimeReceiver.FindProperty("bridgeVeil").objectReferenceValue;
            // Play中のUdonSharpプロキシはSerializedObjectで配列プロパティを解決できないことがあるため、
            // シーンからデシリアライズ済みのC#フィールドをリフレクションで直接読む。
            var beaconSegments = (GameObject[])runtimeReceiver.GetType()
                .GetField("beaconSegments",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(runtimeReceiver);
            Assert.That(beaconSegments, Is.Not.Null.And.Length.EqualTo(3));

            Assert.That(chorusHalo.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.False);
            Assert.That(beaconSegments.All(segment => !segment.activeSelf), Is.True);

            // 【ベイク済みAnimationEventの実挙動(このテストで実測・固定化)】
            // - Stop→time→Playでグラフを再構築すると、最初の評価で0秒から現在時刻までの
            //   全AnimationEventが時刻順に一括再生される。
            // - 再生中の前方タイムジャンプも、通過区間のAnimationEventをすべて発火する。
            // - 時刻0で再構築した場合([0,0])は何も発火しない(再生ボタンのリスタートが清潔な理由)。
            // GAIAShowControllerはこの一括再生をResyncToTimeの遅延再実行で正規化する。

            // グラフ再構築して30.02秒で評価: [0, 30.02]のIntro Spark(12.5)とVerse Beacon(30.0)が発火。
            director.Stop();
            director.time = 30.0d - 0.02d;
            director.Evaluate();
            director.Play();
            director.time = 30.0d + 0.02d;
            director.Evaluate();
            yield return null;

            Assert.That(introSparkShards.activeSelf, Is.True);
            Assert.That(beaconSegments[0].activeSelf, Is.True);
            Assert.That(beaconSegments[1].activeSelf, Is.False);
            serializedRuntimeReceiver.Update();
            Assert.That(serializedRuntimeReceiver.FindProperty("eventCount").intValue, Is.EqualTo(2));
            Assert.That(serializedRuntimeReceiver.FindProperty("beaconFireCount").intValue, Is.EqualTo(1));

            // 再構築して56.54秒で評価: [0, 56.54]の12.5 / 30.0 / 56.52が改めて一括再生される
            // (Verse Beaconは2回目の発火となり、セグメント2個目が点灯する)。
            director.Stop();
            director.time = ChorusSignalTestTime - 0.02d;
            director.Evaluate();
            director.Play();
            director.time = ChorusSignalTestTime + 0.02d;
            director.Evaluate();
            yield return null;

            Assert.That(chorusHalo.activeSelf, Is.True);
            Assert.That(finaleBloom.activeSelf, Is.False);
            Assert.That(beaconSegments[1].activeSelf, Is.True);
            serializedRuntimeReceiver.Update();
            Assert.That(serializedRuntimeReceiver.FindProperty("eventCount").intValue, Is.EqualTo(5));
            Assert.That(serializedRuntimeReceiver.FindProperty("beaconFireCount").intValue, Is.EqualTo(2));

            // 再生中の前方ジャンプ(56.54→153.26): 通過する90 / 100 / 100.4 / 120 / 130 / 153.24の
            // 6イベントが発火する。4回目のVerse Beacon(120)はセグメント範囲外だが、
            // 受信側の境界ガードにより安全に無視される。
            director.time = FinaleSignalTestTime - 0.02d;
            director.Evaluate();
            director.time = FinaleSignalTestTime + 0.02d;
            director.Evaluate();
            yield return null;

            // 演出は加算式のため、フィナーレ発火後もハローは点灯したまま。
            // Bridge Dim(130)はイントロスパークのみ退避させる。
            Assert.That(chorusHalo.activeSelf, Is.True);
            Assert.That(finaleBloom.activeSelf, Is.True);
            Assert.That(bridgeVeil.activeSelf, Is.True);
            Assert.That(introSparkShards.activeSelf, Is.False);
            Assert.That(beaconSegments[2].activeSelf, Is.True);
            serializedRuntimeReceiver.Update();
            Assert.That(serializedRuntimeReceiver.FindProperty("eventCount").intValue, Is.EqualTo(11));
            Assert.That(serializedRuntimeReceiver.FindProperty("beaconFireCount").intValue, Is.EqualTo(4));
            Assert.That(serializedRuntimeReceiver.FindProperty("lastEventName").stringValue,
                Is.EqualTo("OnFinaleBloom"));

            // 再生ボタンの再押下相当: 時刻0での再構築([0,0])は何も発火せず、
            // ResyncToTimeで演出とbeaconFireCountが初期状態へ戻ること。
            director.Stop();
            director.time = 0d;
            director.Evaluate();
            director.Play();
            System.Reflection.MethodInfo runtimeResync = runtimeReceiver.GetType().GetMethod("ResyncToTime");
            Assert.That(runtimeResync, Is.Not.Null);
            runtimeResync.Invoke(runtimeReceiver, new object[] { 0f });
            yield return null;

            Assert.That(chorusHalo.activeSelf, Is.False);
            Assert.That(finaleBloom.activeSelf, Is.False);
            Assert.That(bridgeVeil.activeSelf, Is.False);
            Assert.That(introSparkShards.activeSelf, Is.False);
            Assert.That(beaconSegments.All(segment => !segment.activeSelf), Is.True);
            serializedRuntimeReceiver.Update();
            Assert.That(serializedRuntimeReceiver.FindProperty("eventCount").intValue, Is.EqualTo(11),
                "リスタートのグラフ再構築([0,0])で通過済みイベントが再発火してはいけません。");
            Assert.That(serializedRuntimeReceiver.FindProperty("beaconFireCount").intValue, Is.Zero,
                "ResyncToTime(0)はbeaconFireCountを初期化するはずです。");

            yield return new ExitPlayMode();
        }
    }
}
