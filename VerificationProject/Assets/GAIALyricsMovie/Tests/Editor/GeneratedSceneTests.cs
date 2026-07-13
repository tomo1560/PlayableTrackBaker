using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace GAIALyricsMovie.Tests
{
    public sealed class GeneratedSceneTests
    {
        const string ScenePath = "Assets/GAIALyricsMovie/Scenes/GAIA_LyricsMovie.unity";
        const string AudioPath = "Assets/GAIALyricsMovie/Audio/GAIA.ogg";

        [Test]
        public void GeneratedScene_IsBuildReadyAndContainsBakedLyricsMovieTimeline()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            PlayableDirector director = Object.FindObjectOfType<PlayableDirector>(true);
            Assert.That(director, Is.Not.Null);
            Assert.That(director.playOnAwake, Is.True);

            var timeline = director.playableAsset as TimelineAsset;
            Assert.That(timeline, Is.Not.Null);
            Assert.That(timeline.fixedDuration, Is.EqualTo(221.504d).Within(0.001d));
            Assert.That(timeline.GetOutputTracks().OfType<AudioTrack>().Count(), Is.EqualTo(1));
            Assert.That(timeline.GetOutputTracks().OfType<PlayableTrack>().Single().muted, Is.True);
            Assert.That(timeline.GetOutputTracks().OfType<AnimationTrack>()
                .Count(track => track.name.StartsWith("[Baked]")), Is.EqualTo(2));

            GameObject lyrics = GameObject.Find("Lyrics");
            Assert.That(lyrics, Is.Not.Null);
            Assert.That(lyrics.transform.childCount, Is.EqualTo(44));
            Assert.That(GameObject.Find("VRCWorld"), Is.Not.Null);
            Assert.That(EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == ScenePath), Is.True);

            var audioImporter = AssetImporter.GetAtPath(AudioPath) as AudioImporter;
            Assert.That(audioImporter, Is.Not.Null);
            Assert.That(audioImporter.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
        }
    }
}
