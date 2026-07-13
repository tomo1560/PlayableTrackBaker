using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using PlayableTrackBaking;

namespace GAIALyricsMovie.Tests
{
    public sealed class GeneratedSceneTests
    {
        const string ScenePath = "Assets/GAIALyricsMovie/Scenes/GAIA_LyricsMovie.unity";
        const string AudioPath = "Assets/GAIALyricsMovie/Audio/GAIA.ogg";
        const string TempBakeFolder = "Assets/BakedTimelineClips/__ndbake_temp__";

        [Test]
        public void GeneratedScene_IsBuildReadyForAutomaticNonDestructiveBake()
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
            Assert.That(timeline.GetOutputTracks().OfType<PlayableTrack>().Single().muted, Is.False);
            Assert.That(timeline.GetOutputTracks().OfType<AnimationTrack>()
                .Count(track => track.name.StartsWith("[Baked]")), Is.Zero);

            TimelineBakeMarker marker = director.GetComponent<TimelineBakeMarker>();
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.director, Is.SameAs(director));
            Assert.That(marker.recordRoots, Has.Length.EqualTo(2));
            Assert.That(marker.recordRoots.All(root => root != null), Is.True);
            Assert.That(marker.frameRate, Is.EqualTo(30f));
            Assert.That(marker.highPrecision, Is.True);
            Assert.That(marker.highPrecisionReduction, Is.EqualTo(0.002f));
            Assert.That(AssetDatabase.FindAssets("l:GAIALyricsMovie.OwnedBake.v1"), Is.Empty);

            GameObject lyrics = GameObject.Find("Lyrics");
            Assert.That(lyrics, Is.Not.Null);
            Assert.That(lyrics.transform.childCount, Is.EqualTo(44));
            Assert.That(GameObject.Find("VRCWorld"), Is.Not.Null);
            Assert.That(EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == ScenePath), Is.True);

            var audioImporter = AssetImporter.GetAtPath(AudioPath) as AudioImporter;
            Assert.That(audioImporter, Is.Not.Null);
            Assert.That(audioImporter.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
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

                Assert.That(AssetDatabase.GetAssetPath(sourceTimeline), Is.EqualTo(sourcePath));
                Assert.That(sourceTimeline.GetOutputTracks().OfType<PlayableTrack>().Single().muted, Is.False);
                Assert.That(sourceTimeline.GetOutputTracks().OfType<AnimationTrack>()
                    .Any(track => track.name.StartsWith("[Baked]")), Is.False);
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
    }
}
