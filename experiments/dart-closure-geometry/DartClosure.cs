using System.Numerics;
using TekstilCAD.Core;

namespace DartClosureGeometry;

// An isolated mathematical construction, not a completed sewing line.
public sealed record DartAnalysis(Point2 RotatedCenter, Point2 RotatedLegA, Point2 FixedLegB,
    double LegALength, double LegBLength, double EndpointGap, double OutsideLength,
    double OpenChainLength, double OriginalChordLength, double NumericalThreshold,
    bool WithinNumericalThreshold, bool ExactBinarySquaredLegsEqual)
{
    public string SewingStatus => "unresolved";
    public bool ProductionAllowed => false;
    public bool ExportAllowed => false;
}

public static class DartClosure
{
    public static DartAnalysis Analyze(Point2 center, Point2 legA, Point2 tip, Point2 legB, Point2 side, double numericalThreshold)
    {
        foreach (var p in new[] { center, legA, tip, legB, side })
            if (!p.IsFinite || Math.Abs(p.X) > 1_000_000 || Math.Abs(p.Y) > 1_000_000)
                throw new ArgumentOutOfRangeException(nameof(center), "Coordinates must be finite and within ±1,000,000 mm (numerical bound).");
        if (!double.IsFinite(numericalThreshold) || numericalThreshold < 0 || numericalThreshold > 1000)
            throw new ArgumentOutOfRangeException(nameof(numericalThreshold));
        double a = Geometry.Distance(tip, legA), b = Geometry.Distance(tip, legB);
        double outerA = Geometry.Distance(center, legA), outerB = Geometry.Distance(legB, side);
        if (a < 1e-6 || b < 1e-6 || outerA < 1e-6 || outerB < 1e-6)
            throw new ArgumentException("Legs and outside segments must be at least 1e-6 mm (numerical bound).");
        double ux = (legA.X - tip.X) / a, uy = (legA.Y - tip.Y) / a;
        double vx = (legB.X - tip.X) / b, vy = (legB.Y - tip.Y) / b;
        double c = ux * vx + uy * vy, s = ux * vy - uy * vx;
        double norm = Math.Sqrt(c * c + s * s); c /= norm; s /= norm;
        Point2 Rotate(Point2 p) => new(tip.X + c * (p.X - tip.X) - s * (p.Y - tip.Y),
                                        tip.Y + s * (p.X - tip.X) + c * (p.Y - tip.Y));
        Point2 mapped = Rotate(legA);
        double gap = Geometry.Distance(mapped, legB), outside = outerA + outerB;
        return new(Rotate(center), mapped, legB, a, b, gap, outside, outside + a + b,
            Geometry.Distance(center, side), numericalThreshold, gap <= numericalThreshold,
            SquaredDistance(tip, legA) == SquaredDistance(tip, legB));
    }

    // Every finite binary64 value is an integer multiple of 2^-1074. This checks
    // represented coordinates exactly, without claiming exact source measurements.
    private static BigInteger Scaled(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7ff);
        ulong fraction = (ulong)bits & 0x000f_ffff_ffff_ffffUL;
        var integer = new BigInteger(exponent == 0 ? fraction : fraction | (1UL << 52));
        integer <<= exponent == 0 ? 0 : exponent - 1;
        return bits < 0 ? -integer : integer;
    }
    private static BigInteger SquaredDistance(Point2 a, Point2 b)
    {
        BigInteger x = Scaled(a.X) - Scaled(b.X), y = Scaled(a.Y) - Scaled(b.Y);
        return x * x + y * y;
    }
}
