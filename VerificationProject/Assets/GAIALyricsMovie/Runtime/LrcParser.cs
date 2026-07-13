using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GAIALyricsMovie
{
    [Serializable]
    public sealed class LyricCue
    {
        public LyricCue(double startTime, double endTime, string text)
        {
            StartTime = startTime;
            EndTime = endTime;
            Text = text;
        }

        public double StartTime { get; }
        public double EndTime { get; }
        public string Text { get; }
    }

    public static class LrcParser
    {
        static readonly Regex TimedLine = new Regex(
            @"^\[(?<minutes>\d{1,3}):(?<seconds>\d{1,2}(?:\.\d{1,3})?)\](?<text>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IReadOnlyList<LyricCue> Parse(string source, double songDuration)
        {
            if (string.IsNullOrWhiteSpace(source) || songDuration <= 0d
                || double.IsNaN(songDuration) || double.IsInfinity(songDuration))
                return Array.Empty<LyricCue>();

            var timedEntries = new List<(double Time, string Text)>();
            foreach (string line in source.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                Match match = TimedLine.Match(line.Trim());
                if (!match.Success || string.IsNullOrWhiteSpace(match.Groups["text"].Value))
                    continue;

                if (!double.TryParse(match.Groups["seconds"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double seconds)
                    || !int.TryParse(match.Groups["minutes"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int minutes))
                    continue;

                if (seconds < 0d || seconds >= 60d)
                    continue;

                double time = minutes * 60d + seconds;
                if (time < 0d || time >= songDuration)
                    continue;

                timedEntries.Add((time, match.Groups["text"].Value.Trim()));
            }

            timedEntries.Sort((left, right) => left.Time.CompareTo(right.Time));
            var cues = new List<LyricCue>(timedEntries.Count);
            for (int index = 0; index < timedEntries.Count; index++)
            {
                var entry = timedEntries[index];
                double nextTime = index + 1 < timedEntries.Count
                    ? Math.Min(timedEntries[index + 1].Time, songDuration)
                    : songDuration;
                if (nextTime > entry.Time)
                    cues.Add(new LyricCue(entry.Time, nextTime, entry.Text));
            }

            return cues;
        }
    }
}
