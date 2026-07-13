#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace PlayableTrackBaking
{
    /// <summary>
    /// Timeline SignalEmitter を、VRChat Worlds で許可された UdonBehaviour.SendCustomEvent 呼び出しへ変換する。
    /// SignalReceiver の UnityEvent を自動移植せず、利用者が明示した SignalAsset-to-event route だけを出力する。
    /// </summary>
    internal static class TimelineSignalAnimationEventBaker
    {
        const string SendCustomEventMethod = "SendCustomEvent";

        internal static int AddRoutedSignalEvents(
            TimelineAsset timeline, AnimationClip eventHost, IEnumerable<SignalEventRoute> routes)
        {
            if (timeline == null)
                throw new ArgumentNullException(nameof(timeline));
            if (eventHost == null)
                throw new ArgumentNullException(nameof(eventHost));
            if (!IsClipOwnedByTimeline(timeline, eventHost))
                throw new ArgumentException("eventHost は指定した Timeline の AnimationTrack に含まれる clip である必要があります。", nameof(eventHost));

            var validRoutes = (routes ?? Enumerable.Empty<SignalEventRoute>())
                .Where(route => route != null && route.signal != null && !string.IsNullOrWhiteSpace(route.udonEventName))
                .ToList();

            var routedEvents = EnumerateSignalEmitters(timeline)
                .Select(emitter => (emitter, route: FindRoute(timeline, emitter.asset, validRoutes)))
                .Where(item => item.route != null)
                .OrderBy(item => item.emitter.time)
                .Select(item => new AnimationEvent
                {
                    time = (float)item.emitter.time,
                    functionName = SendCustomEventMethod,
                    stringParameter = item.route.udonEventName,
                })
                .ToArray();

            if (routedEvents.Length == 0)
                return 0;

            // ユーザーが既に置いた AnimationEvent は保持する。PoC の生成物を安全に識別する
            // 仕組みは、TimelineBakeMarker へ永続的な route 設定を追加する次段で導入する。
            var mergedEvents = (eventHost.events ?? Array.Empty<AnimationEvent>())
                .Concat(routedEvents)
                .OrderBy(animationEvent => animationEvent.time)
                .ToArray();
            // AnimationClip.events の setter は Editor で永続アセットへ書き込めない。
            // Unity の公式 Editor API を通して設定し、手動・非破壊ベイクの双方で保存可能にする。
            AnimationUtility.SetAnimationEvents(eventHost, mergedEvents);
            return routedEvents.Length;
        }

        static SignalEventRoute FindRoute(
            TimelineAsset timeline, SignalAsset emittedSignal, IEnumerable<SignalEventRoute> routes)
        {
            if (emittedSignal == null)
                return null;
            var direct = routes.FirstOrDefault(route => route.signal == emittedSignal);
            if (direct != null)
                return direct;

            // 非破壊ベイクの CopyAsset で Timeline 内の SignalAsset も複製された場合、
            // 元 route と clone emitter は別インスタンスになる。Timeline 自身の subasset に限り
            // stable local file ID で照合し、別アセット間の同じ file ID を誤結合しない。
            if (AssetDatabase.GetAssetPath(emittedSignal) != AssetDatabase.GetAssetPath(timeline) ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(emittedSignal, out _, out long emittedId))
                return null;
            return routes.FirstOrDefault(route =>
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(route.signal, out _, out long routeId) &&
                routeId == emittedId);
        }

        static IEnumerable<SignalEmitter> EnumerateSignalEmitters(TimelineAsset timeline)
        {
            var tracks = TraverseTracks(timeline.GetRootTracks())
                .Concat(timeline.markerTrack != null
                    ? new[] { timeline.markerTrack }
                    : Enumerable.Empty<TrackAsset>())
                .Distinct();

            foreach (var track in tracks)
            {
                if (!(track is SignalTrack) && track != timeline.markerTrack)
                    continue;
                foreach (var emitter in track.GetMarkers().OfType<SignalEmitter>())
                    yield return emitter;
            }
        }

        static IEnumerable<TrackAsset> TraverseTracks(IEnumerable<TrackAsset> tracks)
        {
            foreach (var track in tracks.Where(track => track != null))
            {
                yield return track;
                foreach (var child in TraverseTracks(track.GetChildTracks()))
                    yield return child;
            }
        }

        static bool IsClipOwnedByTimeline(TimelineAsset timeline, AnimationClip eventHost)
            => TraverseTracks(timeline.GetRootTracks())
                .OfType<AnimationTrack>()
                .SelectMany(track => track.GetClips())
                .Any(timelineClip => timelineClip.asset is AnimationPlayableAsset animationAsset &&
                    animationAsset.clip == eventHost);
    }
}
#endif
