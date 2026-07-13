#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using PlayableTrackBaking;
using VRC.SDK3.Components;

namespace GAIALyricsMovie.Editor
{
    public static class GAIALyricsMovieSceneBuilder
    {
        const string SampleRoot = "Assets/GAIALyricsMovie";
        const string GeneratedFolder = SampleRoot + "/Generated";
        const string SceneFolder = SampleRoot + "/Scenes";
        const string ScenePath = SceneFolder + "/GAIA_LyricsMovie.unity";
        const string TimelinePath = GeneratedFolder + "/GAIA_LyricsMovie_Timeline.playable";
        const string TorusPath = GeneratedFolder + "/GAIA_Torus.asset";
        const string AudioPath = SampleRoot + "/Audio/GAIA.ogg";
        const string LyricsPath = SampleRoot + "/Data/GAIA.lrc";
        const string FontPath = SampleRoot + "/Fonts/NotoSansJP-VF.ttf";
        const string SignalReceiverScriptPath = SampleRoot + "/Signals/GAIASignalEventReceiver.cs";
        const string SignalReceiverProgramPath = SampleRoot + "/Signals/GAIASignalEventReceiver.asset";
        const double SongDuration = 221.504d;
        const string OwnedBakeLabel = "GAIALyricsMovie.OwnedBake.v1";

        static readonly Color DeepNavy = new Color(0.008f, 0.012f, 0.04f, 1f);
        static readonly Color Cyan = new Color(0.12f, 0.92f, 1f, 1f);
        static readonly Color Magenta = new Color(1f, 0.12f, 0.62f, 1f);
        static readonly Color Violet = new Color(0.38f, 0.18f, 1f, 1f);

        [MenuItem("Tools/PlayableTrackBaker/Create GAIA Lyrics Movie Sample")]
        public static void BuildFromMenu() => Build();

        public static void BuildFromCommandLine()
        {
            Build();
            Debug.Log("[GAIA Lyrics Movie] Command-line generation completed (automatic build bake enabled).");
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }

        public static void Build()
        {
            ValidateSourceAssets();
            DeleteOwnedBakeClips();
            ConfigureAudioImporter();
            EnsureFolder(SampleRoot, "Generated");
            EnsureFolder(SampleRoot, "Scenes");
            if (AssetDatabase.IsValidFolder(GeneratedFolder))
            {
                AssetDatabase.DeleteAsset(GeneratedFolder);
                EnsureFolder(SampleRoot, "Generated");
            }

            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            AudioClip audioClip = RequireAsset<AudioClip>(AudioPath);
            Font japaneseFont = RequireAsset<Font>(FontPath);
            string lrc = File.ReadAllText(ToAbsoluteAssetPath(LyricsPath));
            IReadOnlyList<LyricCue> cues = LrcParser.Parse(lrc, SongDuration);
            if (cues.Count == 0)
                throw new InvalidOperationException("GAIA.lrc に有効なタイムコード付き歌詞がありません。");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureEnvironment();

            Material floorMaterial = CreateStandardMaterial("GAIA_Floor", new Color(0.01f, 0.015f, 0.04f), Violet, 0.72f);
            Material cyanMaterial = CreateStandardMaterial("GAIA_Cyan", new Color(0.01f, 0.08f, 0.1f), Cyan, 1.8f);
            Material magentaMaterial = CreateStandardMaterial("GAIA_Magenta", new Color(0.1f, 0.01f, 0.06f), Magenta, 1.55f);
            Material violetMaterial = CreateStandardMaterial("GAIA_Violet", new Color(0.025f, 0.01f, 0.12f), Violet, 1.35f);
            Material starMaterial = CreateParticleMaterial();
            Mesh torusMesh = CreateTorusMesh();
            ValidateSignalReceiverProgramAsset();

            GameObject worldRoot = new GameObject("GAIA Lyrics Movie World");
            CreateWorldDescriptor(worldRoot.transform);
            CreateArchitecture(worldRoot.transform, floorMaterial, cyanMaterial, magentaMaterial);
            Transform visualsRoot = CreateVisuals(worldRoot.transform, torusMesh, cyanMaterial, magentaMaterial, violetMaterial);
            Transform lyricsRoot = CreateLyrics(worldRoot.transform, japaneseFont, cues);
            SignalEffects signalEffects = CreateSignalEffects(
                worldRoot.transform, torusMesh, cyanMaterial, magentaMaterial);
            CreateStarField(worldRoot.transform, starMaterial);
            CreateInformationTypography(worldRoot.transform, japaneseFont);

            GameObject directorObject = CreateDirector(
                worldRoot.transform,
                audioClip,
                cues,
                lyricsRoot,
                visualsRoot,
                signalEffects);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"シーンを保存できませんでした: {ScenePath}");

            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = directorObject;
            Debug.Log(
                $"[GAIA Lyrics Movie] サンプルシーンを生成しました。歌詞 {cues.Count} 行 / {SongDuration:F3} 秒\n" +
                $"Scene: {ScenePath}\nTimeline: {TimelinePath}\n" +
                "Play では custom Playable を再生し、Build & Publish 時はTransform演出を一時Timelineへ非破壊ベイクします。",
                directorObject);
        }

