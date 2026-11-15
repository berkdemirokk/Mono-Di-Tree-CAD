using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

public sealed class UnitParserTests
{
    [Theory]
    [InlineData("mm", LengthUnit.Millimeter)]
    [InlineData("milim", LengthUnit.Millimeter)]
    [InlineData("milimetre", LengthUnit.Millimeter)]
    [InlineData("cm", LengthUnit.Centimeter)]
    [InlineData("santim", LengthUnit.Centimeter)]
    [InlineData("santimetre", LengthUnit.Centimeter)]
    [InlineData("m", LengthUnit.Meter)]
    [InlineData("metre", LengthUnit.Meter)]
    public void PlainUnitWordsParseWithoutSuffix(string word, LengthUnit expected)
    {
        Assert.True(UnitParser.TryParseUnitWord(word, out var unit, out var hasSuffix));
        Assert.Equal(expected, unit);
        Assert.False(hasSuffix);
    }

    [Theory]
    [InlineData("milimlik", LengthUnit.Millimeter)]
    [InlineData("milime", LengthUnit.Millimeter)]
    [InlineData("milimden", LengthUnit.Millimeter)]
    [InlineData("milimetrelik", LengthUnit.Millimeter)]
    [InlineData("milimetreye", LengthUnit.Millimeter)]
    [InlineData("milimetreden", LengthUnit.Millimeter)]
    [InlineData("santimlik", LengthUnit.Centimeter)]
    [InlineData("santime", LengthUnit.Centimeter)]
    [InlineData("santimden", LengthUnit.Centimeter)]
    [InlineData("santimetrelik", LengthUnit.Centimeter)]
    [InlineData("santimetreye", LengthUnit.Centimeter)]
    [InlineData("santimetreden", LengthUnit.Centimeter)]
    [InlineData("metrelik", LengthUnit.Meter)]
    [InlineData("metreye", LengthUnit.Meter)]
    [InlineData("metreden", LengthUnit.Meter)]
    public void LongStemsAcceptGluedSuffixes(string word, LengthUnit expected)
    {
        Assert.True(UnitParser.TryParseUnitWord(word, out var unit, out var hasSuffix));
        Assert.Equal(expected, unit);
        Assert.True(hasSuffix);
    }

    [Theory]
    [InlineData("mme")]
    [InlineData("mma")]
    [InlineData("mmlik")]
    [InlineData("mmden")]
    [InlineData("cme")]
    [InlineData("cmye")]
    [InlineData("cmlik")]
    [InlineData("cmden")]
    [InlineData("mlik")]
    [InlineData("me")]
    [InlineData("mye")]
    [InlineData("mden")]
    [InlineData("mdan")]
    public void ShortStemsRejectGluedSuffixes(string word)
    {
        Assert.False(UnitParser.TryParseUnitWord(word, out var unit, out var hasSuffix));
        Assert.Equal(default, unit);
        Assert.False(hasSuffix);
    }

    [Theory]
    [InlineData("mt")]
    [InlineData("milimetr")]
    [InlineData("santimetreler")]
    [InlineData("meter")]
    [InlineData("cm2")]
    [InlineData("m2")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("santim ")]
    [InlineData("SANTIM")]
    [InlineData("Cm")]
    [InlineData("MM")]
    [InlineData("kilometre")]
    [InlineData("inc")]
    [InlineData("santimlikler")]
    [InlineData("santimetreleri")]
    [InlineData("metrelike")]
    [InlineData("santima")]
    [InlineData("santimdan")]
    [InlineData("metreya")]
    [InlineData("metrea")]
    [InlineData("santimluk")]
    [InlineData("12")]
    [InlineData("mm.")]
    [InlineData("'lik")]
    public void NonUnitWordsAreRejected(string word)
    {
        Assert.False(UnitParser.TryParseUnitWord(word, out var unit, out var hasSuffix));
        Assert.Equal(default, unit);
        Assert.False(hasSuffix);
    }

    [Fact]
    public void NullAndHugeWordsAreRejectedWithoutThrowing()
    {
        Assert.False(UnitParser.TryParseUnitWord(null, out var unit, out var hasSuffix));
        Assert.Equal(default, unit);
        Assert.False(hasSuffix);
        Assert.False(UnitParser.TryParseUnitWord(new string('m', 100_000), out _, out _));
        Assert.False(UnitParser.TryParseUnitWord("santim" + new string('x', 10_000), out _, out _));
    }

