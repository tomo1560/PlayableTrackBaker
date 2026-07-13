using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

namespace PlayableTrackBaking.Tests
{
    /// <summary>
    /// SignalEmitter を VRChat Worlds 向けの Udon SendCustomEvent 呼び出しへ変換する
    /// 最小 PoC の契約を固定する。SignalEmitter は Clip ではなく Marker なので、
    /// SignalTrack と TimelineAsset.markerTrack の両方を明示的に対象とする。
    /// </summary>
    [TestFixture]
    public class TimelineSignalAnimationEventBakerTests
    {
        readonly List<UnityEngine.Object> _cleanup = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _cleanup)
                if (obj != null)
                    UnityEngine.Object.DestroyImmediate(obj);
            _cleanup.Clear();
        }

        [Test]
        public void AddRoutedSignalEvents_ConvertsSignalTrackAndGlobalMarkersToChosenHostClip()
        {
            var timeline = CreateTimeline();
            var signalTrack = timeline.CreateTrack<SignalTrack>(null, "Signals");
            var openSignal = CreateSignal("OpenDoor");
            var unlockSignal = CreateSignal("UnlockPuzzle");
            var ignoredSignal = CreateSignal("Unrouted");
            CreateEmitter(signalTrack, 0.75, openSignal);
            CreateEmitter(timeline.markerTrack, 1.5, unlockSignal);
            CreateEmitter(signalTrack, 2.0, ignoredSignal);

            var eventHost = CreateHostClip(timeline, "Event Host");
            var otherBakedClip = CreateHostClip(timeline, "Other Baked Result");
            var routes = new[]
            {
                new SignalEventRoute(openSignal, "OnOpenDoor"),
                new SignalEventRoute(unlockSignal, "OnUnlockPuzzle"),
            };

            int converted = TimelineSignalAnimationEventBaker.AddRoutedSignalEvents(
                timeline, eventHost, routes);

            Assert.AreEqual(2, converted);
            var events = eventHost.events.OrderBy(animationEvent => animationEvent.time).ToArray();
            Assert.AreEqual(2, events.Length);
            AssertEvent(events[0], 0.75f, "OnOpenDoor");
            AssertEvent(events[1], 1.5f, "OnUnlockPuzzle");
            Assert.IsEmpty(otherBakedClip.events,
                "Signal 用 AnimationEvent は、指定されていないベイク済み clip に複製してはならない");
        }

        [Test]
        public void AddRoutedSignalEvents_RejectsClipThatIsNotOwnedByTimeline()
        {
            var timeline = CreateTimeline();
            var signal = CreateSignal("OpenDoor");
            CreateEmitter(timeline.markerTrack, 0.25, signal);
            var detachedClip = new AnimationClip { name = "Detached Event Host" };
            _cleanup.Add(detachedClip);

            var exception = Assert.Throws<ArgumentException>(() =>
                TimelineSignalAnimationEventBaker.AddRoutedSignalEvents(
                    timeline,
                    detachedClip,
                    new[] { new SignalEventRoute(signal, "OnOpenDoor") }));

            StringAssert.Contains("eventHost", exception.ParamName);
            Assert.IsEmpty(detachedClip.events,
                "無効な event host を渡した場合、clip を部分的に変更してはならない");
        }

        [TestCase(true, false, "retroactive")]
        [TestCase(false, true, "emitOnce")]
        public void AddRoutedSignalEvents_RejectsSignalEmitterSemanticsThatCannotBeRepresentedByAnimationEvents(
            bool retroactive,
            bool emitOnce,
            string unsupportedSetting)
        {
            var timeline = CreateTimeline();
            var signal = CreateSignal("OpenDoor");
            var emitter = CreateEmitter(timeline.markerTrack, 0.25, signal);
            emitter.retroactive = retroactive;
            emitter.emitOnce = emitOnce;
            var eventHost = CreateHostClip(timeline, "Event Host");

            var exception = Assert.Throws<NotSupportedException>(() =>
                TimelineSignalAnimationEventBaker.AddRoutedSignalEvents(
                    timeline,
                    eventHost,
                    new[] { new SignalEventRoute(signal, "OnOpenDoor") }));

            StringAssert.Contains(unsupportedSetting, exception.Message);
            Assert.IsEmpty(eventHost.events,
                "AnimationEvent で同等に再現できない SignalEmitter 設定では、部分的なイベント出力を残してはならない");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AddConfiguredSignalEvents_MissingOrUnrecordedHostLeavesTimelineUntouched(bool missingHost)
        {
            var timeline = CreateTimeline();
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 4.0;
            var signal = CreateSignal("OpenDoor");
            CreateEmitter(timeline.markerTrack, 2.0, signal);
            var recordedHost = CreateHostClip(timeline, "Recorded Host");
            var unrecordedHost = new GameObject("Unrecorded Signal Host");
            _cleanup.Add(unrecordedHost);
            var markerObject = new GameObject("Signal Marker");
            _cleanup.Add(markerObject);
            var marker = markerObject.AddComponent<TimelineBakeMarker>();
            marker.bakeSignalEvents = true;
            marker.signalEventHost = missingHost ? null : unrecordedHost;
            marker.signalEventRoutes = new[] { new SignalEventRoute(signal, "OnOpenDoor") };

            var originalDuration = FindTimelineClip(timeline, recordedHost).duration;

            Assert.Throws<System.InvalidOperationException>(() =>
                PlayableTrackBakeCore.AddConfiguredSignalEvents(
                    timeline,
                    new List<(AnimationClip clip, GameObject root)> { (recordedHost, markerObject) },
                    new[] { marker }));

            Assert.IsEmpty(recordedHost.events,
                "無効な Signal Event Host 設定では AnimationEvent を追加してはならない");
            Assert.AreEqual(originalDuration, FindTimelineClip(timeline, recordedHost).duration, 0.0001,
                "無効な Signal Event Host 設定では TimelineClip の長さを変更してはならない");
        }

        [Test]
        public void AddConfiguredSignalEvents_StaticHostTrackCoversEntireTimeline()
        {
            var timeline = CreateTimeline();
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = 4.0;
            var signal = CreateSignal("OpenDoor");
            CreateEmitter(timeline.markerTrack, 3.5, signal);
            var eventHost = CreateHostClip(timeline, "Static Event Host");
            var hostObject = new GameObject("Static Signal Host");
            _cleanup.Add(hostObject);
            var markerObject = new GameObject("Signal Marker");
            _cleanup.Add(markerObject);
            var marker = markerObject.AddComponent<TimelineBakeMarker>();
            marker.bakeSignalEvents = true;
            marker.signalEventHost = hostObject;
            marker.signalEventRoutes = new[] { new SignalEventRoute(signal, "OnOpenDoor") };

            int added = PlayableTrackBakeCore.AddConfiguredSignalEvents(
                timeline,
                new List<(AnimationClip clip, GameObject root)> { (eventHost, hostObject) },
                new[] { marker });

            Assert.AreEqual(1, added);
            Assert.GreaterOrEqual(FindTimelineClip(timeline, eventHost).duration, timeline.duration,
                "静的な event host でも、後半の Signal が発火できるようベイク済み Track は Timeline 全長を覆うべき");
        }

        [Test]
        public void AddConfiguredSignalEvents_RebakeReplacesPreviouslyGeneratedEvents()
        {
            var timeline = CreateTimeline();
            var signal = CreateSignal("OpenDoor");
            CreateEmitter(timeline.markerTrack, 1.0, signal);
            var eventHost = CreateHostClip(timeline, "Event Host");
            var hostObject = new GameObject("Signal Host");
            _cleanup.Add(hostObject);
            var markerObject = new GameObject("Signal Marker");
            _cleanup.Add(markerObject);
            var marker = markerObject.AddComponent<TimelineBakeMarker>();
            marker.bakeSignalEvents = true;
            marker.signalEventHost = hostObject;
            marker.signalEventRoutes = new[] { new SignalEventRoute(signal, "OnOpenDoor") };
            var recorded = new List<(AnimationClip clip, GameObject root)> { (eventHost, hostObject) };

            Assert.AreEqual(1, PlayableTrackBakeCore.AddConfiguredSignalEvents(timeline, recorded, new[] { marker }));
            marker.signalEventRoutes = new[] { new SignalEventRoute(signal, "OnOpenDoorRebaked") };

            Assert.AreEqual(1, PlayableTrackBakeCore.AddConfiguredSignalEvents(timeline, recorded, new[] { marker }));

            var events = eventHost.events;
            Assert.AreEqual(1, events.Length,
                "再ベイクで以前に生成した Signal AnimationEvent を重複させてはならない");
            AssertEvent(events[0], 1.0f, "OnOpenDoorRebaked");
        }

        TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            // markerTrack は getter で遅延生成されないため、グローバル SignalEmitter を
            // 配置するテストでは明示的に作成する。
            timeline.CreateMarkerTrack();
            _cleanup.Add(timeline);
            return timeline;
        }

        SignalAsset CreateSignal(string name)
        {
            var signal = ScriptableObject.CreateInstance<SignalAsset>();
            signal.name = name;
            _cleanup.Add(signal);
            return signal;
        }

        static SignalEmitter CreateEmitter(TrackAsset track, double time, SignalAsset signal)
        {
            var emitter = track.CreateMarker<SignalEmitter>(time);
            emitter.asset = signal;
            return emitter;
        }

        AnimationClip CreateHostClip(TimelineAsset timeline, string name)
        {
            var clip = new AnimationClip { name = name };
            _cleanup.Add(clip);
            timeline.CreateTrack<AnimationTrack>(null, name).CreateClip(clip);
            return clip;
        }

        static TimelineClip FindTimelineClip(TimelineAsset timeline, AnimationClip clip)
            => timeline.GetOutputTracks()
                .OfType<AnimationTrack>()
                .SelectMany(track => track.GetClips())
                .Single(timelineClip => timelineClip.asset is AnimationPlayableAsset animationAsset &&
                    animationAsset.clip == clip);

        static void AssertEvent(AnimationEvent animationEvent, float expectedTime, string expectedUdonEventName)
        {
            Assert.AreEqual(expectedTime, animationEvent.time, 0.0001f);
            Assert.AreEqual("SendCustomEvent", animationEvent.functionName,
                "VRChat Worlds の allowlist にある UdonBehaviour 呼び出しだけを生成する");
            Assert.AreEqual(expectedUdonEventName, animationEvent.stringParameter,
                "明示的な SignalAsset-to-Udon event route を AnimationEvent の引数に保持する");
        }
    }
}