        readonly struct SignalEffects
        {
            public readonly GameObject ChorusHalo;
            public readonly GameObject FinaleBloom;

            public SignalEffects(GameObject chorusHalo, GameObject finaleBloom)
            {
                ChorusHalo = chorusHalo;
                FinaleBloom = finaleBloom;
            }
        }

        static void DeleteOwnedBakeClips()
        {
            var ownedPaths = new HashSet<string>(StringComparer.Ordinal);
            TimelineAsset existingTimeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (existingTimeline != null)
            {
                foreach (AnimationTrack track in existingTimeline.GetOutputTracks().OfType<AnimationTrack>())
                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip.asset is AnimationPlayableAsset playable && playable.clip != null)
                        ownedPaths.Add(AssetDatabase.GetAssetPath(playable.clip));
                }
            }

            foreach (string guid in AssetDatabase.FindAssets($"l:{OwnedBakeLabel}"))
                ownedPaths.Add(AssetDatabase.GUIDToAssetPath(guid));

            foreach (string path in ownedPaths.Where(path =>
                         path.StartsWith("Assets/BakedTimelineClips/", StringComparison.Ordinal)))
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null && AssetDatabase.GetLabels(asset).Contains(OwnedBakeLabel))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        static void ConfigureAudioImporter()
        {
            var importer = AssetImporter.GetAtPath(AudioPath) as AudioImporter;
            if (importer == null)
                throw new InvalidOperationException($"AudioImporter を取得できません: {AudioPath}");

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = true;
            importer.SaveAndReimport();
        }

        static void ValidateSignalReceiverProgramAsset()
        {
            MonoScript receiverScript = RequireAsset<MonoScript>(SignalReceiverScriptPath);
            UdonSharpProgramAsset program = RequireAsset<UdonSharpProgramAsset>(SignalReceiverProgramPath);
            if (program.sourceCsScript != receiverScript)
                throw new InvalidOperationException(
                    $"固定パスのUdonSharp program assetはGAIA受信スクリプト所有ではありません: {SignalReceiverProgramPath}");
            if (program.CompiledVersion != UdonSharpProgramVersion.CurrentVersion ||
                program.SerializedProgramAsset == null ||
                UdonSharpProgramAsset.GetProgramAssetForClass(typeof(GAIASignalEventReceiver)) != program)
                throw new InvalidOperationException(
                    "GAIASignalEventReceiverが未コンパイルです。UdonSharpのコンパイル完了後に再実行してください。");
        }

        static void ValidateSourceAssets()
        {
            var missing = new[] { AudioPath, LyricsPath, FontPath, SignalReceiverScriptPath }
                .Where(path => !File.Exists(ToAbsoluteAssetPath(path)))
                .ToArray();
            if (missing.Length > 0)
                throw new FileNotFoundException("GAIA サンプル素材が不足しています: " + string.Join(", ", missing));
        }

        static void ConfigureEnvironment()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.025f, 0.035f, 0.1f);
            RenderSettings.ambientEquatorColor = new Color(0.012f, 0.018f, 0.055f);
            RenderSettings.ambientGroundColor = Color.black;
            RenderSettings.ambientIntensity = 0.55f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = DeepNavy;
            RenderSettings.fogDensity = 0.0085f;
            RenderSettings.skybox = null;
        }

        static void CreateWorldDescriptor(Transform parent)
        {
            var spawn = new GameObject("Player Spawn");
            spawn.transform.SetParent(parent, false);
            spawn.transform.position = new Vector3(0f, 1.15f, -12f);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(0f, 3.4f, -13.5f);
            cameraObject.transform.rotation = Quaternion.Euler(1.5f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 58f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 180f;
            camera.backgroundColor = DeepNavy;
            camera.clearFlags = CameraClearFlags.SolidColor;

            var descriptorObject = new GameObject("VRCWorld");
            descriptorObject.transform.SetParent(parent, false);
            VRCSceneDescriptor descriptor = descriptorObject.AddComponent<VRCSceneDescriptor>();
            descriptor.spawns = new[] { spawn.transform };
            descriptor.spawnRadius = 0.2f;
            descriptor.spawnOrder = VRCSceneDescriptor.SpawnOrder.First;
            descriptor.spawnOrientation = VRCSceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint;
            descriptor.ReferenceCamera = cameraObject;
            descriptor.RespawnHeightY = -20f;

            var lightObject = new GameObject("Moon Key Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.34f, 0.42f, 1f);
            light.intensity = 0.42f;
            light.shadows = LightShadows.Soft;
        }

        static void CreateArchitecture(Transform parent, Material floor, Material cyan, Material magenta)
        {
            GameObject platform = CreatePrimitive("Observation Platform", PrimitiveType.Cylinder, parent);
            platform.transform.position = new Vector3(0f, -0.35f, -5f);
            platform.transform.localScale = new Vector3(14f, 0.3f, 14f);
            platform.GetComponent<Renderer>().sharedMaterial = floor;

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject rail = CreatePrimitive(side < 0 ? "Left Light Rail" : "Right Light Rail", PrimitiveType.Cube, parent);
                rail.transform.position = new Vector3(side * 8.2f, 0.22f, 2f);
                rail.transform.localScale = new Vector3(0.08f, 0.08f, 18f);
                rail.GetComponent<Renderer>().sharedMaterial = side < 0 ? cyan : magenta;
                UnityEngine.Object.DestroyImmediate(rail.GetComponent<Collider>());
            }

            GameObject horizon = CreatePrimitive("Horizon Monolith", PrimitiveType.Cube, parent);
            horizon.transform.position = new Vector3(0f, 5f, 23f);
            horizon.transform.localScale = new Vector3(28f, 10f, 0.3f);
            horizon.GetComponent<Renderer>().sharedMaterial = floor;
            UnityEngine.Object.DestroyImmediate(horizon.GetComponent<Collider>());
        }

        static Transform CreateVisuals(Transform parent, Mesh torus, Material cyan, Material magenta, Material violet)
        {
            var root = new GameObject("Baked Visuals").transform;
            root.SetParent(parent, false);
            root.position = new Vector3(0f, 4.1f, 17f);

            Transform rings = CreateLayer("Celestial Rings", root);
            for (int index = 0; index < 5; index++)
            {
                var ring = new GameObject($"Orbit Ring {index + 1}");
                ring.transform.SetParent(rings, false);
                ring.transform.localRotation = Quaternion.Euler(68f + index * 9f, index * 31f, index * 17f);
                ring.transform.localScale = Vector3.one * (1.8f + index * 0.78f);
                ring.AddComponent<MeshFilter>().sharedMesh = torus;
                ring.AddComponent<MeshRenderer>().sharedMaterial = index % 2 == 0 ? cyan : magenta;
            }

            Transform core = CreateLayer("GAIA Core", root);
            GameObject sphere = CreatePrimitive("Core Sphere", PrimitiveType.Sphere, core);
            sphere.transform.localScale = Vector3.one * 2.5f;
            sphere.GetComponent<Renderer>().sharedMaterial = violet;
            UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());

            Transform orbiters = CreateLayer("Orbiting Moons", root);
            for (int index = 0; index < 12; index++)
            {
                float angle = index / 12f * Mathf.PI * 2f;
                GameObject moon = CreatePrimitive($"Moon {index + 1:00}", index % 3 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere, orbiters);
                moon.transform.localPosition = new Vector3(Mathf.Cos(angle) * 7.5f, Mathf.Sin(angle * 2f) * 2.5f, Mathf.Sin(angle) * 4f);
                moon.transform.localScale = Vector3.one * (0.16f + index % 4 * 0.055f);
                moon.GetComponent<Renderer>().sharedMaterial = index % 2 == 0 ? cyan : magenta;
                UnityEngine.Object.DestroyImmediate(moon.GetComponent<Collider>());
            }

            Transform fragments = CreateLayer("Memory Fragments", root);
            for (int index = 0; index < 28; index++)
            {
                float angle = index / 28f * Mathf.PI * 2f;
                GameObject shard = CreatePrimitive($"Fragment {index + 1:00}", PrimitiveType.Cube, fragments);
                float radius = 10f + (index % 5) * 0.72f;
                shard.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle * 3f) * 4f, Mathf.Sin(angle) * 5f);
                shard.transform.localRotation = Quaternion.Euler(index * 19f, index * 37f, index * 11f);
                shard.transform.localScale = new Vector3(0.07f, 0.45f + index % 4 * 0.18f, 0.07f);
                shard.GetComponent<Renderer>().sharedMaterial = index % 3 == 0 ? magenta : cyan;
                UnityEngine.Object.DestroyImmediate(shard.GetComponent<Collider>());
            }

            return root;
        }

        static SignalEffects CreateSignalEffects(
            Transform parent,
            Mesh torus,
            Material cyan,
            Material magenta)
        {
            Transform root = CreateLayer("Signal Effects", parent);
            root.position = new Vector3(0f, 4.1f, 16.5f);

            var chorusHalo = new GameObject("Chorus Signal Halo");
            chorusHalo.transform.SetParent(root, false);
            chorusHalo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            chorusHalo.transform.localScale = Vector3.one * 8.5f;
            chorusHalo.AddComponent<MeshFilter>().sharedMesh = torus;
            chorusHalo.AddComponent<MeshRenderer>().sharedMaterial = cyan;
            chorusHalo.SetActive(false);

            GameObject finaleBloom = CreatePrimitive("Finale Signal Bloom", PrimitiveType.Sphere, root);
            finaleBloom.transform.localScale = Vector3.one * 5.2f;
            finaleBloom.GetComponent<Renderer>().sharedMaterial = magenta;
            UnityEngine.Object.DestroyImmediate(finaleBloom.GetComponent<Collider>());
            finaleBloom.SetActive(false);

            return new SignalEffects(chorusHalo, finaleBloom);
        }

        static Transform CreateLyrics(Transform parent, Font font, IReadOnlyList<LyricCue> cues)
        {
            var root = new GameObject("Lyrics").transform;
            root.SetParent(parent, false);
            root.position = new Vector3(0f, 4.3f, 7.5f);

            for (int index = 0; index < cues.Count; index++)
            {
                LyricCue cue = cues[index];
                var cueObject = new GameObject($"Lyric {index + 1:00} [{cue.StartTime:000.00}]");
                cueObject.transform.SetParent(root, false);
                cueObject.transform.localScale = Vector3.zero;
                TextMesh text = cueObject.AddComponent<TextMesh>();
                text.text = cue.Text;
                text.font = font;
                text.fontSize = 96;
                text.characterSize = cue.Text.Length > 20 ? 0.065f : cue.Text.Length > 13 ? 0.078f : 0.095f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = index % 7 == 0 ? Cyan : Color.white;
                text.richText = false;
                cueObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            return root;
        }

        static void CreateInformationTypography(Transform parent, Font font)
        {
            CreateStaticText("GAIA Title", "G A I A", new Vector3(0f, 8.2f, 9.5f), 0.15f, Cyan, font, parent);
            CreateStaticText("GAIA Subtitle", "LYRICS MOVIE  /  PLAYABLE TRACK BAKER", new Vector3(0f, 7.55f, 9.5f), 0.035f,
                new Color(0.62f, 0.72f, 1f), font, parent);
            CreateStaticText("GAIA Credit", "Music: 魔王魂  •  Compose: 森田交一  •  Lyrics: 火ノ岡レイ  •  Vocal: KEI\nhttps://maou.audio/47_gaia/  •  Local auto-play sample", new Vector3(0f, 0.55f, 7.5f), 0.025f,
                new Color(0.48f, 0.56f, 0.8f), font, parent);
        }

        static GameObject CreateDirector(
            Transform parent,
            AudioClip audioClip,
            IReadOnlyList<LyricCue> cues,
            Transform lyricsRoot,
            Transform visualsRoot,
            SignalEffects signalEffects)
        {
            var audioObject = new GameObject("GAIA 2D Audio");
            audioObject.transform.SetParent(parent, false);
            AudioSource audioSource = audioObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 0.78f;

            var directorObject = new GameObject("GAIA Show Director");
            directorObject.transform.SetParent(parent, false);
            PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
            director.playOnAwake = true;
            director.extrapolationMode = DirectorWrapMode.None;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "GAIA Lyrics Movie Timeline";
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = SongDuration;
            AssetDatabase.CreateAsset(timeline, TimelinePath);

            AudioTrack audioTrack = timeline.CreateTrack<AudioTrack>(null, "GAIA / Music");
            TimelineClip audioTimelineClip = audioTrack.CreateClip(audioClip);
            audioTimelineClip.start = 0d;
            audioTimelineClip.duration = Math.Min(audioClip.length, SongDuration);
            audioTimelineClip.displayName = "GAIA — 魔王魂 / 森田交一";
            director.SetGenericBinding(audioTrack, audioSource);

            PlayableTrack playableTrack = timeline.CreateTrack<PlayableTrack>(null, "GAIA Lyrics + Cosmos (Custom / Bake Me)");
            TimelineClip playableClip = playableTrack.CreateClip<GaiaLyricsPlayableAsset>();
            playableClip.start = 0d;
            playableClip.duration = SongDuration;
            playableClip.displayName = "LRC-synchronised deterministic motion";
            var playableAsset = (GaiaLyricsPlayableAsset)playableClip.asset;
            playableAsset.cueStartTimes = cues.Select(cue => cue.StartTime).ToArray();
            playableAsset.cueEndTimes = cues
                .Select((cue, index) => index == cues.Count - 1
                    ? Math.Min(cue.StartTime + 5d, cue.EndTime)
                    : cue.EndTime)
                .ToArray();
            BindReference(director, ref playableAsset.lyricsRoot, lyricsRoot);
            BindReference(director, ref playableAsset.visualRoot, visualsRoot);

            SignalAsset chorusSignal = CreateSignalAsset(timeline, "GAIA Chorus Pulse");
            SignalAsset finaleSignal = CreateSignalAsset(timeline, "GAIA Finale Bloom");
            timeline.CreateMarkerTrack();
            timeline.markerTrack.name = "GAIA Signal Cues";
            CreateSignalEmitter(timeline.markerTrack, 56.52d, chorusSignal);
            CreateSignalEmitter(timeline.markerTrack, 153.24d, finaleSignal);

            SignalReceiver signalReceiver = directorObject.AddComponent<SignalReceiver>();
            GAIASignalPreviewEffect previewEffect = directorObject.AddComponent<GAIASignalPreviewEffect>();
            ConfigureSignalEffect(previewEffect, signalEffects);
            signalReceiver.AddReaction(
                chorusSignal,
                CreatePersistentReaction(previewEffect.OnChorusPulse));
            signalReceiver.AddReaction(
                finaleSignal,
                CreatePersistentReaction(previewEffect.OnFinaleBloom));

            GAIASignalEventReceiver eventReceiver =
                UdonSharpUndo.AddComponent<GAIASignalEventReceiver>(visualsRoot.gameObject);
            ConfigureSignalEffect(eventReceiver, signalEffects);
            UdonSharpEditorUtility.CopyProxyToUdon(eventReceiver);

            director.playableAsset = timeline;
            var marker = directorObject.AddComponent<TimelineBakeMarker>();
            marker.director = director;
            marker.recordRoots = new[] { lyricsRoot.gameObject, visualsRoot.gameObject };
            marker.recordAllProperties = false;
            marker.frameRate = 30f;
            marker.highPrecision = true;
            marker.highPrecisionReduction = 0.002f;
            marker.mutePlayableTracksAfterBake = true;
            marker.bakeSignalEvents = true;
            marker.signalEventHost = visualsRoot.gameObject;
            marker.signalEventRoutes = new[]
            {
                new SignalEventRoute(chorusSignal, nameof(GAIASignalEventReceiver.OnChorusPulse)),
                new SignalEventRoute(finaleSignal, nameof(GAIASignalEventReceiver.OnFinaleBloom)),
            };

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(playableAsset);
            return directorObject;
        }

        static SignalAsset CreateSignalAsset(TimelineAsset timeline, string name)
        {
            var signal = ScriptableObject.CreateInstance<SignalAsset>();
            signal.name = name;
            AssetDatabase.AddObjectToAsset(signal, timeline);
            return signal;
        }

        static void CreateSignalEmitter(TrackAsset track, double time, SignalAsset signal)
        {
            SignalEmitter emitter = track.CreateMarker<SignalEmitter>(time);
            emitter.asset = signal;
            emitter.retroactive = false;
            emitter.emitOnce = false;
        }

        static UnityEvent CreatePersistentReaction(UnityAction callback)
        {
            var reaction = new UnityEvent();
            UnityEventTools.AddPersistentListener(reaction, callback);
            return reaction;
        }

        static void ConfigureSignalEffect(UnityEngine.Object receiver, SignalEffects signalEffects)
        {
            var serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("chorusHalo").objectReferenceValue = signalEffects.ChorusHalo;
            serializedReceiver.FindProperty("finaleBloom").objectReferenceValue = signalEffects.FinaleBloom;
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BindReference(PlayableDirector director, ref ExposedReference<Transform> reference, Transform value)
        {
            PropertyName exposedName = new PropertyName(GUID.Generate().ToString());
            reference = new ExposedReference<Transform> { exposedName = exposedName };
            director.SetReferenceValue(exposedName, value);
        }

        static void CreateStarField(Transform parent, Material material)
        {
            var starObject = new GameObject("Deterministic Star Field");
            starObject.transform.SetParent(parent, false);
            starObject.transform.position = new Vector3(0f, 5f, 16f);
            ParticleSystem particles = starObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 20f;
            main.startLifetime = 20f;
            main.startSpeed = 0.03f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.11f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.3f, 0.75f, 1f), Color.white);
            main.maxParticles = 800;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            particles.useAutoRandomSeed = false;
            particles.randomSeed = 0x47414941u;
            var emission = particles.emission;
            emission.rateOverTime = 34f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 23f;
            shape.radiusThickness = 1f;
            ParticleSystemRenderer renderer = starObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        static Material CreateStandardMaterial(string name, Color albedo, Color emission, float emissionStrength)
        {
            var material = new Material(Shader.Find("Standard")) { name = name };
            material.color = albedo;
            material.SetFloat("_Metallic", 0.42f);
            material.SetFloat("_Glossiness", 0.86f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", emission * emissionStrength);
            string path = GeneratedFolder + "/" + name + ".mat";
            AssetDatabase.CreateAsset(material, path);
            EnsureSerializedKeyword(material, "_EMISSION");
            return material;
        }

        /// <summary>
        /// -nographics のバッチ実行では EnableKeyword が m_ValidKeywords に永続化されないことがあり、
        /// エミッションが失われてワールドが真っ黒になるため、シリアライズ層でも直接保証する。
        /// </summary>
        static void EnsureSerializedKeyword(Material material, string keyword)
        {
            var serializedMaterial = new SerializedObject(material);
            SerializedProperty keywords = serializedMaterial.FindProperty("m_ValidKeywords");
            for (int index = 0; index < keywords.arraySize; index++)
            {
                if (keywords.GetArrayElementAtIndex(index).stringValue == keyword)
                    return;
            }

            keywords.InsertArrayElementAtIndex(keywords.arraySize);
            keywords.GetArrayElementAtIndex(keywords.arraySize - 1).stringValue = keyword;
            serializedMaterial.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(material);
        }

        static Material CreateParticleMaterial()
        {
            Shader shader = Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Additive");
            var material = new Material(shader) { name = "GAIA_Stars" };
            material.color = new Color(0.4f, 0.82f, 1f, 0.82f);
            AssetDatabase.CreateAsset(material, GeneratedFolder + "/GAIA_Stars.mat");
            return material;
        }

        static Mesh CreateTorusMesh()
        {
            const int majorSegments = 72;
            const int minorSegments = 10;
            const float majorRadius = 1f;
            const float minorRadius = 0.018f;
            var vertices = new Vector3[majorSegments * minorSegments];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[majorSegments * minorSegments * 6];

            for (int major = 0; major < majorSegments; major++)
            {
                float majorAngle = major / (float)majorSegments * Mathf.PI * 2f;
                Vector3 center = new Vector3(Mathf.Cos(majorAngle) * majorRadius, 0f, Mathf.Sin(majorAngle) * majorRadius);
                for (int minor = 0; minor < minorSegments; minor++)
                {
                    float minorAngle = minor / (float)minorSegments * Mathf.PI * 2f;
                    Vector3 normal = new Vector3(
                        Mathf.Cos(majorAngle) * Mathf.Cos(minorAngle),
                        Mathf.Sin(minorAngle),
                        Mathf.Sin(majorAngle) * Mathf.Cos(minorAngle));
                    int vertex = major * minorSegments + minor;
                    normals[vertex] = normal;
                    vertices[vertex] = center + normal * minorRadius;

                    int nextMajor = ((major + 1) % majorSegments) * minorSegments;
                    int nextMinor = (minor + 1) % minorSegments;
                    int triangle = vertex * 6;
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] = nextMajor + minor;
                    triangles[triangle + 2] = nextMajor + nextMinor;
                    triangles[triangle + 3] = vertex;
                    triangles[triangle + 4] = nextMajor + nextMinor;
                    triangles[triangle + 5] = major * minorSegments + nextMinor;
                }
            }

            var mesh = new Mesh { name = "GAIA Torus" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, TorusPath);
            return mesh;
        }

        static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        static Transform CreateLayer(string name, Transform parent)
        {
            var layer = new GameObject(name).transform;
            layer.SetParent(parent, false);
            return layer;
        }

        static void CreateStaticText(string name, string value, Vector3 position, float size, Color color, Font font, Transform parent)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            textObject.transform.position = position;
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = value;
            text.font = font;
            text.fontSize = 96;
            text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            text.richText = false;
            textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"Unity アセットとして読み込めません: {path}");
            return asset;
        }

        static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        static string ToAbsoluteAssetPath(string assetPath)
        {
            string relative = assetPath.Substring("Assets/".Length);
            return Path.Combine(Application.dataPath, relative);
        }

        static void AddSceneToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.path != ScenePath)
                .Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) })
                .ToArray();
            EditorBuildSettings.scenes = scenes;
        }
    }
}
#endif
