#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using VRC.SDK3.Components;
using VRC.Udon;

namespace AudioVolumeExperiment.Editor
{
    /// <summary>
    /// VRChatの音量スライダー（Master/World）がTimeline AudioTrack経由の音に効くかを
    /// 実機で聞き比べるための最小ワールドを生成する。
    /// 同じAudioClipを「Timeline AudioTrack → PlayableDirector」と「AudioSource.Play()」の
    /// 2経路で再生するトグルボタンを左右に並べる。片方だけスライダーに反応すれば経路差が確定する。
    /// </summary>
    public static class AudioVolumeExperimentSceneBuilder
    {
        const string Root = "Assets/AudioVolumeExperiment";
        const string SceneFolder = Root + "/Scenes";
        const string GeneratedFolder = Root + "/Generated";
        const string ScenePath = SceneFolder + "/AudioVolumeExperiment.unity";
        const string TimelinePath = GeneratedFolder + "/AudioVolumeExperiment_Timeline.playable";
        const string AudioPath = "Assets/GAIALyricsMovie/Audio/GAIA.ogg";
        const string FontPath = "Assets/GAIALyricsMovie/Fonts/NotoSansJP-VF.ttf";
        const string ToggleScriptPath = Root + "/Udon/AudioPathToggle.cs";
        const string ToggleProgramPath = Root + "/Udon/AudioPathToggle.asset";
        // 両経路の聞き比べ条件を揃えるため、AudioSource設定は完全に同一にする。
        const float SharedVolume = 0.6f;

        static readonly Color Cyan = new Color(0.12f, 0.92f, 1f, 1f);
        static readonly Color Magenta = new Color(1f, 0.12f, 0.62f, 1f);

        [MenuItem("Tools/PlayableTrackBaker/Create Audio Volume Experiment Scene")]
        public static void BuildFromMenu() => Build();

        public static void BuildFromCommandLine()
        {
            Build();
            Debug.Log("[Audio Volume Experiment] Command-line generation completed.");
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }

        public static void Build()
        {
            ValidateSourceAssets();
            EnsureFolder("Assets", "AudioVolumeExperiment");
            EnsureFolder(Root, "Scenes");
            if (AssetDatabase.IsValidFolder(GeneratedFolder))
                AssetDatabase.DeleteAsset(GeneratedFolder);
            EnsureFolder(Root, "Generated");
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            AudioClip audioClip = RequireAsset<AudioClip>(AudioPath);
            Font font = RequireAsset<Font>(FontPath);
            EnsureToggleProgramAsset();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureEnvironment();

            GameObject worldRoot = new GameObject("Audio Volume Experiment World");
            CreateWorldDescriptor(worldRoot.transform);
            CreateFloor(worldRoot.transform);

            Material cyanMaterial = CreateButtonMaterial("AVE_Cyan", Cyan);
            Material magentaMaterial = CreateButtonMaterial("AVE_Magenta", Magenta);

            PlayableDirector director = CreateTimelinePath(worldRoot.transform, audioClip);
            AudioSource directSource = CreateDirectPath(worldRoot.transform, audioClip);

            CreateToggleButton(
                worldRoot.transform,
                "Timeline Path Button",
                new Vector3(-1.2f, 0f, 0f),
                cyanMaterial,
                font,
                "TIMELINE\nAudioTrack",
                Cyan,
                "Toggle: Timeline AudioTrack",
                director,
                null);
            CreateToggleButton(
                worldRoot.transform,
                "Direct Path Button",
                new Vector3(1.2f, 0f, 0f),
                magentaMaterial,
                font,
                "AUDIOSOURCE\nPlay()",
                Magenta,
                "Toggle: AudioSource.Play()",
                null,
                directSource);
            CreateStaticText(
                "Experiment Instructions",
                "VRChat 音量スライダー実験\n左: Timeline AudioTrack 経由  /  右: AudioSource.Play() 経由\n両方を再生して Master / World スライダーを動かし、どちらの音量が変わるか比べる",
                new Vector3(0f, 2.6f, 2.5f),
                0.03f,
                Color.white,
                font,
                worldRoot.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"シーンを保存できませんでした: {ScenePath}");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"[Audio Volume Experiment] 実験シーンを生成しました。\nScene: {ScenePath}\nTimeline: {TimelinePath}");
        }

