using System.Numerics;

namespace TekstilCAD.FileFormats;

/// <summary>Source-unit 2D placement. Transforms are applied in the supplied inner-to-outer order.</summary>
internal sealed record DxfGeneralPrecisionTransform(string BaseX, string BaseY, string InsertionX, string InsertionY,
    string ScaleX, string ScaleY, string Degrees);

/// <summary>
/// Conservative exact-rational interval check for nested 2D placements. Every
/// interval is closed and all decimal operands are interpreted exactly. Sine and
/// cosine enclosures come from <see cref="DxfTrigInterval"/> and its documented
/// fixed pi bounds. The mm unit parameter is a decimal token, not binary64.
/// Each intermediate bound is rounded outward onto a 2^-256 dyadic grid to
/// bound denominator growth; this widens enclosures and cannot justify a false
/// acceptance. Both output coordinates must fit wholly within their binary64 actual value
/// plus/minus the (binary64, per-coordinate) tolerance. False includes both an
/// actual mismatch and an interval too wide to prove acceptance. This is not a
/// contour/topology check. The strict opt-in importer uses a bounded instance
/// of this verifier; its geometric and layer checks are separate.
/// </summary>
internal static class DxfGeneralInsertPrecision
{
    private const int MaxTransforms = 64;
    private const int MaxTokenLength = 256;
    private const int MaxExponentMagnitude = 256;
    private const int MaxDecimalPower = 511;
    private const int FractionBits = 256;
    private static readonly BigInteger GridDenominator = BigInteger.One << FractionBits;
    private readonly record struct Interval(DxfTrigFraction Lower, DxfTrigFraction Upper);
    private readonly record struct PreparedFrame(Interval BaseX, Interval BaseY, Interval InsertionX,
        Interval InsertionY, Interval ScaleX, Interval ScaleY, Interval Sin, Interval Cos);

    /// <summary>
    /// One import's bounded cache of immutable trigonometric enclosures. It is
    /// intentionally owned by the import operation, never by global state.
    /// Callers must not use one instance concurrently from multiple threads.
    /// Angles are keyed by exact decimal value modulo 360, not binary64.
    /// </summary>
    internal sealed class AngleCache
    {
        private readonly Dictionary<DxfTrigFraction, DxfTrigBounds> bounds = [];
        private readonly int maximumUniqueAngles;
        internal int Count => bounds.Count;
        internal int Computations { get; private set; }

        internal AngleCache(int maximumUniqueAngles)
        {
            if (maximumUniqueAngles is < 1 or > 4096)
                throw new ArgumentOutOfRangeException(nameof(maximumUniqueAngles));
            this.maximumUniqueAngles = maximumUniqueAngles;
        }

        internal DxfTrigBounds Get(string degrees, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Validate with the exact same token, exponent and power limits as
            // DxfTrigInterval.Compute before lookup. A cache hit must never
            // admit a token that the uncached calculation would reject.
            var angle = Parse(degrees, cancellationToken, maxExponentMagnitude: 128, maxDecimalPower: 256);
            var cycle = angle.Denominator * 360;
            var remainder = ((angle.Numerator % cycle) + cycle) % cycle;
            var key = DxfTrigFraction.Create(remainder, angle.Denominator);
            if (bounds.TryGetValue(key, out var cached)) return cached;
            if (bounds.Count >= maximumUniqueAngles)
                throw new InvalidDataException("DXF farklı açı doğrulama sınırını aşıyor.");
            var calculated = DxfTrigInterval.Compute(degrees, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            bounds.Add(key, calculated);
            Computations++;
            return calculated;
        }
    }

    public static bool IsWithinTolerance(string pointX, string pointY,
        IReadOnlyList<DxfGeneralPrecisionTransform> transforms, string millimetersPerUnit,
        double actualX, double actualY, double toleranceMm, CancellationToken cancellationToken = default)
    {
        return Prepare(transforms, millimetersPerUnit, cancellationToken)
            .IsWithinTolerance(pointX, pointY, actualX, actualY, toleranceMm, cancellationToken);
    }

    /// <summary>
    /// Prepares one immutable, bounded inner-to-outer transform chain. The unit
    /// value remains an exact decimal token; no binary64 conversion is implied.
    /// Reuse this object only for points sharing the exact chain and unit value.
    /// </summary>
    public static PreparedContext Prepare(IReadOnlyList<DxfGeneralPrecisionTransform> transforms,
        string millimetersPerUnit, CancellationToken cancellationToken = default)
        => new(transforms, millimetersPerUnit, new AngleCache(MaxTransforms), cancellationToken);

