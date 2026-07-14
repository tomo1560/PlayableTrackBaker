using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AudioVolumeExperiment.Tests
{
    public sealed class ExperimentSceneTests
    {
        const string ScenePath = "Assets/AudioVolumeExperiment/Scenes/AudioVolumeExperiment.unity";
        const string AudioPath = "Assets/GAIALyricsMovie/Audio/GAIA.ogg";
        const string ToggleProgramPath = "Assets/AudioVolumeExperiment/Udon/AudioPathToggle.asset";

        [Test]
        public void ExperimentScene_HasTwoIndependentAudioPathsWiredToToggles()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null,
                "実験シーンが未生成です。Tools/PlayableTrackBaker/Create Audio Volume Experiment Scene を実行してください。");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioPath);
            Assert.That(clip, Is.Not.Null);

            // Timeline経路: AudioTrackがAudioSourceにバインドされ、ループ再生する。
            PlayableDirector director = Object.FindObjectOfType<PlayableDirector>(true);
            Assert.That(director, Is.Not.Null);
            Assert.That(director.playOnAwake, Is.False);
            Assert.That(director.extrapolationMode, Is.EqualTo(DirectorWrapMode.Loop));

            var timeline = director.playableAsset as TimelineAsset;
            Assert.That(timeline, Is.Not.Null);
            AudioTrack audioTrack = timeline.GetOutputTracks().OfType<AudioTrack>().Single();
            TimelineClip timelineClip = audioTrack.GetClips().Single();
            Assert.That(timelineClip.duration, Is.EqualTo((double)clip.length).Within(0.01d));
            var boundSource = director.GetGenericBinding(audioTrack) as AudioSource;
            Assert.That(boundSource, Is.Not.Null, "AudioTrackはAudioSourceにバインドされているはずです。");
            Assert.That(boundSource.playOnAwake, Is.False);

            // 直接経路: 同じクリップを素のAudioSourceでループ再生する。
            AudioSource directSource = Object.FindObjectsOfType<AudioSource>(true)
                .Single(source => source != boundSource);
            Assert.That(directSource.clip, Is.EqualTo(clip));
            Assert.That(directSource.loop, Is.True);
            Assert.That(directSource.playOnAwake, Is.False);

            // 聞き比べ条件が両経路で揃っていること。
            Assert.That(directSource.volume, Is.EqualTo(boundSource.volume).Within(0.0001f));
            Assert.That(boundSource.spatialBlend, Is.Zero);
            Assert.That(directSource.spatialBlend, Is.Zero);
            foreach (AudioSource source in new[] { boundSource, directSource })
            {
                Component spatial = source.GetComponents<Component>()
                    .Single(component => component.GetType().Name == "VRCSpatialAudioSource");
                var serializedSpatial = new SerializedObject(spatial);
                Assert.That(serializedSpatial.FindProperty("EnableSpatialization").boolValue, Is.False,
                    $"{source.name} は2D比較のため空間化を無効にしておくはずです。");
                Assert.That(serializedSpatial.FindProperty("Gain").floatValue, Is.Zero,
                    $"{source.name} のGainは両経路の条件を揃えるため0のはずです。");
            }

            // トグルボタン: 片方はdirectorのみ、もう片方はaudioSourceのみを持つ。
            MonoBehaviour[] toggles = Object.FindObjectsOfType<MonoBehaviour>(true)
                .Where(component => component != null && component.GetType().Name == "AudioPathToggle")
                .ToArray();
            Assert.That(toggles.Length, Is.EqualTo(2));
            foreach (MonoBehaviour toggle in toggles)
            {
                Assert.That(toggle.GetComponent<Collider>(), Is.Not.Null,
                    "トグルはVRChatのInteract対象になるコライダーが必要です。");
                var serializedToggle = new SerializedObject(toggle);
                Object directorRef = serializedToggle.FindProperty("director").objectReferenceValue;
                Object audioRef = serializedToggle.FindProperty("audioSource").objectReferenceValue;
                Assert.That((directorRef != null) ^ (audioRef != null), Is.True,
                    "各トグルはどちらか片方の経路だけを持つはずです。");

                Component backingUdon = toggle.GetComponents<Component>()
                    .Single(component => component.GetType().FullName == "VRC.Udon.UdonBehaviour");
                var serializedBacking = new SerializedObject(backingUdon);
                Assert.That(serializedBacking.FindProperty("programSource").objectReferenceValue, Is.Not.Null);
                Assert.That(serializedBacking.FindProperty("interactText").stringValue,
                    directorRef != null
                        ? Is.EqualTo("Toggle: Timeline AudioTrack")
                        : Is.EqualTo("Toggle: AudioSource.Play()"));
            }

            Assert.That(toggles.Count(toggle =>
                new SerializedObject(toggle).FindProperty("director").objectReferenceValue == (Object)director),
                Is.EqualTo(1));
            Assert.That(toggles.Count(toggle =>
                new SerializedObject(toggle).FindProperty("audioSource").objectReferenceValue == (Object)directSource),
                Is.EqualTo(1));

            Assert.That(AssetDatabase.LoadMainAssetAtPath(ToggleProgramPath), Is.Not.Null);
            Assert.That(GameObject.Find("VRCWorld"), Is.Not.Null);
        }
    }
}
