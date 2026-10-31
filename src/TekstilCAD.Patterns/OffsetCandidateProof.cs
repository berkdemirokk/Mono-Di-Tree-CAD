using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;
using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental, conservative construction of an indexed, grid-rounded ideal
/// miter candidate. Acceptance is not an authorization for production: in
/// particular, preserving turn signs does not prove preservation of feature
/// dimensions, and the independent interval routine itself needs validation.
/// This helper neither inspects nor approves Clipper output.
/// </summary>
internal static class OffsetCandidateProof
{
    private const int MaximumVertices = 1024;
    private const long MaximumPairChecks = 2_000_000;
    // Match the existing inverse-offset guard, expressed in integer grid units.
    private const int ReverseErrorGrid = 8;
    private const double GridPerMillimeter = 1_000_000;

    internal static bool TryCreate(IReadOnlyList<GridPoint> source, long delta,
        out IReadOnlyList<GridPoint> candidate, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        candidate = Array.Empty<GridPoint>();
        reason = "";
        token.ThrowIfCancellationRequested();
        var n = source.Count;
        if (n < 3 || n > MaximumVertices || delta <= 0)
        { reason = "Invalid input or vertex budget exceeded."; return false; }
        // Copy caller data so subsequent mutation cannot change this operation.
        var original = new GridPoint[n];
        for (int i = 0; i < n; i++) { token.ThrowIfCancellationRequested(); original[i] = source[i]; }
        if (!RoundTripsThroughMillimeters(original, token))
        { reason = "Source cannot be represented uniquely on the millimeter grid."; return false; }
        long checks = 0;
        if (!Simple(original, ref checks, token))
        { reason = "Source contour is non-simple or exceeds the work budget."; return false; }
        var area = Area(original, token);
        if (area.IsZero) { reason = "Source contour has zero area."; return false; }
        int orientation = area.Sign;
        var points = new GridPoint[n];
        var miterLimit = (BigInteger)2 * delta * Scale;
        var miterLimitSquared = miterLimit * miterLimit;
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            var a = original[(i + n - 1) % n]; var b = original[i]; var c = original[(i + 1) % n];
            if (!TryBound(a, b, c, delta, orientation, out var box) ||
                !TryRound(box.X, out var x) || !TryRound(box.Y, out var y))
            { reason = $"Miter {i} is ambiguous or outside the grid."; return false; }
            // Prove every point in the enclosure stays within the 2*delta miter
            // limit. This conservative box bound can reject valid corners.
            var bx = (BigInteger)b.X * Scale; var by = (BigInteger)b.Y * Scale;
            var dx = BigInteger.Max(BigInteger.Abs(box.X.Lower - bx), BigInteger.Abs(box.X.Upper - bx));
            var dy = BigInteger.Max(BigInteger.Abs(box.Y.Lower - by), BigInteger.Abs(box.Y.Upper - by));
            if (dx * dx + dy * dy > miterLimitSquared)
            { reason = $"Miter {i} exceeds or cannot prove the limit."; return false; }
            points[i] = new(x, y);
        }
        if (!Simple(points, ref checks, token) || Area(points, token).Sign != orientation)
        { reason = "Rounded candidate is non-simple or changes orientation."; return false; }
        if (!RoundTripsThroughMillimeters(points, token))
        { reason = "Candidate cannot be represented uniquely on the millimeter grid."; return false; }
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            var s0=original[(i+n-1)%n]; var s1=original[i]; var s2=original[(i+1)%n];
            var d0=points[(i+n-1)%n]; var d1=points[i]; var d2=points[(i+1)%n];
            var sourceCross=Cross(s0,s1,s2);
            // If each source corner could move by 8 grid units on either axis,
            // each incident vector changes by at most 16 on each axis. Bound
            // every linear and quadratic term of the cross-product error.
            var ux=(BigInteger)s1.X-s0.X; var uy=(BigInteger)s1.Y-s0.Y;
            var vx=(BigInteger)s2.X-s1.X; var vy=(BigInteger)s2.Y-s1.Y;
            var componentChange=(BigInteger)(2*ReverseErrorGrid);
            var errorBound=componentChange*(BigInteger.Abs(ux)+BigInteger.Abs(uy)+BigInteger.Abs(vx)+BigInteger.Abs(vy))
                + 2*componentChange*componentChange;
            if (sourceCross.IsZero || BigInteger.Abs(sourceCross)<=errorBound)
            { reason = $"Source turn {i} is ambiguous within the local error budget."; return false; }
            if (sourceCross.Sign != Cross(d0,d1,d2).Sign ||
                DotDirection(s1,s2,d1,d2) <= 0)
            { reason = $"Turn or edge direction changes at {i}."; return false; }
            // Reverse the ideal miter from the rounded candidate. The entire
            // interval box, not its midpoint, must fit inside an 8-grid
            // Euclidean disk around the source vertex. This does not reproduce
            // or replace the production Clipper reverse-offset check.
            if (!TryBound(d0,d1,d2,delta,-orientation,out var reverse) ||
                !InsideErrorBudget(reverse.X,reverse.Y,s1.X,s1.Y))
            { reason = $"Reverse miter {i} exceeds or cannot prove the local error budget."; return false; }
        }
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            token.ThrowIfCancellationRequested();
            if (++checks > MaximumPairChecks || Intersects(original[i],original[(i+1)%n],points[j],points[(j+1)%n]))
            { reason = "Source and candidate touch, cross, or exceed the work budget."; return false; }
        }
        if (!ContainsStrict(points, original[0], token))
        { reason = "The candidate does not strictly contain the source."; return false; }
        candidate = Array.AsReadOnly(points);
        reason = "Experimental candidate only; feature dimensions and production fitness are not certified.";
        return true;
    }

    private static bool InsideErrorBudget(Interval x, Interval y, long sourceX, long sourceY)
    {
        var originX=(BigInteger)sourceX*Scale;
        var originY=(BigInteger)sourceY*Scale;
        var budget=(BigInteger)ReverseErrorGrid*Scale;
        var dx=BigInteger.Max(BigInteger.Abs(x.Lower-originX),BigInteger.Abs(x.Upper-originX));
        var dy=BigInteger.Max(BigInteger.Abs(y.Lower-originY),BigInteger.Abs(y.Upper-originY));
        return dx*dx+dy*dy<=budget*budget;
    }

    private static bool RoundTripsThroughMillimeters(IReadOnlyList<GridPoint> points, CancellationToken token)
    {
        var distinct = new HashSet<Point2>();
        foreach (var point in points)
        {
            token.ThrowIfCancellationRequested();
            var mapped = new Point2(point.X/GridPerMillimeter, point.Y/GridPerMillimeter);
            if (!mapped.IsFinite || Math.Abs(mapped.X)>Geometry.MaxCoordinate || Math.Abs(mapped.Y)>Geometry.MaxCoordinate ||
                !distinct.Add(mapped)) return false;
            var x=Math.Round(mapped.X*GridPerMillimeter,MidpointRounding.AwayFromZero);
            var y=Math.Round(mapped.Y*GridPerMillimeter,MidpointRounding.AwayFromZero);
            if (x!=point.X || y!=point.Y) return false;
        }
        return true;
    }

    private static bool TryRound(Interval interval, out long result)
    {
        var lower=RoundAway(interval.Lower); var upper=RoundAway(interval.Upper);
        if (lower != upper || lower < long.MinValue || lower > long.MaxValue) { result=0; return false; }
        result=(long)lower; return true;
    }
    private static BigInteger RoundAway(BigInteger scaled)
    {
        var magnitude=(BigInteger.Abs(scaled)+Scale/2)/Scale;
        return scaled.Sign < 0 ? -magnitude : magnitude;
    }
    private static BigInteger Cross(GridPoint a, GridPoint b, GridPoint c)
        => ((BigInteger)b.X-a.X)*((BigInteger)c.Y-a.Y)-((BigInteger)b.Y-a.Y)*((BigInteger)c.X-a.X);
    private static BigInteger DotDirection(GridPoint a, GridPoint b, GridPoint c, GridPoint d)
        => ((BigInteger)b.X-a.X)*((BigInteger)d.X-c.X)+((BigInteger)b.Y-a.Y)*((BigInteger)d.Y-c.Y);
    private static BigInteger Area(IReadOnlyList<GridPoint> p, CancellationToken token)
    {
        BigInteger sum=0; for(int i=0;i<p.Count;i++) { token.ThrowIfCancellationRequested(); sum+=(BigInteger)p[i].X*p[(i+1)%p.Count].Y-(BigInteger)p[i].Y*p[(i+1)%p.Count].X; }
        return sum;
    }
    private static bool Simple(IReadOnlyList<GridPoint> p, ref long checks, CancellationToken token)
    {
        var distinct=new HashSet<GridPoint>();
        for(int i=0;i<p.Count;i++) { token.ThrowIfCancellationRequested(); if (!distinct.Add(p[i])) return false; }
        for(int i=0;i<p.Count;i++)
        for(int j=i+1;j<p.Count;j++)
        {
            token.ThrowIfCancellationRequested();
            if (++checks>MaximumPairChecks) return false;
            if (j==i+1 || (i==0 && j==p.Count-1))
            {
                // Adjacent collinear edges must continue forward, not overlap.
                var shared = j==i+1 ? p[j] : p[0];
                var before = j==i+1 ? p[i] : p[p.Count-1];
                var after = j==i+1 ? p[(j+1)%p.Count] : p[1];
                if (Cross(before,shared,after).IsZero && DotDirection(before,shared,shared,after)<=0) return false;
                continue;
            }
            if (Intersects(p[i],p[(i+1)%p.Count],p[j],p[(j+1)%p.Count])) return false;
        }
        return true;
    }
    private static bool Intersects(GridPoint a, GridPoint b, GridPoint c, GridPoint d)
    {
        var abC=Cross(a,b,c).Sign; var abD=Cross(a,b,d).Sign; var cdA=Cross(c,d,a).Sign; var cdB=Cross(c,d,b).Sign;
        if (abC==0 && OnSegment(a,b,c) || abD==0 && OnSegment(a,b,d) || cdA==0 && OnSegment(c,d,a) || cdB==0 && OnSegment(c,d,b)) return true;
        return abC*abD<0 && cdA*cdB<0;
    }
    private static bool OnSegment(GridPoint a,GridPoint b,GridPoint p)
        => p.X>=Math.Min(a.X,b.X) && p.X<=Math.Max(a.X,b.X) && p.Y>=Math.Min(a.Y,b.Y) && p.Y<=Math.Max(a.Y,b.Y);
    private static bool ContainsStrict(IReadOnlyList<GridPoint> polygon,GridPoint p,CancellationToken token)
    {
        int winding=0;
        for(int i=0;i<polygon.Count;i++)
        {
            token.ThrowIfCancellationRequested();
            var a=polygon[i]; var b=polygon[(i+1)%polygon.Count]; var cross=Cross(a,b,p);
            if (cross.IsZero && OnSegment(a,b,p)) return false;
            if(a.Y<=p.Y && b.Y>p.Y && cross.Sign>0) winding++;
            else if(a.Y>p.Y && b.Y<=p.Y && cross.Sign<0) winding--;
        }
        return winding!=0;
    }
}