    /// <summary>Prepares a chain using a cache owned by the current import.</summary>
    internal static PreparedContext Prepare(IReadOnlyList<DxfGeneralPrecisionTransform> transforms,
        string millimetersPerUnit, AngleCache angleCache, CancellationToken cancellationToken = default)
        => new(transforms, millimetersPerUnit, angleCache, cancellationToken);

    internal sealed class PreparedContext
    {
        private readonly PreparedFrame[] frames;
        private readonly Interval units;

        internal PreparedContext(IReadOnlyList<DxfGeneralPrecisionTransform> transforms,
            string millimetersPerUnit, AngleCache angleCache, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(transforms);
            ArgumentNullException.ThrowIfNull(angleCache);
            if (transforms.Count > MaxTransforms) throw new InvalidDataException("DXF dönüşüm derinlik sınırı aşıldı.");
            var prepared = new PreparedFrame[transforms.Count];
            for (int i = 0; i < prepared.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var transform = transforms[i] ?? throw new InvalidDataException("DXF dönüşümü boş olamaz.");
                var bx = Singleton(Parse(transform.BaseX, cancellationToken));
                var by = Singleton(Parse(transform.BaseY, cancellationToken));
                var ix = Singleton(Parse(transform.InsertionX, cancellationToken));
                var iy = Singleton(Parse(transform.InsertionY, cancellationToken));
                var sx = Parse(transform.ScaleX, cancellationToken);
                var sy = Parse(transform.ScaleY, cancellationToken);
                if (sx.Numerator.IsZero || sy.Numerator.IsZero) throw new InvalidDataException("DXF ölçeği sıfır olamaz.");
                if (transform.Degrees is null) throw new InvalidDataException("DXF açısı eksik.");
                var trig = angleCache.Get(transform.Degrees, cancellationToken);
                prepared[i] = new(bx, by, ix, iy, Singleton(sx), Singleton(sy),
                    Create(trig.SinLower, trig.SinUpper), Create(trig.CosLower, trig.CosUpper));
            }
            var unit = Parse(millimetersPerUnit, cancellationToken);
            if (unit.Numerator.Sign <= 0) throw new InvalidDataException("DXF birim ölçeği pozitif olmalıdır.");
            cancellationToken.ThrowIfCancellationRequested();
            frames = prepared;
            units = Singleton(unit);
        }

