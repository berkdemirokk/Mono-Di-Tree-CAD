using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

public sealed class NumberWordsTests
{
    private static readonly string[] OnesWords = ["", "bir", "iki", "uc", "dort", "bes", "alti", "yedi", "sekiz", "dokuz"];
    private static readonly string[] TensWords = ["", "on", "yirmi", "otuz", "kirk", "elli", "altmis", "yetmis", "seksen", "doksan"];

    // Test-local reference spelling used to cross-check the reader (independent of the library).
    private static string SpellBelowThousand(int n)
    {
        var parts = new List<string>();
        var hundreds = n / 100;
        if (hundreds == 1) parts.Add("yuz");
        else if (hundreds > 1) { parts.Add(OnesWords[hundreds]); parts.Add("yuz"); }
        if (n / 10 % 10 > 0) parts.Add(TensWords[n / 10 % 10]);
        if (n % 10 > 0) parts.Add(OnesWords[n % 10]);
        return string.Join(' ', parts);
    }

    private static string Spell(int n)
    {
        if (n == 0) return "sifir";
        var parts = new List<string>();
        var thousands = n / 1000;
        if (thousands == 1) parts.Add("bin");
        else if (thousands > 1) { parts.Add(SpellBelowThousand(thousands)); parts.Add("bin"); }
        if (n % 1000 > 0) parts.Add(SpellBelowThousand(n % 1000));
        return string.Join(' ', parts);
    }

    private static string[] Split(string phrase) =>
        phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static NumberWordMatch Read(string phrase, int start = 0)
    {
        Assert.True(NumberWords.TryRead(Split(phrase), start, out var match), $"expected a number in '{phrase}' at {start}");
        return match;
    }

    private static List<NumberWordMatch> ReadAll(string phrase)
    {
        var words = Split(phrase);
        var result = new List<NumberWordMatch>();
        var index = 0;
        while (index < words.Length)
        {
            Assert.True(NumberWords.TryRead(words, index, out var match), $"no number at {index} in '{phrase}'");
            result.Add(match);
            index += match.WordCount;
        }
        return result;
    }

    [Theory]
    [InlineData("sifir", 0)]
    [InlineData("bir", 1)]
    [InlineData("iki", 2)]
    [InlineData("uc", 3)]
    [InlineData("dort", 4)]
    [InlineData("bes", 5)]
    [InlineData("alti", 6)]
    [InlineData("yedi", 7)]
    [InlineData("sekiz", 8)]
    [InlineData("dokuz", 9)]
    [InlineData("on", 10)]
    [InlineData("yirmi", 20)]
    [InlineData("otuz", 30)]
    [InlineData("kirk", 40)]
    [InlineData("elli", 50)]
    [InlineData("altmis", 60)]
    [InlineData("yetmis", 70)]
    [InlineData("seksen", 80)]
    [InlineData("doksan", 90)]
    [InlineData("yuz", 100)]
    [InlineData("bin", 1000)]
    public void SingleVocabularyWordReadsItsValue(string word, int expected)
    {
        var match = Read(word);
        Assert.Equal(expected, match.Value);
        Assert.Equal(1, match.WordCount);
        Assert.False(match.HasSuffix);
        Assert.False(match.TooLarge);
        Assert.True(NumberWords.IsNumberWord(word));
    }

    [Theory]
    [InlineData("bucuk")]
    [InlineData("yarim")]
    [InlineData("virgul")]
    [InlineData("yuze")]
    [InlineData("bese")]
    [InlineData("dorde")]
    [InlineData("ona")]
    [InlineData("onluk")]
    [InlineData("altiya")]
    [InlineData("binlik")]
    public void NumberVocabularyIncludesHelpersAndSuffixedWords(string word)
    {
        Assert.True(NumberWords.IsNumberWord(word));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("120")]
    [InlineData("1,5")]
    [InlineData("carpi")]
    [InlineData("santim")]
    [InlineData("mm")]
    [InlineData("YUZ")]
    [InlineData("yuzz")]
    [InlineData("bira")]
    [InlineData("yuzler")]
    [InlineData("birinci")]
    [InlineData("sifirbir")]
    public void NonNumberWordsAreRejected(string? word)
    {
        Assert.False(NumberWords.IsNumberWord(word));
    }

