using System.Diagnostics;
using System.Text;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

public sealed class CommandParserFuzzTests
{
    private static readonly string[] Pieces = { "Ön beden", "Arka beden", "Kol", "Ön beden 2" };

    private static readonly string[] Fragments =
    {
        "120", "80", "x", "×", "*", "çarpı", "dikdörtgen", "kare", "çizgi", "sağa", "sola", "yukarı", "aşağı", "taşı", "döndür",
        "aynala", "sil", "seç", "geri", "al", "yap", "olsun", "adı", "genişliği", "eni", "boyu", "cm", "mm", "santim", "milim", "m",
        "yüz", "yirmi", "bin", "buçuk", "yarım", "virgül", "on", "bir", "iki", "1.500", "1,500", "0,125", "12,5", "'e", "'lük",
        "ön", "bedeni", "kolu", "konumuna", "y", "dikiş", "payı", "pastal", "eksi", "-", "--", "?", ".", ",", "ı", "İ", "I", "i",
        "\t", "\n", "\u0000", "̇", "😀", "１２", "٣", "99999999999999999999", "1e999", "0x1F", "﷽", "ǅ", "ß", "ŉ",
    };

    private static string RandomLine(Random random)
    {
        var sb = new StringBuilder();
        var parts = random.Next(1, 12);
        for (var i = 0; i < parts; i++)
        {
            if (i > 0 && random.Next(4) > 0) sb.Append(' ');
            if (random.Next(5) == 0) sb.Append((char)random.Next(0, 0x2FFF)); // any BMP letter, mark or symbol
            else sb.Append(Fragments[random.Next(Fragments.Length)]);
        }
        return sb.ToString();
    }

    [Fact]
    public void RandomSentencesNeverThrowAndAlwaysAnswerWithACommandOrANotice()
    {
        var random = new Random(20261115);
        var context = new ParserContext(Pieces);
        for (var i = 0; i < 20000; i++)
        {
            var line = RandomLine(random);
            var result = CommandParser.Parse(line, context);
            Assert.NotNull(result);
            if (result.IsCommand)
            {
                Assert.Null(result.Notice);
                Assert.NotNull(result.Command);
            }
            else
            {
                Assert.NotNull(result.Notice);
                Assert.False(string.IsNullOrWhiteSpace(result.Notice!.Message));
                Assert.DoesNotContain("{", result.Notice.Message);
                Assert.False(TurkishText.ContainsForbiddenWord(result.Notice.Message.Replace("Şöyle yazabilirsiniz", string.Empty)), $"\"{line}\"");
            }
        }
    }

    [Fact]
    public void RandomCharactersFromAllOfTheBasicMultilingualPlaneNeverThrow()
    {
        var random = new Random(7);
        for (var i = 0; i < 20000; i++)
        {
            var length = random.Next(0, 60);
            var chars = new char[length];
            for (var k = 0; k < length; k++) chars[k] = (char)random.Next(0, 0x10000); // includes lone surrogates
            var result = CommandParser.Parse(new string(chars));
            Assert.NotNull(result);
        }
    }

    [Fact]
    public void HugeNumbersAndLongDigitStringsAreRefusedQuickly()
    {
        var stopwatch = Stopwatch.StartNew();
        foreach (var digits in new[] { 6, 9, 10, 18, 28, 29, 60, 150, 190 })
        {
            var number = new string('9', digits);
            foreach (var template in new[] { "{0} çizgi", "{0} x {0} dikdörtgen", "{0} sağa taşı", "x {0} y {0} konumuna taşı", "{0},{0} çizgi", "kare {0}.{0}" })
            {
                var result = CommandParser.Parse(string.Format(template, number), new ParserContext(Pieces));
                Assert.False(result.IsCommand, template + " " + digits);
            }
        }
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void InputsAtAndAboveTheLimitHaveBoundedTime()
    {
        var stopwatch = Stopwatch.StartNew();
        foreach (var unit in new[] { "yüz ", "1 ", "x ", "'e ", "bir buçuk ", "virgül ", ",", ".", "- ", "a", " " })
        {
            for (var length = 195; length <= 205; length++)
            {
                var sb = new StringBuilder();
                while (sb.Length < length) sb.Append(unit);
                var line = sb.ToString().Substring(0, length);
                var result = CommandParser.Parse(line, new ParserContext(Pieces));
                Assert.NotNull(result);
            }
        }
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ControlCharactersBetweenWordsAreWhiteSpace()
    {
        var result = CommandParser.Parse("120\u0000x\u0007 80\u001fdikdörtgen");
        Assert.True(result.IsCommand, result.ToString());
        Assert.Equal(new RectangleCommand(120, 80), result.Command);
    }

    [Fact]
    public void TheSameLineAlwaysGivesTheSameResult()
    {
        var random = new Random(99);
        for (var i = 0; i < 2000; i++)
        {
            var line = RandomLine(random);
            var a = CommandParser.Parse(line, new ParserContext(Pieces));
            var b = CommandParser.Parse(line, new ParserContext(Pieces));
            Assert.Equal(a.ToString(), b.ToString());
        }
    }

    [Fact]
    public void ParsingIsSafeFromManyThreadsAtOnce()
    {
        var lines = new[] { "120 x 80 dikdörtgen", "ön bedeni seç", "1.500 çizgi", "yüz yirmi çarpı seksen kutu", "zzz" };
        var expected = lines.Select(l => CommandParser.Parse(l, new ParserContext(Pieces)).ToString()).ToArray();
        Parallel.For(0, 4000, i =>
        {
            var k = i % lines.Length;
            Assert.Equal(expected[k], CommandParser.Parse(lines[k], new ParserContext(Pieces)).ToString());
        });
    }
}