    [Theory]
    [InlineData(LengthUnit.Millimeter, 1)]
    [InlineData(LengthUnit.Centimeter, 10)]
    [InlineData(LengthUnit.Meter, 1000)]
    public void MillimetersPerUnitMatchesTheMetricFactors(LengthUnit unit, int expected)
    {
        Assert.Equal(expected, UnitParser.MillimetersPerUnit(unit));
    }

    [Theory]
    [InlineData("1.5", LengthUnit.Meter, "1500")]
    [InlineData("12", LengthUnit.Centimeter, "120")]
    [InlineData("7", LengthUnit.Millimeter, "7")]
    [InlineData("0", LengthUnit.Meter, "0")]
    [InlineData("0.25", LengthUnit.Centimeter, "2.5")]
    [InlineData("-3", LengthUnit.Centimeter, "-30")]
    [InlineData("0.001", LengthUnit.Meter, "1")]
    public void ToMillimetersMultipliesByTheFactor(string value, LengthUnit unit, string expected)
    {
        var parsedValue = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var parsedExpected = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(parsedExpected, UnitParser.ToMillimeters(parsedValue, unit));
    }

    [Fact]
    public void ToMillimetersDoesNotThrowAtTheDecimalLimits()
    {
        Assert.Equal(decimal.MaxValue, UnitParser.ToMillimeters(decimal.MaxValue, LengthUnit.Meter));
        Assert.Equal(decimal.MinValue, UnitParser.ToMillimeters(decimal.MinValue, LengthUnit.Meter));
        Assert.Equal(decimal.MaxValue, UnitParser.ToMillimeters(decimal.MaxValue, LengthUnit.Millimeter));
    }

    [Theory]
    [InlineData(LengthUnit.Millimeter, "mm")]
    [InlineData(LengthUnit.Centimeter, "cm")]
    [InlineData(LengthUnit.Meter, "m")]
    public void ShortNamesMatchTheUnits(LengthUnit unit, string expected)
    {
        Assert.Equal(expected, UnitParser.ShortName(unit));
    }

    [Fact]
    public void ShortNameRoundTripsThroughTheParser()
    {
        foreach (var unit in Enum.GetValues<LengthUnit>())
        {
            Assert.True(UnitParser.TryParseUnitWord(UnitParser.ShortName(unit), out var parsed, out var hasSuffix));
            Assert.Equal(unit, parsed);
            Assert.False(hasSuffix);
        }
    }

    [Fact]
    public void UndefinedUnitValuesNeverThrow()
    {
        var undefined = (LengthUnit)42;
        Assert.Equal(1m, UnitParser.MillimetersPerUnit(undefined));
        Assert.Equal("mm", UnitParser.ShortName(undefined));
        Assert.Equal(5m, UnitParser.ToMillimeters(5m, undefined));
    }

    [Fact]
    public void UnitsAreOrderedFromSmallestToLargest()
    {
        Assert.True(LengthUnit.Millimeter < LengthUnit.Centimeter);
        Assert.True(LengthUnit.Centimeter < LengthUnit.Meter);
    }

    [Fact]
    public void CombineSumsDescendingUnits()
    {
        Assert.True(UnitParser.TryCombine([(1m, LengthUnit.Centimeter), (2m, LengthUnit.Millimeter)], out var mm));
        Assert.Equal(12m, mm);
    }

    [Fact]
    public void CombineSumsMetersAndCentimeters()
    {
        Assert.True(UnitParser.TryCombine([(1m, LengthUnit.Meter), (20m, LengthUnit.Centimeter)], out var mm));
        Assert.Equal(1200m, mm);
    }

    [Fact]
    public void CombineSumsMetersAndMillimeters()
    {
        Assert.True(UnitParser.TryCombine([(2m, LengthUnit.Meter), (5m, LengthUnit.Millimeter)], out var mm));
        Assert.Equal(2005m, mm);
    }

    [Fact]
    public void CombineSumsAllThreeUnits()
    {
        Assert.True(UnitParser.TryCombine(
            [(1m, LengthUnit.Meter), (2m, LengthUnit.Centimeter), (3m, LengthUnit.Millimeter)], out var mm));
        Assert.Equal(1023m, mm);
    }