    [Fact]
    public void AllNumbersBelowHundredThousandRoundTripThroughReferenceSpelling()
    {
        for (var n = 0; n <= NumberWords.MaxValue; n++)
        {
            var phrase = Spell(n);
            var words = Split(phrase);
            Assert.True(NumberWords.TryRead(words, 0, out var match), phrase);
            Assert.Equal(n, match.Value);
            Assert.Equal(words.Length, match.WordCount);
            Assert.False(match.HasSuffix);
            Assert.False(match.TooLarge);
        }
    }

    [Fact]
    public void AllNumbersZeroToOneHundredTwentyReadFromWords()
    {
        for (var n = 0; n <= 120; n++)
            Assert.Equal(n, Read(Spell(n)).Value);
    }

    [Fact]
    public void EveryMultipleOfTenUpToOneThousandReads()
    {
        for (var n = 10; n <= 1000; n += 10)
        {
            var match = Read(Spell(n));
            Assert.Equal(n, match.Value);
            Assert.Equal(Split(Spell(n)).Length, match.WordCount);
        }
    }

    [Theory]
    [InlineData("yuz yirmi", 120, 2)]
    [InlineData("iki yuz", 200, 2)]
    [InlineData("bir yuz", 100, 2)]
    [InlineData("bin bes yuz", 1500, 3)]
    [InlineData("bir bin", 1000, 2)]
    [InlineData("bir bin bir", 1001, 3)]
    [InlineData("on iki bin uc yuz kirk bes", 12345, 7)]
    [InlineData("doksan dokuz bin dokuz yuz doksan dokuz", 99999, 7)]
    [InlineData("yirmi bin", 20000, 2)]
    [InlineData("on bin", 10000, 2)]
    [InlineData("bin yirmi", 1020, 2)]
    [InlineData("bin elli bes", 1055, 3)]
    [InlineData("dokuz yuz doksan dokuz", 999, 4)]
    [InlineData("yuz bir", 101, 2)]
    [InlineData("on bir", 11, 2)]
    [InlineData("doksan dokuz", 99, 2)]
    public void CompoundNumbersReadGreedily(string phrase, int expected, int words)
    {
        var match = Read(phrase);
        Assert.Equal(expected, match.Value);
        Assert.Equal(words, match.WordCount);
        Assert.False(match.TooLarge);
    }

    [Theory]
    [InlineData("yuz bin", 100000)]
    [InlineData("yuz elli bin", 150000)]
    [InlineData("yuz bir bin", 101000)]
    [InlineData("iki yuz bin", 200000)]
    [InlineData("dokuz yuz doksan dokuz bin dokuz yuz doksan dokuz", 999999)]
    [InlineData("yuz bin bir", 100001)]
    public void ValuesAboveMaximumAreFlaggedTooLargeButConsumed(string phrase, int expected)
    {
        var match = Read(phrase);
        Assert.Equal(expected, match.Value);
        Assert.True(match.TooLarge);
        Assert.Equal(Split(phrase).Length, match.WordCount);
    }

    [Fact]
    public void MaximumValueIsNotTooLarge()
    {
        var match = Read("doksan dokuz bin dokuz yuz doksan dokuz");
        Assert.Equal(NumberWords.MaxValue, match.Value);
        Assert.False(match.TooLarge);
    }

    [Theory]
    [InlineData("bir bucuk", 1.5, 2)]
    [InlineData("iki bucuk", 2.5, 2)]
    [InlineData("on bucuk", 10.5, 2)]
    [InlineData("yuz elli bucuk", 150.5, 3)]
    [InlineData("bin bucuk", 1000.5, 2)]
    [InlineData("on iki bucuk", 12.5, 3)]
    public void BucukAddsHalfAfterAnInteger(string phrase, double expected, int words)
    {
        var match = Read(phrase);
        Assert.Equal((decimal)expected, match.Value);
        Assert.Equal(words, match.WordCount);
    }

