#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        static readonly Regex CSharpIdentifierPattern = new Regex(
            @"^[_\p{L}\p{Nl}][_\p{L}\p{Nl}\p{Mn}\p{Mc}\p{Nd}\p{Pc}\p{Cf}]*$",
            RegexOptions.CultureInvariant);
        static readonly HashSet<string> CSharpKeywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
            "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
            "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
            "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
            "object", "operator", "out", "override", "params", "private", "protected",
            "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
            "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while",
        };

        internal static int AddRoutedSignalEvents(
            TimelineAsset timeline,
            AnimationClip eventHost,
            IEnumerable<SignalEventRoute> routes,
            TimelineAsset routeSourceTimeline = null)
        {
            if (timeline == null)
                throw new ArgumentNullException(nameof(timeline));
            if (eventHost == null)
                throw new ArgumentNullException(nameof(eventHost));
            if (!IsClipOwnedByTimeline(timeline, eventHost))
                throw new ArgumentException("eventHost は指定した Timeline の AnimationTrack に含まれる clip である必要があります。", nameof(eventHost));

            var validRoutes = (routes ?? Enumerable.Empty<SignalEventRoute>())
                .Where(route => route != null && route.signal != null)
                .ToList();
            foreach (var route in validRoutes)
            {
                if (!IsValidCSharpIdentifier(route.udonEventName))
                    throw new ArgumentException(
                        $"Udon event 名 '{route.udonEventName}' は有効な C# 識別子である必要があります。",
                        nameof(routes));
            }
            if (validRoutes.GroupBy(route => route.signal).Any(group => group.Count() > 1))
                throw new ArgumentException(
                    "同じ SignalAsset に複数の Udon event route を指定することはできません。",
                    nameof(routes));

            routeSourceTimeline = routeSourceTimeline ?? timeline;

            var routedEmitters = EnumerateSignalEmitters(timeline)
                .Select(emitter => (emitter, route: FindRoute(
                    timeline, routeSourceTimeline, emitter.asset, validRoutes)))
                .Where(item => item.route != null)
                .OrderBy(item => item.emitter.time)
                .ToList();

            foreach (var item in routedEmitters)
            {
                if (item.emitter.retroactive)
                    throw new NotSupportedException(
                        "SignalEmitter.retroactive は AnimationEvent へ安全に変換できません。無効にするか Udon 側で再生開始時の状態を処理してください。");
                if (item.emitter.emitOnce)
                    throw new NotSupportedException(
                        "SignalEmitter.emitOnce は AnimationEvent へ安全に変換できません。無効にするか Udon 側でループ時の重複を抑止してください。");
            }

            var routedEvents = routedEmitters
                .Select(item => new AnimationEvent
                {
                    time = (float)item.emitter.time,
                    functionName = SendCustomEventMethod,
                    stringParameter = item.route.udonEventName,
                })
                .ToArray();

            // event host は本ツールが生成・所有する clip である。以前の Signal route を変更して
            // 再ベイクした場合にも古い SendCustomEvent を残さないよう置換し、それ以外の event は保持する。
            var mergedEvents = (eventHost.events ?? Array.Empty<AnimationEvent>())
                .Where(animationEvent => animationEvent.functionName != SendCustomEventMethod)
                .Concat(routedEvents)
                .OrderBy(animationEvent => animationEvent.time)
                .ToArray();
            // AnimationClip.events の setter は Editor で永続アセットへ書き込めない。
            // Unity の公式 Editor API を通して設定し、手動・非破壊ベイクの双方で保存可能にする。
            AnimationUtility.SetAnimationEvents(eventHost, mergedEvents);
            return routedEvents.Length;
        }

        static SignalEventRoute FindRoute(
            TimelineAsset timeline,
            TimelineAsset routeSourceTimeline,
            SignalAsset emittedSignal,
            IEnumerable<SignalEventRoute> routes)
        {
            if (emittedSignal == null)
                return null;
            var direct = routes.FirstOrDefault(route => route.signal == emittedSignal);
            if (direct != null)
                return direct;

            // 非破壊ベイクの CopyAsset で Timeline 内の SignalAsset も複製された場合、
            // 元 route と clone emitter は別インスタンスになる。Timeline 自身の subasset に限り
            // stable local file ID で照合し、別アセット間の同じ file ID を誤結合しない。
            string emittedTimelinePath = AssetDatabase.GetAssetPath(timeline);
            string routeSourcePath = AssetDatabase.GetAssetPath(routeSourceTimeline);
            if (string.IsNullOrEmpty(emittedTimelinePath) || string.IsNullOrEmpty(routeSourcePath) ||
                AssetDatabase.GetAssetPath(emittedSignal) != emittedTimelinePath ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(emittedSignal, out _, out long emittedId))
                return null;
            return routes.FirstOrDefault(route =>
                AssetDatabase.GetAssetPath(route.signal) == routeSourcePath &&
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
                if ((!(track is SignalTrack) && track != timeline.markerTrack) || track.mutedInHierarchy)
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

        static bool IsValidCSharpIdentifier(string value)
            => !string.IsNullOrEmpty(value) &&
                CSharpIdentifierPattern.IsMatch(value) &&
                !CSharpKeywords.Contains(value);
    }
}
#endif
