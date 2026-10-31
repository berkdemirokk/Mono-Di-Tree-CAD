using System.Globalization;
using System.Numerics;

namespace TekstilCAD.FileFormats;

/// <summary>A 2D source-unit placement with a counterclockwise angle in quarter turns.</summary>
internal sealed record DxfPrecisionTransform(string BaseX, string BaseY, string InsertionX, string InsertionY,
    string ScaleX, string ScaleY, int QuarterTurns);

/// <summary>
/// Exact reference check for one point under decimal scale, quarter-turn rotation,
/// translation and unit conversion. This is not an expander or a check of contour
/// topology, layer semantics, general angles, or collective import performance.
/// Transforms are supplied from innermost to outermost. All error is measured
/// against the provided actual binary64 point in millimeters, component by component.
/// </summary>
internal static class DxfInsertPrecision
{
    private const int MaxTransforms = 64;
    private const int MaxTokenLength = 256;
    private const int MaxExponent = 256;
    private const int MaxBits = 16384;
    private readonly record struct Fraction(BigInteger N, BigInteger D);

    /// <summary>
    /// Interprets the original decimal degrees exactly. Returns false when the
    /// angle is not a multiple of 90; invalid or out-of-budget tokens throw.
    /// The returned counterclockwise quarter-turn count is normalized to 0..3.
    /// </summary>
    public static bool TryGetQuarterTurns(string degrees, out int quarterTurns,
        CancellationToken cancellationToken = default)
    {
        quarterTurns = 0;
        cancellationToken.ThrowIfCancellationRequested();
        var angle = Parse(degrees, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var wholeTurns = BigInteger.DivRem(angle.N, angle.D * 90, out var remainder);
        if (!remainder.IsZero) return false;
        var normalized = wholeTurns % 4;
        if (normalized.Sign < 0) normalized += 4;
        quarterTurns = (int)normalized;
        return true;
    }

    public static bool IsWithinTolerance(string pointX, string pointY, IReadOnlyList<DxfPrecisionTransform> transforms,
        string millimetersPerUnit, double actualX, double actualY, double toleranceMm, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(transforms);
        if (transforms.Count > MaxTransforms) throw new InvalidDataException("DXF dönüşüm derinlik sınırı aşıldı.");
        if (!double.IsFinite(actualX) || !double.IsFinite(actualY)) throw new ArgumentOutOfRangeException(nameof(actualX));
        if (!double.IsFinite(toleranceMm) || toleranceMm < 0) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        var x = Parse(pointX, cancellationToken); var y = Parse(pointY, cancellationToken);
        foreach (var transform in transforms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (transform is null) throw new InvalidDataException("DXF dönüşümü boş olamaz.");
            var bx = Parse(transform.BaseX, cancellationToken); var by = Parse(transform.BaseY, cancellationToken);
            var ix = Parse(transform.InsertionX, cancellationToken); var iy = Parse(transform.InsertionY, cancellationToken);
            var sx = Parse(transform.ScaleX, cancellationToken); var sy = Parse(transform.ScaleY, cancellationToken);
            if (sx.N.IsZero || sy.N.IsZero) throw new InvalidDataException("DXF ölçeği sıfır olamaz.");
            var dx = Mul(Sub(x, bx), sx); var dy = Mul(Sub(y, by), sy);
            int turn = ((transform.QuarterTurns % 4) + 4) % 4;
            (var rx, var ry) = turn switch
            {
                0 => (dx, dy), 1 => (Neg(dy), dx), 2 => (Neg(dx), Neg(dy)), _ => (dy, Neg(dx))
            };
            x = Add(ix, rx); y = Add(iy, ry);
        }
        var mm = Parse(millimetersPerUnit, cancellationToken);
        if (mm.N.Sign <= 0) throw new InvalidDataException("DXF birim ölçeği pozitif olmalıdır.");
        x = Mul(x, mm); y = Mul(y, mm);
        var tolerance = FromDouble(toleranceMm);
        cancellationToken.ThrowIfCancellationRequested();
        return Within(x, FromDouble(actualX), tolerance) && Within(y, FromDouble(actualY), tolerance);
    }

    private static bool Within(Fraction expected, Fraction actual, Fraction tolerance)
    {
        var difference = Sub(expected, actual);
        return BigInteger.Abs(difference.N) * tolerance.D <= tolerance.N * difference.D;
    }
    private static Fraction Neg(Fraction a) => new(-a.N, a.D);
    private static Fraction Add(Fraction a, Fraction b) => Make(a.N * b.D + b.N * a.D, a.D * b.D);
    private static Fraction Sub(Fraction a, Fraction b) => Add(a, Neg(b));
    private static Fraction Mul(Fraction a, Fraction b) => Make(a.N * b.N, a.D * b.D);
    private static Fraction Make(BigInteger n, BigInteger d)
    {
        if (d.Sign <= 0) throw new InvalidDataException("DXF sayısal paydası geçersiz.");
        if (n.GetBitLength() > MaxBits || d.GetBitLength() > MaxBits) throw new InvalidDataException("DXF hassasiyet iş sınırı aşıldı.");
        if (n.IsZero) return new(BigInteger.Zero, BigInteger.One);
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(n), d);
        return new(n / gcd, d / gcd);
    }
    private static Fraction Parse(string? raw, CancellationToken token)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxTokenLength) throw new InvalidDataException("DXF ondalık metni geçersiz.");
        var span = raw.AsSpan().Trim();
        if (span.IsEmpty) throw new InvalidDataException("DXF ondalık metni boş.");
        int i = 0, digits = 0, fractional = 0; bool negative = false, dot = false;
        if (span[i] is '+' or '-') negative = span[i++] == '-';
        var coefficient = BigInteger.Zero;
        while (i < span.Length && span[i] is not ('e' or 'E'))
        {
            token.ThrowIfCancellationRequested();
            var ch = span[i++];
            if (ch == '.' && !dot) { dot = true; continue; }
            if (ch is < '0' or > '9') throw new InvalidDataException("DXF ondalık biçimi geçersiz.");
            coefficient = coefficient * 10 + (ch - '0'); digits++; if (dot) fractional++;
        }
        if (digits == 0) throw new InvalidDataException("DXF ondalık rakamı eksik.");
        int exponent = 0; bool exponentNegative = false;
        if (i < span.Length)
        {
            i++; if (i < span.Length && span[i] is '+' or '-') exponentNegative = span[i++] == '-';
            if (i == span.Length) throw new InvalidDataException("DXF ondalık üssü eksik.");
            while (i < span.Length)
            {
                token.ThrowIfCancellationRequested();
                var ch = span[i++]; if (ch is < '0' or > '9') throw new InvalidDataException("DXF üssü geçersiz.");
                exponent = exponent * 10 + ch - '0';
                if (exponent > MaxExponent) throw new InvalidDataException("DXF ondalık üs sınırı aşıldı.");
            }
        }
        if (negative) coefficient = -coefficient;
        if (exponentNegative) exponent = -exponent;
        var power = exponent - fractional;
        token.ThrowIfCancellationRequested();
        return power >= 0 ? Make(coefficient * BigInteger.Pow(10, power), BigInteger.One)
            : Make(coefficient, BigInteger.Pow(10, -power));
    }
    private static Fraction FromDouble(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        if (exponent == 0 && fraction == 0) return new(BigInteger.Zero, BigInteger.One);
        var significand = new BigInteger(exponent == 0 ? fraction : fraction | (1L << 52));
        int power = exponent == 0 ? -1074 : exponent - 1023 - 52;
        if (negative) significand = -significand;
        return power >= 0 ? Make(significand << power, BigInteger.One) : Make(significand, BigInteger.One << -power);
    }
}