    [Fact]
    public void BucukAtStartOrAfterZeroIsNotPartOfANumber()
    {
        Assert.False(NumberWords.TryRead(Split("bucuk"), 0, out _));
        Assert.False(NumberWords.TryRead(Split("bucuk bir"), 0, out _));
        var zero = Read("sifir bucuk");
        Assert.Equal(0m, zero.Value);
        Assert.Equal(1, zero.WordCount);
        Assert.False(NumberWords.TryRead(Split("sifir bucuk"), 1, out _));
    }

    [Fact]
    public void BucukEndsTheNumber()
    {
        var all = ReadAll("iki bucuk bir");
        Assert.Equal(2, all.Count);
        Assert.Equal(2.5m, all[0].Value);
        Assert.Equal(1m, all[1].Value);
        Assert.False(NumberWords.TryRead(Split("iki bucuk bucuk"), 2, out _));
    }

    [Fact]
    public void YarimIsHalfOnItsOwnAndEndsTheNumber()
    {
        var match = Read("yarim");
        Assert.Equal(0.5m, match.Value);
        Assert.Equal(1, match.WordCount);
        var two = ReadAll("yarim yarim");
        Assert.Equal(2, two.Count);
        Assert.All(two, m => Assert.Equal(0.5m, m.Value));
    }

    [Theory]
    [InlineData("on iki virgul bes", 12.5, 4)]
    [InlineData("bir virgul bes", 1.5, 3)]
    [InlineData("sifir virgul bes", 0.5, 3)]
    [InlineData("iki virgul yirmi bes", 2.25, 4)]
    [InlineData("sifir virgul yuz yirmi bes", 0.125, 5)]
    [InlineData("bir virgul sifir bes", 1.05, 4)]
    [InlineData("bir virgul sifir sifir bir", 1.001, 5)]
    [InlineData("bir virgul sifir", 1.0, 3)]
    [InlineData("bir virgul sifir sifir", 1.0, 4)]
    [InlineData("sifir virgul sifir", 0.0, 3)]
    [InlineData("yuz virgul bir", 100.1, 3)]
    [InlineData("bir virgul yirmi", 1.2, 3)]
    [InlineData("bir virgul on", 1.1, 3)]
    [InlineData("bin virgul bes", 1000.5, 3)]
    public void VirgulReadsAnExactDecimalFraction(string phrase, double expected, int words)
    {
        var match = Read(phrase);
        Assert.Equal((decimal)expected, match.Value);
        Assert.Equal(words, match.WordCount);
        Assert.False(match.HasSuffix);
    }

    [Fact]
    public void VirgulFractionIsExactDecimalArithmetic()
    {
        Assert.Equal(0.125m, Read("sifir virgul yuz yirmi bes").Value);
        Assert.Equal(0.000001m, Read("sifir virgul sifir sifir sifir sifir sifir bir").Value);
        Assert.Equal(0.999m, Read("sifir virgul dokuz yuz doksan dokuz").Value);
        Assert.Equal(0.000999m, Read("sifir virgul sifir sifir sifir dokuz yuz doksan dokuz").Value);
    }

    [Fact]
    public void VirgulReadsOnlyOneGroupAfterTheZeros()
    {
        var all = ReadAll("sifir virgul bir iki bes");
        Assert.Equal(3, all.Count);
        Assert.Equal(0.1m, all[0].Value);
        Assert.Equal(3, all[0].WordCount);
        Assert.Equal(2m, all[1].Value);
        Assert.Equal(5m, all[2].Value);
    }

