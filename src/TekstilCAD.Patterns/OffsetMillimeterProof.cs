using System.Numerics;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental comparison of grid contours with their binary64 millimeter
/// representation. All orientation, intersection and containment predicates
/// operate on exact integers, including the exact dyadic values of the doubles.
/// This does not certify offset distance, notch dimensions, downstream floating
/// point algorithms, or fitness for production.
/// </summary>
internal static class OffsetMillimeterProof
{
    private const int MaximumVertices = 1024;
    private const long MaximumPairChecks = 2_000_000;
    private const double GridPerMillimeter = 1_000_000.0;
    private readonly record struct ExactPoint(BigInteger X, BigInteger Y);
    private readonly record struct Dyadic(BigInteger Significand, int Exponent);

    internal static bool TryValidate(IReadOnlyList<GridPoint> sourceGrid,
        IReadOnlyList<GridPoint> candidateGrid, IReadOnlyList<Point2> sourceMm,
        IReadOnlyList<Point2> candidateMm, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sourceGrid);
        ArgumentNullException.ThrowIfNull(candidateGrid);
        ArgumentNullException.ThrowIfNull(sourceMm);
        ArgumentNullException.ThrowIfNull(candidateMm);
        reason = "";
        token.ThrowIfCancellationRequested();
        int n = sourceGrid.Count;
        if (n < 3 || n > MaximumVertices || candidateGrid.Count != n || sourceMm.Count != n || candidateMm.Count != n)
            return Reject(out reason, "Contour lengths differ or exceed the vertex budget.");
        // Two simple-contour scans and one cross-contour scan, each in grid
        // and exact-binary64 space. Check before allocating or enumerating.
        long pairs = 4L * n * (n - 1) / 2 + 2L * n * n;
        if (pairs > MaximumPairChecks)
            return Reject(out reason, "Exact topology comparison exceeds the pair budget.");