        static PlayableDirector CreateTimelinePath(Transform parent, AudioClip audioClip)
        {
            var audioObject = new GameObject("Timeline Audio Source");
            audioObject.transform.SetParent(parent, false);
            AudioSource audioSource = ConfigureAudioSource(audioObject);

            var directorObject = new GameObject("Experiment Director");
            directorObject.transform.SetParent(parent, false);
            PlayableDirector director = directorObject.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            // 実験中はスライダーを操作する時間が必要なので、曲が終わってもループさせ続ける。
            director.extrapolationMode = DirectorWrapMode.Loop;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = "Audio Volume Experiment Timeline";
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = audioClip.length;
            AssetDatabase.CreateAsset(timeline, TimelinePath);

            AudioTrack audioTrack = timeline.CreateTrack<AudioTrack>(null, "Music (AudioTrack)");
            TimelineClip timelineClip = audioTrack.CreateClip(audioClip);
            timelineClip.start = 0d;
            timelineClip.duration = audioClip.length;
            timelineClip.displayName = "GAIA via AudioTrack";

            director.playableAsset = timeline;
            director.SetGenericBinding(audioTrack, audioSource);
            return director;
        }

        static AudioSource CreateDirectPath(Transform parent, AudioClip audioClip)
        {
            var audioObject = new GameObject("Direct Audio Source");
            audioObject.transform.SetParent(parent, false);
            AudioSource audioSource = ConfigureAudioSource(audioObject);
            audioSource.clip = audioClip;
            audioSource.loop = true;
            return audioSource;
        }

        static AudioSource ConfigureAudioSource(GameObject audioObject)
        {
            AudioSource audioSource = audioObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = SharedVolume;
            // VRChatは実行時にVRC Spatial Audio Sourceを自動付与し、既定でGain +10dB・空間化有効になる。
            // 実験条件を固定するため、両経路とも明示的に「2D・ゲイン0」で付けておく。
            var spatial = audioObject.AddComponent<VRCSpatialAudioSource>();
            spatial.EnableSpatialization = false;
            spatial.Gain = 0f;
            return audioSource;
        }