    [Fact]
    public void VirgulFractionWithMoreThanSixDigitsIsNotPartOfTheNumber()
    {
        // 7 zeros -> 7 digits.
        var tooLong = Read("bir virgul sifir sifir sifir sifir sifir sifir sifir bir");
        Assert.Equal(1m, tooLong.Value);
        Assert.Equal(1, tooLong.WordCount);
        // 5 zeros + group of 2 digits = 7 digits.
        var tooLong2 = Read("bir virgul sifir sifir sifir sifir sifir on");
        Assert.Equal(1m, tooLong2.Value);
        Assert.Equal(1, tooLong2.WordCount);
        // exactly 6 digits is fine.
        var exact = Read("bir virgul sifir sifir sifir sifir on");
        Assert.Equal(1.000010m, exact.Value);
        Assert.Equal(7, exact.WordCount);
    }

    [Theory]
    [InlineData("bir virgul")]
    [InlineData("bir virgul carpi")]
    [InlineData("bir virgul virgul bes")]
    [InlineData("bir virgul bucuk")]
    [InlineData("bir virgul yarim")]
    [InlineData("bir virgul bin")]
    public void VirgulWithoutAValidFractionStopsBeforeVirgul(string phrase)
    {
        var match = Read(phrase);
        Assert.Equal(1m, match.Value);
        Assert.Equal(1, match.WordCount);
    }

    [Fact]
    public void VirgulAtTheStartIsNotANumber()
    {
        Assert.False(NumberWords.TryRead(Split("virgul bes"), 0, out _));
        Assert.True(NumberWords.TryRead(Split("virgul bes"), 1, out var five));
        Assert.Equal(5m, five.Value);
    }

    [Fact]
    public void BucukAfterAFractionIsNotPartOfTheNumber()
    {
        var match = Read("bir virgul bes bucuk");
        Assert.Equal(1.5m, match.Value);
        Assert.Equal(3, match.WordCount);
        Assert.False(NumberWords.TryRead(Split("bir virgul bes bucuk"), 3, out _));
    }

    [Fact]
    public void VirgulFractionGroupFollowsTheBelowThousandRules()
    {
        // "virgul bin" is not a fraction group (bin is not part of a below-1000 group).
        var match = Read("iki virgul yirmi bin");
        Assert.Equal(2.2m, match.Value);
        Assert.Equal(3, match.WordCount);
    }

    [Theory]
    [InlineData("bir iki", 1, 2)]
    [InlineData("bir bir", 1, 1)]
    [InlineData("on bir iki", 11, 2)]
    [InlineData("yirmi otuz", 20, 30)]
    [InlineData("on yuz", 10, 100)]
    [InlineData("bin bin", 1000, 1000)]
    [InlineData("yuz yuz", 100, 100)]
    [InlineData("elli on", 50, 10)]
    [InlineData("bir yirmi", 1, 20)]
    public void InvalidContinuationsSplitIntoTwoNumbers(string phrase, int first, int second)
    {
        var all = ReadAll(phrase);
        Assert.Equal(2, all.Count);
        Assert.Equal(first, all[0].Value);
        Assert.Equal(second, all[1].Value);
    }

    [Fact]
    public void GreedySplittingUsesStartOffsets()
    {
        var words = Split("yuz yirmi seksen");
        Assert.True(NumberWords.TryRead(words, 0, out var first));
        Assert.Equal(120m, first.Value);
        Assert.Equal(2, first.WordCount);
        Assert.True(NumberWords.TryRead(words, 2, out var second));
        Assert.Equal(80m, second.Value);
        Assert.Equal(1, second.WordCount);
        Assert.True(NumberWords.TryRead(words, 1, out var inner));
        Assert.Equal(20m, inner.Value);
    }

    [Fact]
    public void ReadingStopsBeforeNonNumberWords()
    {
        var words = Split("yuz yirmi carpi seksen");
        Assert.True(NumberWords.TryRead(words, 0, out var first));
        Assert.Equal(120m, first.Value);
        Assert.Equal(2, first.WordCount);
        Assert.False(NumberWords.TryRead(words, 2, out _));
        Assert.True(NumberWords.TryRead(words, 3, out var last));
        Assert.Equal(80m, last.Value);
    }