        var source = new ExactPoint[n]; var candidate = new ExactPoint[n];
        var sourceValues = new Point2[n]; var candidateValues = new Point2[n];
        int minExponent = 0;
        bool hasNonzero = false;
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            var s = sourceGrid[i]; var c = candidateGrid[i];
            var sm = sourceMm[i]; var cm = candidateMm[i];
            if (!Matches(s, sm) || !Matches(c, cm))
                return Reject(out reason, "A point does not round-trip to its original grid coordinate.");
            source[i] = new(s.X, s.Y); candidate[i] = new(c.X, c.Y);
            sourceValues[i] = sm; candidateValues[i] = cm;
            UpdateExponent(sm.X); UpdateExponent(sm.Y); UpdateExponent(cm.X); UpdateExponent(cm.Y);
        }

        var sourceExact = new ExactPoint[n]; var candidateExact = new ExactPoint[n];
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            sourceExact[i] = new(ToInteger(sourceValues[i].X), ToInteger(sourceValues[i].Y));
            candidateExact[i] = new(ToInteger(candidateValues[i].X), ToInteger(candidateValues[i].Y));
        }

        long checks = 0;
        if (!ValidatePair(source, candidate) || !ValidatePair(sourceExact, candidateExact))
            return Reject(out reason, "Contours are degenerate, intersect, or are not strictly nested.");
        int sourceOrientation = Area(source, token).Sign, candidateOrientation = Area(candidate, token).Sign;
        if (sourceOrientation != Area(sourceExact, token).Sign || candidateOrientation != Area(candidateExact, token).Sign)
            return Reject(out reason, "Binary64 conversion changes contour orientation.");
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            int prev = (i + n - 1) % n, next = (i + 1) % n;
            if (Cross(source[prev], source[i], source[next]).Sign != Cross(sourceExact[prev], sourceExact[i], sourceExact[next]).Sign ||
                Cross(candidate[prev], candidate[i], candidate[next]).Sign != Cross(candidateExact[prev], candidateExact[i], candidateExact[next]).Sign)
                return Reject(out reason, "Binary64 conversion changes a local turn.");
        }
        reason = "Exact grid and binary64 topology agree; feature dimensions and production fitness are not certified.";
        return true;

        void UpdateExponent(double value)
        {
            var d = Decode(value);
            if (!d.Significand.IsZero && (!hasNonzero || d.Exponent < minExponent)) { minExponent = d.Exponent; hasNonzero = true; }
        }
        BigInteger ToInteger(double value)
        {
            var d = Decode(value);
            return d.Significand.IsZero ? BigInteger.Zero : d.Significand << (d.Exponent - minExponent);
        }
        bool ValidatePair(ExactPoint[] inner, ExactPoint[] outer)
        {
            if (!Simple(inner, ref checks, token) || !Simple(outer, ref checks, token)) return false;
            int a = Area(inner, token).Sign, b = Area(outer, token).Sign;
            if (a == 0 || b == 0 || a != b) return false;
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                token.ThrowIfCancellationRequested();
                if (++checks > MaximumPairChecks || Intersects(inner[i], inner[(i + 1) % n], outer[j], outer[(j + 1) % n])) return false;
            }
            return ContainsStrict(outer, inner[0], token);
        }
    }

    private static bool Reject(out string reason, string message) { reason = message; return false; }

    private static bool Matches(GridPoint point, Point2 value)
    {
        if (!value.IsFinite || Math.Abs(value.X) > Geometry.MaxCoordinate || Math.Abs(value.Y) > Geometry.MaxCoordinate) return false;
        if (value.X != point.X / GridPerMillimeter || value.Y != point.Y / GridPerMillimeter) return false;
        return Math.Round(value.X * GridPerMillimeter, MidpointRounding.AwayFromZero) == point.X &&
               Math.Round(value.Y * GridPerMillimeter, MidpointRounding.AwayFromZero) == point.Y;
    }

    private static Dyadic Decode(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponentBits = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        if (exponentBits == 0x7ff) throw new ArgumentOutOfRangeException(nameof(value));
        if (exponentBits == 0 && fraction == 0) return new(BigInteger.Zero, 0);
        var significand = new BigInteger(exponentBits == 0 ? fraction : fraction | (1L << 52));
        if (bits < 0) significand = -significand;
        return new(significand, exponentBits == 0 ? -1074 : exponentBits - 1023 - 52);
    }

    private static BigInteger Cross(ExactPoint a, ExactPoint b, ExactPoint c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static BigInteger Dot(ExactPoint a, ExactPoint b, ExactPoint c)
        => (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y);
    private static BigInteger Area(IReadOnlyList<ExactPoint> p, CancellationToken token)
    {
        BigInteger area = 0;
        for (int i = 0; i < p.Count; i++) { token.ThrowIfCancellationRequested(); var q = p[(i + 1) % p.Count]; area += p[i].X * q.Y - p[i].Y * q.X; }
        return area;
    }
    private static bool Simple(IReadOnlyList<ExactPoint> p, ref long checks, CancellationToken token)
    {
        var unique = new HashSet<ExactPoint>();
        for (int i = 0; i < p.Count; i++) { token.ThrowIfCancellationRequested(); if (!unique.Add(p[i])) return false; }
        for (int i = 0; i < p.Count; i++)
        for (int j = i + 1; j < p.Count; j++)
        {
            token.ThrowIfCancellationRequested();
            if (++checks > MaximumPairChecks) return false;
            if (j == i + 1 || (i == 0 && j == p.Count - 1))
            {
                var before = j == i + 1 ? p[i] : p[p.Count - 1];
                var shared = j == i + 1 ? p[j] : p[0];
                var after = j == i + 1 ? p[(j + 1) % p.Count] : p[1];
                if (Cross(before, shared, after).IsZero && Dot(before, shared, after) <= 0) return false;
                continue;
            }
            if (Intersects(p[i], p[(i + 1) % p.Count], p[j], p[(j + 1) % p.Count])) return false;
        }
        return true;
    }
    private static bool Intersects(ExactPoint a, ExactPoint b, ExactPoint c, ExactPoint d)
    {
        if (BigInteger.Max(a.X, b.X) < BigInteger.Min(c.X, d.X) || BigInteger.Max(c.X, d.X) < BigInteger.Min(a.X, b.X) ||
            BigInteger.Max(a.Y, b.Y) < BigInteger.Min(c.Y, d.Y) || BigInteger.Max(c.Y, d.Y) < BigInteger.Min(a.Y, b.Y)) return false;
        int abC = Cross(a, b, c).Sign, abD = Cross(a, b, d).Sign, cdA = Cross(c, d, a).Sign, cdB = Cross(c, d, b).Sign;
        if ((abC == 0 && OnSegment(a, b, c)) || (abD == 0 && OnSegment(a, b, d)) ||
            (cdA == 0 && OnSegment(c, d, a)) || (cdB == 0 && OnSegment(c, d, b))) return true;
        return abC * abD < 0 && cdA * cdB < 0;
    }
    private static bool OnSegment(ExactPoint a, ExactPoint b, ExactPoint p)
        => p.X >= BigInteger.Min(a.X, b.X) && p.X <= BigInteger.Max(a.X, b.X) &&
           p.Y >= BigInteger.Min(a.Y, b.Y) && p.Y <= BigInteger.Max(a.Y, b.Y);
    private static bool ContainsStrict(IReadOnlyList<ExactPoint> polygon, ExactPoint p, CancellationToken token)
    {
        int winding = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; int cross = Cross(a, b, p).Sign;
            if (cross == 0 && OnSegment(a, b, p)) return false;
            if (a.Y <= p.Y && b.Y > p.Y && cross > 0) winding++;
            else if (a.Y > p.Y && b.Y <= p.Y && cross < 0) winding--;
        }
        return winding != 0;
    }
}
