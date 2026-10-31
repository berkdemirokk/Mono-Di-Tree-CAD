using System.Globalization;
using System.Numerics;

namespace TekstilCAD.FileFormats;

/// <summary>
/// Bounded comparison between the exact decimal difference in a DXF file and
/// the difference obtained after parsing and subtracting as binary64.
/// This checks subtraction only; scaling, rotation and later composition need
/// their own error accounting before geometry may be accepted.
/// </summary>
internal static class DxfExactDecimal
{
    private const int MaximumTokenLength = 256;
    private const int MaximumExponentMagnitude = 1024;
    private readonly record struct Rational(BigInteger Numerator, BigInteger Denominator);

    /// <summary>Exact equality of bounded decimal tokens, without binary64 rounding.</summary>
    internal static bool AreEqual(string left,string right,CancellationToken cancellationToken=default)
    {
        var a=Parse(left,cancellationToken);var b=Parse(right,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return a.Numerator*b.Denominator==b.Numerator*a.Denominator;
    }

    /// <param name="absoluteTolerance">Maximum absolute error in the source coordinate unit.</param>
    public static bool IsDifferenceWithinTolerance(string left, string right, double absoluteTolerance,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(absoluteTolerance) || absoluteTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(absoluteTolerance));
        var a = Parse(left, cancellationToken);
        var b = Parse(right, cancellationToken);
        if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var da) || !double.IsFinite(da) ||
            !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var db) || !double.IsFinite(db))
            throw new InvalidDataException("DXF ondalık değeri binary64 sınırını aşıyor.");
        var difference = da - db;
        if (!double.IsFinite(difference)) return false;
        var actual = FromDouble(difference);
        var tolerance = FromDouble(absoluteTolerance);
        cancellationToken.ThrowIfCancellationRequested();

        // |(a - b) - actual| <= tolerance, evaluated without rounding.
        var exactNumerator = a.Numerator * b.Denominator - b.Numerator * a.Denominator;
        var exactDenominator = a.Denominator * b.Denominator;
        var error = BigInteger.Abs(exactNumerator * actual.Denominator - actual.Numerator * exactDenominator);
        var errorDenominator = exactDenominator * actual.Denominator;
        cancellationToken.ThrowIfCancellationRequested();
        return error * tolerance.Denominator <= tolerance.Numerator * errorDenominator;
    }

    private static Rational Parse(string? raw, CancellationToken cancellationToken)
    {
        if (raw is null || raw.Length == 0 || raw.Length > MaximumTokenLength)
            throw new InvalidDataException("DXF ondalık metni boş veya çok uzun.");
        var text = raw.AsSpan().Trim();
        if (text.IsEmpty) throw new InvalidDataException("DXF ondalık metni boş.");
        int i = 0, digits = 0, fractional = 0;
        bool negative = false, dot = false;
        if (text[i] is '+' or '-') { negative = text[i] == '-'; i++; }
        var coefficient = BigInteger.Zero;
        while (i < text.Length && text[i] is not ('e' or 'E'))
        {
            cancellationToken.ThrowIfCancellationRequested();
            char ch = text[i++];
            if (ch == '.' && !dot) { dot = true; continue; }
            if (ch < '0' || ch > '9') throw new InvalidDataException("DXF ondalık biçimi geçersiz.");
            coefficient = coefficient * 10 + (ch - '0');
            digits++;
            if (dot) fractional++;
        }
        if (digits == 0) throw new InvalidDataException("DXF ondalık rakamı eksik.");
        int exponent = 0;
        if (i < text.Length)
        {
            i++;
            bool exponentNegative = false;
            if (i < text.Length && text[i] is '+' or '-') { exponentNegative = text[i] == '-'; i++; }
            if (i == text.Length) throw new InvalidDataException("DXF ondalık üssü eksik.");
            while (i < text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ch = text[i++];
                if (ch < '0' || ch > '9') throw new InvalidDataException("DXF ondalık üssü geçersiz.");
                exponent = exponent * 10 + (ch - '0');
                if (exponent > MaximumExponentMagnitude) throw new InvalidDataException("DXF ondalık üssü sınırı aşıyor.");
            }
            if (exponentNegative) exponent = -exponent;
        }
        if (negative) coefficient = -coefficient;
        var power = exponent - fractional;
        cancellationToken.ThrowIfCancellationRequested();
        return power >= 0
            ? new(coefficient * BigInteger.Pow(10, power), BigInteger.One)
            : new(coefficient, BigInteger.Pow(10, -power));
    }

    private static Rational FromDouble(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        if (exponent == 0 && fraction == 0) return new(BigInteger.Zero, BigInteger.One);
        var significand = new BigInteger(exponent == 0 ? fraction : fraction | (1L << 52));
        int power = exponent == 0 ? -1074 : exponent - 1023 - 52;
        if (negative) significand = -significand;
        return power >= 0 ? new(significand << power, BigInteger.One) : new(significand, BigInteger.One << -power);
    }
}