    [Theory]
    [InlineData("yuze", 100, 1)]
    [InlineData("yirmi bese", 25, 2)]
    [InlineData("ona", 10, 1)]
    [InlineData("dorde", 4, 1)]
    [InlineData("dorda", 4, 1)]
    [InlineData("yuzluk", 100, 1)]
    [InlineData("binlik", 1000, 1)]
    [InlineData("bine", 1000, 1)]
    [InlineData("onluk", 10, 1)]
    [InlineData("altiya", 6, 1)]
    [InlineData("altilik", 6, 1)]
    [InlineData("yediye", 7, 1)]
    [InlineData("kirka", 40, 1)]
    [InlineData("elliye", 50, 1)]
    [InlineData("seksene", 80, 1)]
    [InlineData("doksana", 90, 1)]
    [InlineData("bire", 1, 1)]
    [InlineData("ikiye", 2, 1)]
    [InlineData("uce", 3, 1)]
    [InlineData("ucluk", 3, 1)]
    [InlineData("yirmiye", 20, 1)]
    [InlineData("otuza", 30, 1)]
    [InlineData("altmisa", 60, 1)]
    [InlineData("yetmise", 70, 1)]
    [InlineData("sifira", 0, 1)]
    [InlineData("dokuza", 9, 1)]
    [InlineData("sekize", 8, 1)]
    [InlineData("bese", 5, 1)]
    [InlineData("yuzden", 100, 1)]
    [InlineData("bin yuze", 1100, 2)]
    [InlineData("iki yuze", 200, 2)]
    [InlineData("yuz elli bese", 155, 3)]
    [InlineData("on iki bine", 12000, 3)]
    [InlineData("on iki binlik", 12000, 3)]
    [InlineData("bin yuzluk", 1100, 2)]
    [InlineData("doksan dokuzluk", 99, 2)]
    public void SuffixedLastWordIsReadWithHasSuffix(string phrase, int expected, int words)
    {
        var match = Read(phrase);
        Assert.Equal(expected, match.Value);
        Assert.Equal(words, match.WordCount);
        Assert.True(match.HasSuffix);
        Assert.False(match.TooLarge);
    }

    [Theory]
    [InlineData("altidan")]
    [InlineData("besten")]
    [InlineData("dortten")]
    [InlineData("kirktan")]
    [InlineData("altmistan")]
    [InlineData("yetmisten")]
    [InlineData("ucten")]
    [InlineData("ondan")]
    [InlineData("birden")]
    [InlineData("yuzden")]
    [InlineData("bucuga")]
    [InlineData("bucukluk")]
    [InlineData("bucuktan")]
    [InlineData("yarimlik")]
    public void AblativeAndSofteningFormsAreVocabulary(string word)
    {
        Assert.True(NumberWords.IsNumberWord(word));
    }

    [Fact]
    public void ReadingStopsAfterASuffixedWord()
    {
        var all = ReadAll("yuze yirmi");
        Assert.Equal(2, all.Count);
        Assert.Equal(100m, all[0].Value);
        Assert.True(all[0].HasSuffix);
        Assert.Equal(20m, all[1].Value);
        Assert.False(all[1].HasSuffix);

        var bin = ReadAll("bine yuz");
        Assert.Equal(2, bin.Count);
        Assert.Equal(1000m, bin[0].Value);
        Assert.Equal(100m, bin[1].Value);

        var inner = ReadAll("bire yuz");
        Assert.Equal(2, inner.Count);
        Assert.Equal(1m, inner[0].Value);
        Assert.True(inner[0].HasSuffix);
        Assert.Equal(100m, inner[1].Value);

        var noBin = ReadAll("yirmiye bin");
        Assert.Equal(20m, noBin[0].Value);
        Assert.Equal(1000m, noBin[1].Value);
    }

