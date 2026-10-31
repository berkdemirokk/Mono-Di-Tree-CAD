using System.Numerics;

namespace TekstilCAD.FileFormats;

// Exact fractions are retained in the result: converting an interval endpoint
// to binary64 without directed rounding would not preserve the enclosure.
internal readonly record struct DxfTrigFraction(BigInteger Numerator, BigInteger Denominator)
{
    internal static DxfTrigFraction Create(BigInteger n, BigInteger d)
    {
        if (d <= 0 || n.GetBitLength() > 16384 || d.GetBitLength() > 16384)
            throw new InvalidDataException("DXF açı hassasiyet iş sınırı aşıldı.");
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(n), d);
        return new(n / gcd, d / gcd);
    }
    internal static DxfTrigFraction FromInt(int value) => new(value, BigInteger.One);
    internal DxfTrigFraction Negate() => new(-Numerator, Denominator);
    internal DxfTrigFraction Add(DxfTrigFraction other) => Create(Numerator * other.Denominator + other.Numerator * Denominator, Denominator * other.Denominator);
    internal DxfTrigFraction Multiply(DxfTrigFraction other) => Create(Numerator * other.Numerator, Denominator * other.Denominator);
    internal DxfTrigFraction Divide(int value) => Create(Numerator, Denominator * value);
    internal int Compare(DxfTrigFraction other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
}

internal readonly record struct DxfTrigBounds(DxfTrigFraction SinLower, DxfTrigFraction SinUpper,
    DxfTrigFraction CosLower, DxfTrigFraction CosUpper);

/// <summary>
/// Exact rational enclosures for sin/cos of a bounded decimal degree token.
/// This is not integrated into DXF import and proves neither geometry nor
/// interval propagation through a placement. The fixed pi enclosure is
/// 3.1415926535897932384626433832795028841971 &lt; pi &lt; ...1972.
/// On the reduced interval [-pi/4,pi/4], alternating Taylor terms decrease;
/// the even and odd partial sums enclose the exact function.
/// </summary>
internal static class DxfTrigInterval
{
    private const int Terms = 14;
    private static readonly BigInteger PiDenominator = BigInteger.Pow(10, 40);
    private static readonly DxfTrigFraction PiLow = DxfTrigFraction.Create(BigInteger.Parse("31415926535897932384626433832795028841971"), PiDenominator);
    private static readonly DxfTrigFraction PiHigh = DxfTrigFraction.Create(BigInteger.Parse("31415926535897932384626433832795028841972"), PiDenominator);

    internal static DxfTrigBounds Compute(string degrees, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var angle = ParseDecimal(degrees, token);
        // Reduce exactly in decimal degrees, then choose the closest quadrant.
        var cycle = 360 * angle.Denominator;
        var positive = ((angle.Numerator % cycle) + cycle) % cycle;
        var quadrant = (int)((positive + 45 * angle.Denominator) / (90 * angle.Denominator));
        var reduced = DxfTrigFraction.Create(positive - quadrant * 90 * angle.Denominator, angle.Denominator);
        var zero = DxfTrigFraction.FromInt(0); var one = DxfTrigFraction.FromInt(1);
        if (reduced.Numerator.IsZero)
            return Rotate(new(zero, zero, one, one), quadrant);
        var lower = reduced.Multiply(reduced.Numerator.Sign < 0 ? PiHigh : PiLow).Divide(180);
        var upper = reduced.Multiply(reduced.Numerator.Sign < 0 ? PiLow : PiHigh).Divide(180);
        var sinLo = SinAt(lower, token).Lower;
        var sinHi = SinAt(upper, token).Upper;
        var absLo = lower.Numerator.Sign < 0 ? lower.Negate() : lower;
        var absHi = upper.Numerator.Sign < 0 ? upper.Negate() : upper;
        var maxAbs = absLo.Compare(absHi) >= 0 ? absLo : absHi;
        var minAbs = lower.Numerator.Sign <= 0 && upper.Numerator.Sign >= 0 ? zero :
            absLo.Compare(absHi) <= 0 ? absLo : absHi;
        var cosLo = CosAt(maxAbs, token).Lower;
        var cosHi = minAbs.Numerator.IsZero ? one : CosAt(minAbs, token).Upper;
        return Rotate(new(sinLo, sinHi, cosLo, cosHi), quadrant);
    }