        static void CreateToggleButton(
            Transform parent,
            string name,
            Vector3 basePosition,
            Material material,
            Font font,
            string label,
            Color labelColor,
            string interactText,
            PlayableDirector director,
            AudioSource audioSource)
        {
            GameObject pedestal = CreatePrimitive(name + " Pedestal", PrimitiveType.Cylinder, parent);
            pedestal.transform.position = basePosition + new Vector3(0f, 0.5f, 0f);
            pedestal.transform.localScale = new Vector3(0.5f, 0.55f, 0.5f);

            GameObject button = CreatePrimitive(name, PrimitiveType.Cube, parent);
            button.transform.position = basePosition + new Vector3(0f, 1.11f, 0f);
            button.transform.localScale = new Vector3(0.34f, 0.12f, 0.34f);
            button.GetComponent<Renderer>().sharedMaterial = material;

            CreateStaticText(
                name + " Label", label, basePosition + new Vector3(0f, 1.6f, 0f), 0.035f, labelColor, font, parent);

            AudioPathToggle toggle = UdonSharpUndo.AddComponent<AudioPathToggle>(button);
            var serializedToggle = new SerializedObject(toggle);
            serializedToggle.FindProperty("director").objectReferenceValue = director;
            serializedToggle.FindProperty("audioSource").objectReferenceValue = audioSource;
            serializedToggle.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(toggle);

            UdonBehaviour backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(toggle);
            var serializedBacking = new SerializedObject(backingBehaviour);
            SerializedProperty interactTextProperty = serializedBacking.FindProperty("interactText");
            if (interactTextProperty == null)
                throw new InvalidOperationException("UdonBehaviourにinteractTextが見つかりません。VRChat SDKの構成を確認してください。");
            interactTextProperty.stringValue = interactText;
            serializedBacking.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureToggleProgramAsset()
        {
            MonoScript toggleScript = RequireAsset<MonoScript>(ToggleScriptPath);
            var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ToggleProgramPath);
            if (program == null)
            {
                program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                program.sourceCsScript = toggleScript;
                AssetDatabase.CreateAsset(program, ToggleProgramPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ToggleProgramPath);
                if (program == null)
                    throw new InvalidOperationException(
                        $"生成したUdonSharp program assetを再読込できませんでした: {ToggleProgramPath}");
            }

            if (program.sourceCsScript != toggleScript)
                throw new InvalidOperationException(
                    $"固定パスのUdonSharp program assetはAudioPathToggle所有ではありません: {ToggleProgramPath}");
            // バッチ実行中に新規作成したprogram assetはScriptVersionがUnknownのままで、
            // それを引き上げるUdonSharpのアップグレードパスはエディタ更新ループでしか走らない。
            // このスクリプトは現行のシリアライズ規約([OdinSerialize]不要なUnity直列化可能フィールドのみ)で
            // 書かれているため、アップグレーダーの書き換え不要時の処理と同じ版数引き上げだけを行う。
            // これがないとCopyProxyToUdonが "outdated script version" 例外で失敗する。
            program.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
            if (!IsProgramAssetCompiled(program))
                UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = true });
            if (!IsProgramAssetCompiled(program))
                throw new InvalidOperationException(
                    "AudioPathToggleが未コンパイルです。UdonSharpのコンパイル完了後に再実行してください。");
        }

        static bool IsProgramAssetCompiled(UdonSharpProgramAsset program)
        {
            return program.ScriptVersion == UdonSharpProgramVersion.CurrentVersion &&
                   program.CompiledVersion == UdonSharpProgramVersion.CurrentVersion &&
                   program.SerializedProgramAsset != null &&
                   UdonSharpProgramAsset.GetProgramAssetForClass(typeof(AudioPathToggle)) == program;
        }

        static void ValidateSourceAssets()
        {
            var missing = new[] { AudioPath, FontPath, ToggleScriptPath }
                .Where(path => !File.Exists(ToAbsoluteAssetPath(path)))
                .ToArray();
            if (missing.Length > 0)
                throw new FileNotFoundException("実験シーンの素材が不足しています: " + string.Join(", ", missing));
        }

        static void ConfigureEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.55f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
        }

        static void CreateWorldDescriptor(Transform parent)
        {
            var spawn = new GameObject("Player Spawn");
            spawn.transform.SetParent(parent, false);
            spawn.transform.position = new Vector3(0f, 0.05f, -3f);

            var descriptorObject = new GameObject("VRCWorld");
            descriptorObject.transform.SetParent(parent, false);
            VRCSceneDescriptor descriptor = descriptorObject.AddComponent<VRCSceneDescriptor>();
            descriptor.spawns = new[] { spawn.transform };
            descriptor.spawnRadius = 0.2f;
            descriptor.spawnOrder = VRCSceneDescriptor.SpawnOrder.First;
            descriptor.spawnOrientation = VRCSceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint;
            descriptor.RespawnHeightY = -20f;

            var lightObject = new GameObject("Key Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.9f;
        }

        static void CreateFloor(Transform parent)
        {
            GameObject floor = CreatePrimitive("Floor", PrimitiveType.Plane, parent);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
        }

        static Material CreateButtonMaterial(string name, Color emission)
        {
            var material = new Material(Shader.Find("Standard")) { name = name };
            material.color = Color.black;
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", emission * 1.4f);
            AssetDatabase.CreateAsset(material, GeneratedFolder + "/" + name + ".mat");
            EnsureSerializedKeyword(material, "_EMISSION");
            return material;
        }

        /// <summary>
        /// -nographics のバッチ実行では EnableKeyword が m_ValidKeywords に永続化されないことがあるため、
        /// シリアライズ層でも直接保証する（GAIAビルダーと同じ回避策）。
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

        static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        static TextMesh CreateStaticText(
            string name, string value, Vector3 position, float size, Color color, Font font, Transform parent)
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
            return text;
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
    }
}
#endif