    [Fact]
    public void SuffixedWordCannotMultiplyOrBeMultiplied()
    {
        // "bire yuz" must not read as 100 (suffixed digit is not a hundreds multiplier).
        var match = Read("bire yuz");
        Assert.Equal(1m, match.Value);
        Assert.Equal(1, match.WordCount);
    }

    [Fact]
    public void SuffixAfterAFractionMarksTheNumber()
    {
        var match = Read("bir virgul bese");
        Assert.Equal(1.5m, match.Value);
        Assert.Equal(3, match.WordCount);
        Assert.True(match.HasSuffix);
    }

    [Fact]
    public void SuffixedBucukEndsTheNumber()
    {
        var match = Read("iki bucuga");
        Assert.Equal(2.5m, match.Value);
        Assert.Equal(2, match.WordCount);
        Assert.True(match.HasSuffix);
    }

    [Fact]
    public void SuffixedZeroAndHalfAreReadAsNumbers()
    {
        var zero = Read("sifira");
        Assert.Equal(0m, zero.Value);
        Assert.True(zero.HasSuffix);
        var half = Read("yarimlik");
        Assert.Equal(0.5m, half.Value);
        Assert.True(half.HasSuffix);
    }

    [Theory]
    [InlineData("120")]
    [InlineData("1")]
    [InlineData("1,5")]
    [InlineData("bir2")]
    [InlineData("12")]
    public void DigitStringsAreNeverNumberWords(string word)
    {
        Assert.False(NumberWords.TryRead([word], 0, out var match));
        Assert.Equal(default, match);
    }

    [Theory]
    [InlineData("carpi")]
    [InlineData("santim")]
    [InlineData("yuzu")]
    [InlineData("onbir")]
    [InlineData("YUZ")]
    [InlineData("Yuz")]
    [InlineData("yüz")]
    [InlineData("altı")]
    [InlineData("bin1")]
    public void InvalidStartsReturnFalseAndDefaultMatch(string word)
    {
        var ok = NumberWords.TryRead([word, "bir"], 0, out var match);
        Assert.False(ok);
        Assert.Equal(default, match);
    }

    [Fact]
    public void NullListAndOutOfRangeStartsReturnFalse()
    {
        Assert.False(NumberWords.TryRead(null!, 0, out var nullMatch));
        Assert.Equal(default, nullMatch);
        var words = Split("bir iki");
        foreach (var start in new[] { -1, 2, 3, int.MinValue, int.MaxValue, -1000 })
        {
            Assert.False(NumberWords.TryRead(words, start, out var match));
            Assert.Equal(default, match);
        }
        Assert.False(NumberWords.TryRead(Array.Empty<string>(), 0, out _));
    }

    [Fact]
    public void NullAndEmptyEntriesAreTolerated()
    {
        string[] words = ["bir", null!, "", "iki", " "];
        Assert.True(NumberWords.TryRead(words, 0, out var first));
        Assert.Equal(1m, first.Value);
        Assert.Equal(1, first.WordCount);
        Assert.False(NumberWords.TryRead(words, 1, out _));
        Assert.False(NumberWords.TryRead(words, 2, out _));
        Assert.True(NumberWords.TryRead(words, 3, out var two));
        Assert.Equal(2m, two.Value);
        Assert.Equal(1, two.WordCount);
        Assert.False(NumberWords.TryRead(words, 4, out _));
    }

    [Fact]
    public void NullEntryInTheMiddleOfANumberStopsReading()
    {
        string[] words = ["yuz", null!, "yirmi"];
        Assert.True(NumberWords.TryRead(words, 0, out var match));
        Assert.Equal(100m, match.Value);
        Assert.Equal(1, match.WordCount);
    }

    [Fact]
    public void VeryLongGarbageListsNeverThrow()
    {
        var garbage = new string[50_000];
        for (var i = 0; i < garbage.Length; i++)
            garbage[i] = (i % 5) switch { 0 => "xyzzy", 1 => "", 2 => new string('a', 5000), 3 => "123", _ => "carpi" };
        for (var i = 0; i < garbage.Length; i += 97)
            Assert.False(NumberWords.TryRead(garbage, i, out _));
    }

