using System.Globalization;
using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

public sealed class TurkishTextTests
{
    [Theory]
    [InlineData("I", "ı")]
    [InlineData("İ", "i")]
    [InlineData("DİKDÖRTGEN", "dikdörtgen")]
    [InlineData("IŞIK", "ışık")]
    [InlineData("ISPARTA İZMİR", "ısparta izmir")]
    [InlineData("ÇĞÖŞÜ", "çğöşü")]
    [InlineData("i ı İ I", "i ı i ı")]
    [InlineData("", "")]
    public void LowerCaseFollowsTurkishRules(string input, string expected) =>
        Assert.Equal(expected, TurkishText.ToLower(input));

    [Theory]
    [InlineData("i", "İ")]
    [InlineData("ı", "I")]
    [InlineData("dikdörtgen", "DİKDÖRTGEN")]
    [InlineData("ışık", "IŞIK")]
    public void UpperCaseFollowsTurkishRules(string input, string expected) =>
        Assert.Equal(expected, TurkishText.ToUpper(input));

    [Theory]
    [InlineData("DİKDÖRTGEN", "dikdortgen")]
    [InlineData("dikdörtgen", "dikdortgen")]
    [InlineData("Dikdortgen", "dikdortgen")]
    [InlineData("SAĞA", "saga")]
    [InlineData("ÇOĞALT", "cogalt")]
    [InlineData("çoğalt", "cogalt")]
    [InlineData("Ön Beden", "on beden")]
    [InlineData("ıİiI", "iiii")]
    [InlineData("âîû", "aiu")]
    [InlineData("Bézier", "bezier")]
    [InlineData("kırk altı", "kirk alti")]
    public void FoldingMakesDottedAndDotlessAndAccentedLettersEqual(string input, string expected) =>
        Assert.Equal(expected, TurkishText.Fold(input));

    [Fact]
    public void FoldingKeepsTheLengthSoOffsetsCarryOver()
    {
        const string text = "ÇOĞALT İŞLEMİ ışık ÜÖ x 120";
        Assert.Equal(text.Length, TurkishText.Fold(text).Length);
        Assert.Equal(text.Length, TurkishText.ToLower(text).Length);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("az-Latn-AZ")]
    [InlineData("lt-LT")]
    public void NothingDependsOnTheCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("ışık", TurkishText.ToLower("IŞIK"));
            Assert.Equal("isik", TurkishText.Fold("IŞIK"));
            Assert.Equal("12,5", TurkishText.FormatNumber(12.5m));
            Assert.Equal("İ", TurkishText.ToUpper("i"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("  120 x 80  ", "120 x 80")]
    [InlineData("120\t x\n80", "120 x 80")]
    [InlineData("120 x 80.", "120 x 80")]
    [InlineData("120 x 80!?.", "120 x 80")]
    [InlineData("120 x 80 .", "120 x 80")]
    [InlineData("?", "?")]
    [InlineData("...", ".")]
    [InlineData("a   b", "a b")]
    [InlineData("100’e 150", "100'e 150")]
    [InlineData("100‘e 150", "100'e 150")]
    [InlineData("100´e 150", "100'e 150")]
    [InlineData("100`e 150", "100'e 150")]
    [InlineData("a b", "a b")]
    [InlineData("a​b", "a b")]
    [InlineData("a\u0001b", "a b")]
    [InlineData("", "")]
    public void NormalizeCleansWhiteSpaceQuotesAndTrailingPunctuation(string input, string expected) =>
        Assert.Equal(expected, TurkishText.Normalize(input));

    [Fact]
    public void NormalizeComposesDecomposedTurkishLetters()
    {
        Assert.Equal("dikdörtgen", TurkishText.Normalize("dikdörtgen"));
        Assert.Equal("çiz", TurkishText.Normalize("çiz"));
        Assert.Equal("İ", TurkishText.Normalize("İ"));
        Assert.Equal("sağa", TurkishText.Normalize("sağa".Replace("sağa", "sa" + "ğ" + "a")));
    }

    [Theory]
    [InlineData(12.5, "12,5")]
    [InlineData(120, "120")]
    [InlineData(0.125, "0,125")]
    [InlineData(1000.0, "1000")]
    [InlineData(-5, "-5")]
    public void NumbersAreFormattedWithATurkishCommaAndNoGrouping(double value, string expected) =>
        Assert.Equal(expected, TurkishText.FormatNumber(value));

    [Fact]
    public void FormattingNonFiniteNumbersNeverThrows()
    {
        Assert.Equal("0", TurkishText.FormatNumber(double.NaN));
        Assert.Equal("0", TurkishText.FormatNumber(double.PositiveInfinity));
        Assert.Equal("0", TurkishText.FormatNumber(1e300));
    }

    [Theory]
    [InlineData("yapay zeka ile çiz", true)]
    [InlineData("YAPAY ZEKA", true)]
    [InlineData("bir asistan", true)]
    [InlineData("modeli çiz", true)]
    [InlineData("MODEL", true)]
    [InlineData("120 x 80", false)]
    [InlineData("yapay", false)]
    [InlineData("", false)]
    public void ForbiddenWordsAreRecognised(string text, bool expected) =>
        Assert.Equal(expected, TurkishText.ContainsForbiddenWord(text));

    [Theory]
    [InlineData('0', true)]
    [InlineData('9', true)]
    [InlineData('a', false)]
    [InlineData('٣', false)]
    [InlineData('３', false)]
    public void OnlyAsciiDigitsAreDigits(char c, bool expected) => Assert.Equal(expected, TurkishText.IsDigit(c));

    [Fact]
    public void NullInputGivesEmptyStrings()
    {
        Assert.Equal(string.Empty, TurkishText.ToLower(null));
        Assert.Equal(string.Empty, TurkishText.ToUpper(null));
        Assert.Equal(string.Empty, TurkishText.Fold(null));
        Assert.Equal(string.Empty, TurkishText.Normalize(null));
    }
}