    private static DxfTrigBounds Rotate(DxfTrigBounds b, int quadrant) => (quadrant % 4) switch
    {
        0 => b,
        1 => new(b.CosLower, b.CosUpper, b.SinUpper.Negate(), b.SinLower.Negate()),
        2 => new(b.SinUpper.Negate(), b.SinLower.Negate(), b.CosUpper.Negate(), b.CosLower.Negate()),
        _ => new(b.CosUpper.Negate(), b.CosLower.Negate(), b.SinLower, b.SinUpper)
    };

    private static (DxfTrigFraction Lower, DxfTrigFraction Upper) SinAt(DxfTrigFraction x, CancellationToken token)
    {
        if (x.Numerator.Sign < 0) { var p = SinAt(x.Negate(), token); return (p.Upper.Negate(), p.Lower.Negate()); }
        return Series(x, true, token);
    }
    private static (DxfTrigFraction Lower, DxfTrigFraction Upper) CosAt(DxfTrigFraction x, CancellationToken token)
        => Series(x, false, token);

    private static (DxfTrigFraction Lower, DxfTrigFraction Upper) Series(DxfTrigFraction x, bool sine, CancellationToken token)
    {
        var square = x.Multiply(x);
        var term = sine ? x : DxfTrigFraction.FromInt(1);
        var sum = DxfTrigFraction.FromInt(0);
        for (int i = 0; i < Terms; i++)
        {
            token.ThrowIfCancellationRequested();
            sum = sum.Add(i % 2 == 0 ? term : term.Negate());
            int a = sine ? 2 * i + 2 : 2 * i + 1;
            term = term.Multiply(square).Divide(a * (a + 1));
        }
        return (sum, sum.Add(term)); // 14 terms ends on the negative term.
    }

    private static DxfTrigFraction ParseDecimal(string? raw, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 256) throw new InvalidDataException("DXF açı metni geçersiz.");
        var text = raw.AsSpan().Trim(); int i = 0, fractional = 0, digits = 0; bool negative = false, dot = false;
        if (text[i] is '+' or '-') negative = text[i++] == '-';
        var coefficient = BigInteger.Zero;
        while (i < text.Length && text[i] is not ('e' or 'E'))
        {
            token.ThrowIfCancellationRequested(); var c = text[i++];
            if (c == '.' && !dot) { dot = true; continue; }
            if (c is < '0' or > '9') throw new InvalidDataException("DXF açı ondalığı geçersiz.");
            coefficient = coefficient * 10 + c - '0'; digits++; if (dot) fractional++;
        }
        if (digits == 0) throw new InvalidDataException("DXF açı rakamı eksik.");
        int exponent = 0; bool exponentNegative = false;
        if (i < text.Length)
        {
            i++; if (i < text.Length && text[i] is '+' or '-') exponentNegative = text[i++] == '-';
            if (i == text.Length) throw new InvalidDataException("DXF açı üssü eksik.");
            while (i < text.Length)
            {
                token.ThrowIfCancellationRequested(); var c = text[i++];
                if (c is < '0' or > '9') throw new InvalidDataException("DXF açı üssü geçersiz.");
                exponent = exponent * 10 + c - '0';
                if (exponent > 128) throw new InvalidDataException("DXF açı üs sınırını aşıyor.");
            }
            if (exponentNegative) exponent = -exponent;
        }
        if (negative) coefficient = -coefficient;
        var power = exponent - fractional;
        if (Math.Abs(power) > 256) throw new InvalidDataException("DXF açı hassasiyet sınırını aşıyor.");
        return power >= 0 ? DxfTrigFraction.Create(coefficient * BigInteger.Pow(10, power), BigInteger.One) :
            DxfTrigFraction.Create(coefficient, BigInteger.Pow(10, -power));
    }
}