        /// <summary>Checks one source point; cancellation is observed within each frame.</summary>
        public bool IsWithinTolerance(string pointX, string pointY, double actualX, double actualY,
            double toleranceMm, CancellationToken cancellationToken = default)
        {
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(actualX)) throw new ArgumentOutOfRangeException(nameof(actualX));
        if (!double.IsFinite(actualY)) throw new ArgumentOutOfRangeException(nameof(actualY));
        if (!double.IsFinite(toleranceMm) || toleranceMm < 0) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        var x = Singleton(Parse(pointX, cancellationToken));
        var y = Singleton(Parse(pointY, cancellationToken));
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dx = Multiply(Subtract(x, frame.BaseX), frame.ScaleX);
            var dy = Multiply(Subtract(y, frame.BaseY), frame.ScaleY);
            // Both components must be calculated from the old x and y.
            var nextX = Add(frame.InsertionX, Subtract(Multiply(dx, frame.Cos), Multiply(dy, frame.Sin)));
            var nextY = Add(frame.InsertionY, Add(Multiply(dx, frame.Sin), Multiply(dy, frame.Cos)));
            x = nextX; y = nextY;
        }
        x = Multiply(x, units);
        y = Multiply(y, units);
        var tolerance = FromDouble(toleranceMm);
        cancellationToken.ThrowIfCancellationRequested();
        return IsContained(x, FromDouble(actualX), tolerance) && IsContained(y, FromDouble(actualY), tolerance);
        }
    }

    private static bool IsContained(Interval value, DxfTrigFraction actual, DxfTrigFraction tolerance)
    {
        var allowedLower = actual.Add(tolerance.Negate());
        var allowedUpper = actual.Add(tolerance);
        return value.Lower.Compare(allowedLower) >= 0 && value.Upper.Compare(allowedUpper) <= 0;
    }
    private static Interval Create(DxfTrigFraction lower, DxfTrigFraction upper)
    {
        if (lower.Compare(upper) > 0) throw new InvalidDataException("DXF sayısal aralığı geçersiz.");
        // BigInteger division truncates toward zero. Correct negative lower
        // and positive upper quotients to implement mathematical floor/ceiling.
        // Scaling is exact and the resulting bounds enclose the originals.
        return new(RoundToGrid(lower, false), RoundToGrid(upper, true));
    }
    private static DxfTrigFraction RoundToGrid(DxfTrigFraction value, bool upwards)
    {
        var scaled = value.Numerator * GridDenominator;
        var quotient = BigInteger.DivRem(scaled, value.Denominator, out var remainder);
        if (!remainder.IsZero)
        {
            if (upwards && scaled.Sign > 0) quotient += BigInteger.One;
            else if (!upwards && scaled.Sign < 0) quotient -= BigInteger.One;
        }
        return DxfTrigFraction.Create(quotient, GridDenominator);
    }
    private static Interval Singleton(DxfTrigFraction value) => new(value, value);
    private static Interval Add(Interval a, Interval b) => Create(a.Lower.Add(b.Lower), a.Upper.Add(b.Upper));
    private static Interval Subtract(Interval a, Interval b) => Create(a.Lower.Add(b.Upper.Negate()), a.Upper.Add(b.Lower.Negate()));
    private static Interval Multiply(Interval a, Interval b)
    {
        var aa = a.Lower.Multiply(b.Lower); var ab = a.Lower.Multiply(b.Upper);
        var ba = a.Upper.Multiply(b.Lower); var bb = a.Upper.Multiply(b.Upper);
        var min = aa; var max = aa;
        foreach (var p in new[] { ab, ba, bb })
        {
            if (p.Compare(min) < 0) min = p;
            if (p.Compare(max) > 0) max = p;
        }
        return Create(min, max);
    }
    private static DxfTrigFraction Parse(string? raw, CancellationToken token,
        int maxExponentMagnitude = MaxExponentMagnitude, int maxDecimalPower = MaxDecimalPower)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxTokenLength) throw new InvalidDataException("DXF ondalık metni geçersiz.");
        var text = raw.AsSpan().Trim();
        if (text.IsEmpty) throw new InvalidDataException("DXF ondalık metni boş.");
        int i = 0, digits = 0, fractional = 0; bool negative = false, dot = false;
        if (text[i] is '+' or '-') negative = text[i++] == '-';
        var coefficient = BigInteger.Zero;
        while (i < text.Length && text[i] is not ('e' or 'E'))
        {
            token.ThrowIfCancellationRequested();
            var c = text[i++];
            if (c == '.' && !dot) { dot = true; continue; }
            if (c is < '0' or > '9') throw new InvalidDataException("DXF ondalık biçimi geçersiz.");
            coefficient = coefficient * 10 + (c - '0'); digits++; if (dot) fractional++;
        }
        if (digits == 0) throw new InvalidDataException("DXF ondalık rakamı eksik.");
        int exponent = 0; bool exponentNegative = false;
        if (i < text.Length)
        {
            i++; if (i < text.Length && text[i] is '+' or '-') exponentNegative = text[i++] == '-';
            if (i == text.Length) throw new InvalidDataException("DXF ondalık üssü eksik.");
            while (i < text.Length)
            {
                token.ThrowIfCancellationRequested(); var c = text[i++];
                if (c is < '0' or > '9') throw new InvalidDataException("DXF ondalık üssü geçersiz.");
                exponent = exponent * 10 + (c - '0');
                if (exponent > maxExponentMagnitude) throw new InvalidDataException("DXF ondalık üs sınırı aşıldı.");
            }
        }
        if (exponentNegative) exponent = -exponent;
        if (negative) coefficient = -coefficient;
        var power = exponent - fractional;
        if (Math.Abs(power) > maxDecimalPower) throw new InvalidDataException("DXF ondalık hassasiyet sınırı aşıldı.");
        token.ThrowIfCancellationRequested();
        return power >= 0 ? DxfTrigFraction.Create(coefficient * BigInteger.Pow(10, power), BigInteger.One)
            : DxfTrigFraction.Create(coefficient, BigInteger.Pow(10, -power));
    }
    private static DxfTrigFraction FromDouble(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        if (exponent == 0 && fraction == 0) return DxfTrigFraction.FromInt(0);
        var significand = new BigInteger(exponent == 0 ? fraction : fraction | (1L << 52));
        int power = exponent == 0 ? -1074 : exponent - 1023 - 52;
        if (negative) significand = -significand;
        return power >= 0 ? DxfTrigFraction.Create(significand << power, BigInteger.One)
            : DxfTrigFraction.Create(significand, BigInteger.One << -power);
    }
}