    [Fact]
    public void CombineKeepsDecimalFractions()
    {
        Assert.True(UnitParser.TryCombine([(1.5m, LengthUnit.Centimeter), (0.5m, LengthUnit.Millimeter)], out var mm));
        Assert.Equal(15.5m, mm);
    }

    [Fact]
    public void CombineRejectsAscendingOrder()
    {
        Assert.False(UnitParser.TryCombine([(2m, LengthUnit.Millimeter), (1m, LengthUnit.Centimeter)], out var mm));
        Assert.Equal(0m, mm);
        Assert.False(UnitParser.TryCombine([(2m, LengthUnit.Centimeter), (1m, LengthUnit.Meter)], out _));
        Assert.False(UnitParser.TryCombine(
            [(1m, LengthUnit.Meter), (2m, LengthUnit.Millimeter), (3m, LengthUnit.Centimeter)], out _));
    }

    [Fact]
    public void CombineRejectsRepeatedUnits()
    {
        Assert.False(UnitParser.TryCombine([(1m, LengthUnit.Centimeter), (2m, LengthUnit.Centimeter)], out var mm));
        Assert.Equal(0m, mm);
        Assert.False(UnitParser.TryCombine([(1m, LengthUnit.Meter), (2m, LengthUnit.Meter)], out _));
        Assert.False(UnitParser.TryCombine([(1m, LengthUnit.Millimeter), (2m, LengthUnit.Millimeter)], out _));
        Assert.False(UnitParser.TryCombine(
            [(1m, LengthUnit.Meter), (2m, LengthUnit.Centimeter), (3m, LengthUnit.Centimeter)], out _));
    }

    [Fact]
    public void CombineRejectsSingleEmptyAndNullLists()
    {
        Assert.False(UnitParser.TryCombine([(1m, LengthUnit.Centimeter)], out var single));
        Assert.Equal(0m, single);
        Assert.False(UnitParser.TryCombine([], out var empty));
        Assert.Equal(0m, empty);
        Assert.False(UnitParser.TryCombine(null!, out var none));
        Assert.Equal(0m, none);
    }

    [Fact]
    public void CombineRejectsTooManyPartsWithoutThrowing()
    {
        var parts = Enumerable.Range(0, 10_000)
            .Select(_ => (1m, LengthUnit.Centimeter))
            .ToArray();
        Assert.False(UnitParser.TryCombine(parts, out var mm));
        Assert.Equal(0m, mm);
        Assert.False(UnitParser.TryCombine(
            [(1m, LengthUnit.Meter), (1m, LengthUnit.Centimeter), (1m, LengthUnit.Millimeter), (1m, LengthUnit.Millimeter)],
            out _));
    }

    [Fact]
    public void CombineRejectsUndefinedUnits()
    {
        Assert.False(UnitParser.TryCombine([(1m, (LengthUnit)7), (1m, LengthUnit.Millimeter)], out _));
        Assert.False(UnitParser.TryCombine([(1m, LengthUnit.Meter), (1m, (LengthUnit)(-1))], out _));
    }

    [Fact]
    public void CombineOverflowReturnsFalseInsteadOfThrowing()
    {
        Assert.False(UnitParser.TryCombine(
            [(decimal.MaxValue, LengthUnit.Meter), (1m, LengthUnit.Millimeter)], out var mm));
        Assert.Equal(0m, mm);
    }

    [Fact]
    public void CombineAcceptsZeroValuedParts()
    {
        Assert.True(UnitParser.TryCombine([(0m, LengthUnit.Meter), (0m, LengthUnit.Millimeter)], out var mm));
        Assert.Equal(0m, mm);
    }

    [Fact]
    public void ParsingIsCultureIndependent()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "tr-TR", "en-US", "de-DE" })
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
                Assert.True(UnitParser.TryParseUnitWord("milimetre", out var unit, out _));
                Assert.Equal(LengthUnit.Millimeter, unit);
                Assert.True(UnitParser.TryParseUnitWord("santimlik", out var cm, out var suffix));
                Assert.Equal(LengthUnit.Centimeter, cm);
                Assert.True(suffix);
                Assert.False(UnitParser.TryParseUnitWord("MILIM", out _, out _));
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ParsingIsDeterministic()
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.True(UnitParser.TryParseUnitWord("metreye", out var unit, out var suffix));
            Assert.Equal(LengthUnit.Meter, unit);
            Assert.True(suffix);
        }
    }
}
