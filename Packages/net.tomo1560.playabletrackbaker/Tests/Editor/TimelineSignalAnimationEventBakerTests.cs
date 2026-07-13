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