    [Fact]
    public void VeryLongNumberListsReadBoundedPrefix()
    {
        var words = Enumerable.Repeat("bir", 100_000).ToArray();
        Assert.True(NumberWords.TryRead(words, 0, out var match));
        Assert.Equal(1m, match.Value);
        Assert.Equal(1, match.WordCount);

        var tens = Enumerable.Repeat("yuz", 100_000).ToArray();
        Assert.True(NumberWords.TryRead(tens, 0, out var hundred));
        Assert.Equal(100m, hundred.Value);
        Assert.Equal(1, hundred.WordCount);

        var thousands = Enumerable.Repeat("bin", 100_000).ToArray();
        Assert.True(NumberWords.TryRead(thousands, 0, out var thousand));
        Assert.Equal(1000m, thousand.Value);
        Assert.Equal(1, thousand.WordCount);
    }

    [Fact]
    public void LongVirgulAndZeroRunsAreBounded()
    {
        var words = new List<string> { "bir", "virgul" };
        words.AddRange(Enumerable.Repeat("sifir", 10_000));
        words.Add("bir");
        Assert.True(NumberWords.TryRead(words, 0, out var match));
        Assert.Equal(1m, match.Value);
        Assert.Equal(1, match.WordCount);

        var zeros = Enumerable.Repeat("sifir", 10_000).ToArray();
        Assert.True(NumberWords.TryRead(zeros, 0, out var zero));
        Assert.Equal(0m, zero.Value);
        Assert.Equal(1, zero.WordCount);
    }

    [Fact]
    public void ReadingDoesNotDependOnTheCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "tr-TR", "en-US", "de-DE" })
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
                var match = Read("on iki virgul bes");
                Assert.Equal(12.5m, match.Value);
                Assert.True(NumberWords.IsNumberWord("bir"));
                Assert.True(NumberWords.IsNumberWord("sifir"));
                Assert.False(NumberWords.IsNumberWord("BIR"));
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ReadingIsDeterministic()
    {
        var words = Split("on iki bin uc yuz kirk bes virgul bes");
        Assert.True(NumberWords.TryRead(words, 0, out var first));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(NumberWords.TryRead(words, 0, out var again));
            Assert.Equal(first, again);
        }
        Assert.Equal(12345.5m, first.Value);
        Assert.Equal(9, first.WordCount);
    }

    [Fact]
    public void ReadingDoesNotModifyTheInput()
    {
        var words = Split("yuz yirmi bes");
        var copy = (string[])words.Clone();
        NumberWords.TryRead(words, 0, out _);
        Assert.Equal(copy, words);
    }

    [Fact]
    public void EveryNumberWordFromTheReferenceSpellingIsInTheVocabulary()
    {
        for (var n = 0; n <= 9999; n += 7)
            foreach (var word in Split(Spell(n)))
                Assert.True(NumberWords.IsNumberWord(word), word);
    }

    [Fact]
    public void MaxValueConstantMatchesTheSpecification()
    {
        Assert.Equal(99999, NumberWords.MaxValue);
    }

    [Fact]
    public void ListWithAMixedSentenceReadsEachNumberAtItsOffset()
    {
        var words = Split("kol boyu yuz yirmi bes carpi uc bucuk");
        Assert.False(NumberWords.TryRead(words, 0, out _));
        Assert.False(NumberWords.TryRead(words, 1, out _));
        Assert.True(NumberWords.TryRead(words, 2, out var first));
        Assert.Equal(125m, first.Value);
        Assert.Equal(3, first.WordCount);
        Assert.False(NumberWords.TryRead(words, 5, out _));
        Assert.True(NumberWords.TryRead(words, 6, out var second));
        Assert.Equal(3.5m, second.Value);
        Assert.Equal(2, second.WordCount);
    }
}
