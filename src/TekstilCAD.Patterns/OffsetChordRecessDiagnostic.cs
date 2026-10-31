using System.Collections.Immutable;
using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

// Exact grid-coordinate measurements of a specifically defined family of
// chord-relative recesses. Not a complete discovery of physical features.
internal sealed record OffsetChordRecess(int Start, int End, int VertexCount,
    BigInteger WidthSquared, BigInteger DepthNumeratorSquared, BigInteger DepthDenominator);

internal static class OffsetChordRecessDiagnostic
{
    private const int MaxVertices = 1024;
    private const long MaxWork = 5_000_000;

    internal static bool TryMeasure(IReadOnlyList<GridPoint> source,
        out ImmutableArray<OffsetChordRecess> recesses, out string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        recesses = ImmutableArray<OffsetChordRecess>.Empty;
        reason = "";
        cancellationToken.ThrowIfCancellationRequested();
        int n = source.Count;
        if (n < 3 || n > MaxVertices) { reason = "Vertex limit"; return false; }
        var p = new GridPoint[n];
        var distinct = new HashSet<GridPoint>();
        for (int i = 0; i < n; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            p[i] = source[i];
            if (!distinct.Add(p[i])) { reason = "Duplicate vertex"; return false; }
        }
        long work = 0;
        bool Spend() { cancellationToken.ThrowIfCancellationRequested(); return ++work <= MaxWork; }
        BigInteger area = 0;
        for (int i = 0; i < n; i++) area += (BigInteger)p[i].X * p[(i+1)%n].Y - (BigInteger)p[i].Y * p[(i+1)%n].X;
        if (area.IsZero) { reason = "Zero area"; return false; }
        for (int i = 0; i < n; i++)
        {
            if (Cross(p[(i+n-1)%n], p[i], p[(i+1)%n]).IsZero)
            { reason = "Collinear adjacent edges"; return false; }
            for (int j = i+1; j < n; j++)
            {
                if (!Spend()) { reason = "Work limit"; return false; }
                if (j == i+1 || (i == 0 && j == n-1)) continue;
                if (Intersects(p[i], p[(i+1)%n], p[j], p[(j+1)%n]))
                { reason = "Self intersection or contact"; return false; }
            }
        }
        var found = ImmutableArray.CreateBuilder<OffsetChordRecess>();
        // Every directed proper chain with at least one intermediate vertex.
        // A recess has every intermediate point on the polygon-interior side
        // of its chord, strictly within the endpoint projections, and contains
        // a strictly concave original turn. Overlapping chains are intentional.
        for (int start = 0; start < n; start++)
        for (int steps = 2; steps <= n-2; steps++)
        {
            int end = (start + steps) % n;
            BigInteger dx = (BigInteger)p[end].X-p[start].X, dy = (BigInteger)p[end].Y-p[start].Y;
            BigInteger width2 = dx*dx+dy*dy, maxCross = 0;
            bool valid = true, concave = false;
            for (int k = 1; k < steps; k++)
            {
                if (!Spend()) { reason = "Work limit"; return false; }
                int v = (start+k)%n;
                BigInteger x = (BigInteger)p[v].X-p[start].X, y = (BigInteger)p[v].Y-p[start].Y;
                BigInteger cross = (dx*y-dy*x)*area.Sign, projection = dx*x+dy*y;
                if (cross.Sign <= 0 || projection.Sign <= 0 || projection >= width2) { valid = false; break; }
                if (cross > maxCross) maxCross = cross;
                if (Cross(p[(v+n-1)%n],p[v],p[(v+1)%n]).Sign != area.Sign) concave = true;
            }
            if (valid && concave) found.Add(new(start,end,steps+1,width2,maxCross*maxCross,width2));
        }
        recesses = found.ToImmutable();
        reason = "Exact chord dimensions only; no physical feature completeness or offset proof";
        return true;
    }
    private static BigInteger Cross(GridPoint a, GridPoint b, GridPoint c) =>
        ((BigInteger)b.X-a.X)*((BigInteger)c.Y-a.Y)-((BigInteger)b.Y-a.Y)*((BigInteger)c.X-a.X);
    private static bool On(GridPoint a, GridPoint b, GridPoint p) =>
        p.X >= Math.Min(a.X,b.X) && p.X <= Math.Max(a.X,b.X) && p.Y >= Math.Min(a.Y,b.Y) && p.Y <= Math.Max(a.Y,b.Y);
    private static bool Intersects(GridPoint a, GridPoint b, GridPoint c, GridPoint d)
    {
        var x=Cross(a,b,c); var y=Cross(a,b,d); var z=Cross(c,d,a); var w=Cross(c,d,b);
        return (x.IsZero && On(a,b,c)) || (y.IsZero && On(a,b,d)) ||
            (z.IsZero && On(c,d,a)) || (w.IsZero && On(c,d,b)) || (x.Sign*y.Sign<0 && z.Sign*w.Sign<0);
    }
}
