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

            // 2つのTimeline経路（バインドあり/なし）が同条件でループ再生する。
            PlayableDirector[] directors = Object.FindObjectsOfType<PlayableDirector>(true);
            Assert.That(directors, Has.Length.EqualTo(2));
            foreach (PlayableDirector anyDirector in directors)
            {
                Assert.That(anyDirector.playOnAwake, Is.False, anyDirector.name);
                Assert.That(anyDirector.extrapolationMode, Is.EqualTo(DirectorWrapMode.Loop), anyDirector.name);
                var anyTimeline = anyDirector.playableAsset as TimelineAsset;
                Assert.That(anyTimeline, Is.Not.Null, anyDirector.name);
                TimelineClip anyClip = anyTimeline.GetOutputTracks().OfType<AudioTrack>().Single()
                    .GetClips().Single();
                Assert.That(anyClip.duration, Is.EqualTo((double)clip.length).Within(0.01d), anyDirector.name);
            }

            PlayableDirector director = directors.Single(d => d.name == "Experiment Director");
            PlayableDirector unboundDirector = directors.Single(d => d.name == "Unbound Experiment Director");

            AudioTrack audioTrack = ((TimelineAsset)director.playableAsset)
                .GetOutputTracks().OfType<AudioTrack>().Single();
            var boundSource = director.GetGenericBinding(audioTrack) as AudioSource;
            Assert.That(boundSource, Is.Not.Null, "AudioTrackはAudioSourceにバインドされているはずです。");
            Assert.That(boundSource.playOnAwake, Is.False);

            // 再現経路: 「音は鳴るのに音量制御が効かない」状態を作るため、意図的に未バインドにする。
            AudioTrack unboundTrack = ((TimelineAsset)unboundDirector.playableAsset)
                .GetOutputTracks().OfType<AudioTrack>().Single();
            Assert.That(unboundDirector.GetGenericBinding(unboundTrack), Is.Null,
                "再現用経路のAudioTrackは意図的に未バインドのはずです。");
            var serializedUnboundTrack = new SerializedObject(unboundTrack);
            Assert.That(serializedUnboundTrack.FindProperty("m_TrackProperties.volume").floatValue,
                Is.EqualTo(boundSource.volume).Within(0.0001f),
                "未バインド経路はAudioSource.volumeを通らないため、トラックVolumeで音量を揃えるはずです。");

            // VideoPlayer Direct経路: AudioSourceを介さない音のもう1つの再現。
            // VRChatのWorldValidationがロード時にAudioSource出力へ強制変換するため、
            // 実機での予想は「スライダーが効く」（未バインドAudioTrackとの対比用）。
            var videoPlayer = Object.FindObjectsOfType<UnityEngine.Video.VideoPlayer>(true).Single();
            Assert.That(videoPlayer.gameObject.activeSelf, Is.False,
                "ビデオ経路はSetActiveトグルで開始するため初期非アクティブのはずです。");
            Assert.That(videoPlayer.playOnAwake, Is.True,
                "UdonからVideoPlayer APIを呼べないため、SetActive時のplayOnAwakeで再生するはずです。");
            Assert.That(videoPlayer.isLooping, Is.True);
            Assert.That(videoPlayer.clip, Is.Not.Null);
            Assert.That(videoPlayer.audioOutputMode,
                Is.EqualTo(UnityEngine.Video.VideoAudioOutputMode.Direct),
                "Direct音声出力の再現経路のはずです。");
            Assert.That(videoPlayer.GetDirectAudioVolume(0), Is.EqualTo(0.6f).Within(0.0001f),
                "他経路と実効音量を揃えるはずです。");

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

            // トグルボタン: 4経路それぞれがちょうど1つのトグルに配線されている。
            MonoBehaviour[] toggles = Object.FindObjectsOfType<MonoBehaviour>(true)
                .Where(component => component != null && component.GetType().Name == "AudioPathToggle")
                .ToArray();
            Assert.That(toggles.Length, Is.EqualTo(4));
            foreach (MonoBehaviour toggle in toggles)
            {
                Assert.That(toggle.GetComponent<Collider>(), Is.Not.Null,
                    "トグルはVRChatのInteract対象になるコライダーが必要です。");
                var serializedToggle = new SerializedObject(toggle);
                Object directorRef = serializedToggle.FindProperty("director").objectReferenceValue;
                Object audioRef = serializedToggle.FindProperty("audioSource").objectReferenceValue;
                Object targetRef = serializedToggle.FindProperty("toggleTarget").objectReferenceValue;
                int wiredPathCount =
                    (directorRef != null ? 1 : 0) + (audioRef != null ? 1 : 0) + (targetRef != null ? 1 : 0);
                Assert.That(wiredPathCount, Is.EqualTo(1),
                    "各トグルはちょうど1つの経路だけを持つはずです。");

                string expectedInteractText =
                    directorRef == (Object)director ? "Toggle: Timeline AudioTrack"
                    : directorRef == (Object)unboundDirector ? "Toggle: Unbound AudioTrack"
                    : targetRef != null ? "Toggle: VideoPlayer Direct Audio"
                    : "Toggle: AudioSource.Play()";
                Component backingUdon = toggle.GetComponents<Component>()
                    .Single(component => component.GetType().FullName == "VRC.Udon.UdonBehaviour");
                var serializedBacking = new SerializedObject(backingUdon);
                Assert.That(serializedBacking.FindProperty("programSource").objectReferenceValue, Is.Not.Null);
                Assert.That(serializedBacking.FindProperty("interactText").stringValue,
                    Is.EqualTo(expectedInteractText));
            }

            Assert.That(toggles.Count(toggle =>
                new SerializedObject(toggle).FindProperty("director").objectReferenceValue == (Object)director),
                Is.EqualTo(1));
            Assert.That(toggles.Count(toggle =>
                new SerializedObject(toggle).FindProperty("director").objectReferenceValue == (Object)unboundDirector),
                Is.EqualTo(1));
            Assert.That(toggles.Count(toggle =>
                new SerializedObject(toggle).FindProperty("audioSource").objectReferenceValue == (Object)directSource),
                Is.EqualTo(1));
            Assert.That(toggles.Count(toggle =>
                new SerializedObject(toggle).FindProperty("toggleTarget").objectReferenceValue ==
                    (Object)videoPlayer.gameObject),
                Is.EqualTo(1));

            Assert.That(AssetDatabase.LoadMainAssetAtPath(ToggleProgramPath), Is.Not.Null);
            Assert.That(GameObject.Find("VRCWorld"), Is.Not.Null);
        }
    }
}
