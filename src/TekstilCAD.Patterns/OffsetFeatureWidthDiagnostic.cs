using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Research-only exact lower bound for the distance between two explicitly
/// identified grid edges. This does not discover features or prove that the
/// indexed edges correspond to a user's intended notch after offsetting.
/// </summary>
internal static class OffsetFeatureWidthDiagnostic
{
    internal static bool HasMinimumWidth(IReadOnlyList<GridPoint> contour, int firstEdge,
        int secondEdge, long minimumWidth, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(contour);
        token.ThrowIfCancellationRequested();
        int count = contour.Count;
        if (count < 4 || firstEdge < 0 || firstEdge >= count || secondEdge < 0 ||
            secondEdge >= count || minimumWidth <= 0 || firstEdge == secondEdge ||
            (firstEdge + 1) % count == secondEdge || (secondEdge + 1) % count == firstEdge)
            return false;
        var a = contour[firstEdge]; var b = contour[(firstEdge + 1) % count];
        var c = contour[secondEdge]; var d = contour[(secondEdge + 1) % count];
        var ab = Vector(a, b); var cd = Vector(c, d);
        if (Dot(ab, ab).IsZero || Dot(cd, cd).IsZero) return false;
        var ac = Cross(ab, Vector(a, c)); var ad = Cross(ab, Vector(a, d));
        var ca = Cross(cd, Vector(c, a)); var cb = Cross(cd, Vector(c, b));
        if (ac.Sign * ad.Sign <= 0 && ca.Sign * cb.Sign <= 0 &&
            BigInteger.Max(BigInteger.Min(a.X, b.X), BigInteger.Min(c.X, d.X)) <= BigInteger.Min(BigInteger.Max(a.X, b.X), BigInteger.Max(c.X, d.X)) &&
            BigInteger.Max(BigInteger.Min(a.Y, b.Y), BigInteger.Min(c.Y, d.Y)) <= BigInteger.Min(BigInteger.Max(a.Y, b.Y), BigInteger.Max(c.Y, d.Y))) return false;
        var squared = (BigInteger)minimumWidth * minimumWidth;
        return FarEnough(a, c, d, squared) && FarEnough(b, c, d, squared) &&
               FarEnough(c, a, b, squared) && FarEnough(d, a, b, squared);
    }

    private static bool FarEnough(GridPoint p, GridPoint a, GridPoint b, BigInteger squared)
    {
        var v = Vector(a, b); var w = Vector(a, p); var length = Dot(v, v); var projection = Dot(w, v);
        if (projection <= 0) return Dot(w, w) >= squared;
        if (projection >= length) { var e = Vector(b, p); return Dot(e, e) >= squared; }
        var cross = Cross(v, w);
        return cross * cross >= squared * length;
    }
    private static (BigInteger X, BigInteger Y) Vector(GridPoint a, GridPoint b) =>
        ((BigInteger)b.X - a.X, (BigInteger)b.Y - a.Y);
    private static BigInteger Dot((BigInteger X, BigInteger Y) a, (BigInteger X, BigInteger Y) b) => a.X * b.X + a.Y * b.Y;
    private static BigInteger Cross((BigInteger X, BigInteger Y) a, (BigInteger X, BigInteger Y) b) => a.X * b.Y - a.Y * b.X;
}
