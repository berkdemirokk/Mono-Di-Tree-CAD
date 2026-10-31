using System.Globalization;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class MeasurementInputTests
{
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void DecimalContractDoesNotDependOnCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            foreach (var input in new[] { "1,500", "1.500", "+1.5", "15e-1", " 1,5\t" })
            {
                Assert.True(MeasurementInput.TryParseMillimeters(input, out var result));
                Assert.Equal(1.5, result);
            }
            Assert.Equal("1.5", MeasurementInput.Format(1.5));
            Assert.True(MeasurementInput.TryParseMillimeters("-.25", out var negative));
            Assert.Equal(-.25, negative);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("1,234.5")]
    [InlineData("1.234,5")]
    [InlineData("1,234,567")]
    [InlineData("1.234.567")]
    [InlineData("1 500")]
    [InlineData("1\u00a0500")]
    [InlineData("1\u202f500")]
    [InlineData("1\t500")]
    [InlineData("1'500")]
    [InlineData("1e 3")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("--2")]
    [InlineData(".")]
    [InlineData(",")]
    [InlineData("+")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("∞")]
    [InlineData("1e9999")]
    [InlineData("1e-9999")]
    [InlineData("-1e-400")]
    [InlineData("１２.５")]
    [InlineData("12 mm")]
    public void AmbiguousInvalidOrUnrepresentableInputIsRejected(string? input)
    {
        Assert.False(MeasurementInput.TryParseMillimeters(input, out var value));
        Assert.Equal(0, value);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.12345678901234567)]
    [InlineData(-99999999.99999999)]
    [InlineData(double.Epsilon)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    [InlineData(1e-250)]
    public void FormatRoundTripsEveryFiniteExampleExactly(double original)
    {
        Assert.True(MeasurementInput.TryParseMillimeters(MeasurementInput.Format(original), out var parsed));
        Assert.Equal(BitConverter.DoubleToInt64Bits(original), BitConverter.DoubleToInt64Bits(parsed));
    }

    [Fact]
    public void NegativeZeroSignIsPreservedByRoundTrip()
    {
        var negativeZero=BitConverter.Int64BitsToDouble(long.MinValue);
        Assert.True(MeasurementInput.TryParseMillimeters(MeasurementInput.Format(negativeZero),out var parsed));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(parsed));
    }

    [Fact]
    public void BoundsRejectRatherThanClampOrRescale()
    {
        Assert.True(MeasurementInput.TryParseMillimeters("-1e8", 1e8, out var boundary));
        Assert.Equal(-1e8, boundary);
        Assert.False(MeasurementInput.TryParseMillimeters("100000000.001", 1e8, out var outside));
        Assert.Equal(0, outside);
        Assert.True(MeasurementInput.TryParseMillimeters("0e-999", 0, out var zero));
        Assert.Equal(0, zero);
        Assert.False(MeasurementInput.TryParseMillimeters("1e-300", 0, out _));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteFormattingAndBoundsAreProgrammingErrors(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementInput.Format(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementInput.TryParseMillimeters("1", value, out _));
    }

    [Fact]
    public void NegativeBoundIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeasurementInput.TryParseMillimeters("1", -1, out _));
    }
}
