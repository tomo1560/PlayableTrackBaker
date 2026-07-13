using System.Linq;
using NUnit.Framework;

namespace GAIALyricsMovie.Tests
{
    public sealed class LrcParserTests
    {
        [Test]
        public void Parse_ExtractsTimedLyricsAndIgnoresMetadataAndBlankEntries()
        {
            const string source =
                "[ti:GAIA]\n" +
                "[00:09.28] GAIA 導いて\n" +
                "[00:04.70] GAIA ねぇ教えて\n" +
                "[00:14.53] ♪\n" +
                "[00:48.03]   \n";

            var result = LrcParser.Parse(source, 60d);

            Assert.That(result.Select(entry => entry.Text), Is.EqualTo(new[]
            {
                "GAIA ねぇ教えて",
                "GAIA 導いて",
                "♪",
            }));
            Assert.That(result.Select(entry => entry.StartTime), Is.EqualTo(new[]
            {
                4.70d,
                9.28d,
                14.53d,
            }).Within(0.001d));
            Assert.That(result.Select(entry => entry.EndTime), Is.EqualTo(new[]
            {
                9.28d,
                14.53d,
                60d,
            }).Within(0.001d));
        }

        [Test]
        public void Parse_ClampsFinalEntryToSongDurationAndRejectsInvalidInput()
        {
            const string source = "broken\n[03:07.97] Ah\n[09:00.00] after song\n[00:60.00] invalid seconds";

            var result = LrcParser.Parse(source, 221.504d);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].StartTime, Is.EqualTo(187.97d).Within(0.001d));
            Assert.That(result[0].EndTime, Is.EqualTo(221.504d).Within(0.001d));
            Assert.That(LrcParser.Parse("[00:01.00] ignored", double.NaN), Is.Empty);
        }
    }
}
