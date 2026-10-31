using TekstilCAD.Core;
using System.Numerics;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental, deliberately restrictive ordered comparison. This is a diagnostic,
/// not a topology certificate or authorization to use an offset in production.
/// </summary>
public static class OffsetOrderedDiagnostic
{
    public const int MaximumVertices = 2048;
    public sealed record Result(bool CandidateConstructed, bool OrderedMatch, bool Ambiguous,
        int CandidateVertices, int OutputVertices, double MaximumVertexDistanceMm, string? Reason);

    /// <summary>
    /// Constructs an independent miter candidate and compares output segments in cyclic
    /// order. A candidate edge may correspond to several monotone output segments.
    /// Miter clipping, ambiguous alignments and ill-conditioned corners are rejected.
    /// The requested corridor is a diagnostic input, not a proven rounding bound.
    /// </summary>
    public static Result Compare(IReadOnlyList<Point2> source, double allowance,
        IReadOnlyList<Point2> output, double corridorMm)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        if (!double.IsFinite(allowance) || allowance <= 0 || allowance > 100_000)
            throw new ArgumentOutOfRangeException(nameof(allowance));
        if (!double.IsFinite(corridorMm) || corridorMm <= 0 || corridorMm > 1)
            throw new ArgumentOutOfRangeException(nameof(corridorMm));
        var points = CopyContour(source, nameof(source));
        var target = CopyContour(output, nameof(output));
        var orientation = Math.Sign(Geometry.SignedArea(points));
        var candidate = new List<Point2>(points.Count);
        var cornerRadius = new List<double>(points.Count);
        static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
        static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[(i + points.Count - 1) % points.Count];
            var b = points[i]; var c = points[(i + 1) % points.Count];
            var u = b - a; var v = c - b;
            var ul = Geometry.Distance(a, b); var vl = Geometry.Distance(b, c);
            var cross = Cross(u, v);
            if (Math.Abs(cross) <= 1e-12 * ul * vl)
                return new(false, false, false, points.Count, target.Count, 0, "Nearly parallel adjacent edges are ambiguous.");
            var q = b + new Point2(orientation * u.Y / ul, -orientation * u.X / ul) * allowance;
            var r = b + new Point2(orientation * v.Y / vl, -orientation * v.X / vl) * allowance;
            var p = q + u * (Cross(r - q, v) / cross);
            if (!p.IsFinite || Math.Abs(p.X) > Geometry.MaxCoordinate || Math.Abs(p.Y) > Geometry.MaxCoordinate ||
                Geometry.Distance(p, b) > 2 * allowance)
                return new(false, false, false, points.Count, target.Count, 0, "Corner is outside the supported miter range.");
            candidate.Add(p);
            // A concave join may include the two offset-normal points before
            // the finishing union. Their distance to the ideal miter is local
            // to this one source vertex; do not widen the global corridor.
            // Treat the corridor as caller-supplied numerical/grid uncertainty,
            // not as a proven rounding error or a production certificate.
            var radius = cross * orientation < 0
                ? Math.Max(Geometry.Distance(p, q), Geometry.Distance(p, r)) + corridorMm
                : corridorMm;
            if (!double.IsFinite(radius))
                return new(false, false, true, points.Count, target.Count, 0, "Corner uncertainty is not finite.");
            cornerRadius.Add(radius);
        }
        if (Geometry.HasSelfIntersections(candidate) || Math.Sign(Geometry.SignedArea(candidate)) != orientation)
            return new(false, false, false, candidate.Count, target.Count, 0, "Candidate is not a simple contour with the source orientation.");
        for (var i = 0; i < points.Count; i++)
            if (Dot(points[(i + 1) % points.Count] - points[i], candidate[(i + 1) % points.Count] - candidate[i]) <= 0)
                return new(false, false, false, candidate.Count, target.Count, 0, "Candidate edge order is not preserved.");
        // If neighboring corner neighborhoods overlap, the walk cannot determine
        // whether an entire candidate edge has disappeared inside the corridor.
        // Report this as ambiguous rather than a definite geometric mismatch.
        for (var i = 0; i < candidate.Count; i++)
            if (Geometry.Distance(candidate[i], candidate[(i + 1) % candidate.Count]) <= cornerRadius[i] + cornerRadius[(i + 1) % candidate.Count])
                return new(true, false, true, candidate.Count, target.Count, 0, "Corner corridors overlap; segment correspondence is ambiguous.");
        int matches = 0; double matchedMax = 0;
        int bestProgress = -1; string? bestFailure = null;
        int anchors = 0;
        for (var start = 0; start < target.Count; start++)
        {
            if (Geometry.Distance(candidate[0], target[start]) > cornerRadius[0]) continue;
            if (++anchors > 16)
                return new(true, false, true, candidate.Count, target.Count, 0, "Too many possible starting vertices.");
            foreach (var direction in new[] { 1, -1 })
            {
                if (!MatchSubdivided(candidate, cornerRadius, target, start, direction, corridorMm,
                    out var maximum, out var progress, out var failure))
                {
                    if (progress > bestProgress)
                    { bestProgress = progress; bestFailure = $"start={start}, direction={direction}: {failure}"; }
                    continue;
                }
                matches++; matchedMax = maximum;
                if (matches > 1)
                    return new(true, false, true, candidate.Count, target.Count, matchedMax, "More than one cyclic alignment fits the corridor.");
            }
        }
        return new(true, matches == 1, false, candidate.Count, target.Count, matchedMax,
            matches == 1 ? null : "No cyclic vertex alignment fits the corridor. " +
                (bestFailure ?? "No output vertex lies in the starting corner corridor."));
    }

    // Conservative monotone walk. Each output edge is assigned to one candidate
    // edge; endpoints stay in its convex distance corridor and move forward along
    // it. A target vertex must occur near each candidate corner. This is still not
    // a feature-preservation proof: geometry smaller than the corridor can hide.
    private static bool MatchSubdivided(IReadOnlyList<Point2> candidate, IReadOnlyList<double> cornerRadius, IReadOnlyList<Point2> target,
        int start, int direction, double corridor, out double maximum, out int progress, out string? failure)
    {
        maximum = 0; progress = 0; failure = null; var consumed = 0; var n = target.Count;
        Point2 Vertex(int step) => target[(start + direction * step % n + n) % n];
        (double RadiusDistance, double PreviousLineDistance, double NextLineDistance) CornerDistances(Point2 point, int index)
        {
            var corner = candidate[index];
            static double LineDistance(Point2 p, Point2 x, Point2 y)
            {
                var dx = y.X - x.X; var dy = y.Y - x.Y;
                return Math.Abs((p.X - x.X) * dy - (p.Y - x.Y) * dx) / Math.Sqrt(dx * dx + dy * dy);
            }
            var previous = candidate[(index + candidate.Count - 1) % candidate.Count];
            var next = candidate[(index + 1) % candidate.Count];
            return (Geometry.Distance(point, corner), LineDistance(point, previous, corner), LineDistance(point, corner, next));
        }
        bool NearCorner(Point2 point, int index)
        {
            var d = CornerDistances(point, index);
            return d.RadiusDistance <= cornerRadius[index] && Math.Min(d.PreviousLineDistance, d.NextLineDistance) <= corridor;
        }
        for (var i = 0; i < candidate.Count; i++)
        {
            var a = candidate[i]; var b = candidate[(i + 1) % candidate.Count];
            var dx = b.X - a.X; var dy = b.Y - a.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            // The two corner neighborhoods must not overlap: otherwise the
            // candidate edge could vanish wholly within the tolerance.
            if (length <= cornerRadius[i] + cornerRadius[(i + 1) % candidate.Count])
            { failure = $"candidate edge {i}: corner corridors overlap (length={length:R})."; return false; }
            var lengthSquared = length * length;
            (double Along, double Distance) Project(Point2 p)
            {
                var x = p.X - a.X; var y = p.Y - a.Y;
                var t = (x * dx + y * dy) / lengthSquared;
                var clamped = Math.Clamp(t, 0, 1);
                var ex = x - clamped * dx; var ey = y - clamped * dy;
                return (t, Math.Sqrt(ex * ex + ey * ey));
            }
            // Exact interval coverage of a straight output segment by the union of
            // this edge's finite corridor and its two local corner neighborhoods.
            // The neighborhoods are disk ∩ (previous-line stripe ∪ next-line
            // stripe). This checks the whole chord, not merely its midpoint.
            // All calculations remain experimental floating-point diagnostics.
            bool SegmentCovered(Point2 p, Point2 q)
            {
                var sx = q.X - p.X; var sy = q.Y - p.Y;
                var intervals = new List<(double Lo, double Hi)>();
                static bool LinearBand(ref double lo, ref double hi, double value, double slope, double min, double max)
                {
                    if (slope == 0) return value >= min && value <= max;
                    var t0 = (min - value) / slope; var t1 = (max - value) / slope;
                    if (t0 > t1) (t0, t1) = (t1, t0);
                    lo = Math.Max(lo, t0); hi = Math.Min(hi, t1); return lo <= hi;
                }
                bool Disk(Point2 center, double radius, out double lo, out double hi)
                {
                    lo = 0; hi = 1;
                    var x = p.X - center.X; var y = p.Y - center.Y;
                    var aa = sx * sx + sy * sy; var bb = x * sx + y * sy;
                    var cc = x * x + y * y - radius * radius;
                    if (aa == 0) return cc <= 0;
                    var disc = bb * bb - aa * cc;
                    if (disc < 0) return false;
                    var root = Math.Sqrt(disc);
                    lo = Math.Max(0, (-bb - root) / aa); hi = Math.Min(1, (-bb + root) / aa);
                    return lo <= hi;
                }
                bool Stripe(Point2 x, Point2 y, ref double lo, ref double hi)
                {
                    var ex = y.X - x.X; var ey = y.Y - x.Y; var el = Math.Sqrt(ex * ex + ey * ey);
                    var value = ((p.X - x.X) * ey - (p.Y - x.Y) * ex) / el;
                    var slope = (sx * ey - sy * ex) / el;
                    return LinearBand(ref lo, ref hi, value, slope, -corridor, corridor);
                }
                void AddCorner(int index)
                {
                    var center = candidate[index];
                    if (!Disk(center, cornerRadius[index], out var dlo, out var dhi)) return;
                    var prev = candidate[(index + candidate.Count - 1) % candidate.Count];
                    var nextCorner = candidate[(index + 1) % candidate.Count];
                    var lo = dlo; var hi = dhi;
                    if (Stripe(prev, center, ref lo, ref hi)) intervals.Add((lo, hi));
                    lo = dlo; hi = dhi;
                    if (Stripe(center, nextCorner, ref lo, ref hi)) intervals.Add((lo, hi));
                }
                // Capsule = finite edge rectangle plus endpoint disks.
                var rlo = 0.0; var rhi = 1.0;
                var along = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
                var alongSlope = (sx * dx + sy * dy) / lengthSquared;
                if (LinearBand(ref rlo, ref rhi, along, alongSlope, 0, 1) && Stripe(a, b, ref rlo, ref rhi))
                    intervals.Add((rlo, rhi));
                if (Disk(a, corridor, out var alo, out var ahi)) intervals.Add((alo, ahi));
                if (Disk(b, corridor, out var blo, out var bhi)) intervals.Add((blo, bhi));
                AddCorner(i); AddCorner((i + 1) % candidate.Count);
                var covered = 0.0;
                foreach (var range in intervals.OrderBy(v => v.Lo))
                {
                    // Do not bridge a numerical gap by a tolerance: uncertain
                    // coverage rejects conservatively.
                    if (range.Lo > covered) return false;
                    covered = Math.Max(covered, range.Hi);
                    if (covered >= 1) return true;
                }
                return false;
            }
            var current = Vertex(consumed);
            var cornerDistance = Geometry.Distance(current, a);
            if (!NearCorner(current, i))
            { failure = $"candidate edge {i}, output vertex {(start + direction * consumed % n + n) % n}: start-corner distance={cornerDistance:R}."; return false; }
            var previous = Project(current);
            maximum = Math.Max(maximum, previous.Distance);
            while (!NearCorner(current, (i + 1) % candidate.Count))
            {
                if (consumed >= n)
                { failure = $"candidate edge {i}: output loop exhausted before end corner."; return false; }
                var next = Vertex(consumed + 1); var projected = Project(next);
                var nextIndex = (start + direction * (consumed + 1) % n + n) % n;
                var atStart = NearCorner(next, i);
                var atEnd = NearCorner(next, (i + 1) % candidate.Count);
                // A corridor around a corner must not hide a multi-turn feature.
                // Three successive points all inside the same local corner area
                // may only subdivide a straight, forward edge. This conservative
                // diagnostic rule can reject legitimate intricate join fragments;
                // it is not a proof that shorter features cannot be lost.
                if (consumed + 1 < n)
                {
                    var afterCornerPoint = Vertex(consumed + 2);
                    var endCorner = (i + 1) % candidate.Count;
                    bool ThreeInCorner(int index) => NearCorner(current, index)
                        && NearCorner(next, index) && NearCorner(afterCornerPoint, index);
                    if ((ThreeInCorner(i) || ThreeInCorner(endCorner))
                        && !ExactlyCollinearForward(current, next, afterCornerPoint))
                    { failure = $"candidate edge {i}, output vertex {nextIndex}: unmatched turn inside corner corridor."; return false; }
                }
                // A V of any angle can fit inside the distance corridor. Within a
                // straight candidate edge, accept an added vertex only when its
                // two output edges are exactly collinear and advance in the same
                // direction in the binary values actually supplied by the caller.
                // This exact predicate does not certify the separate corridor,
                // topology or floating-point miter calculations.
                if (!atStart && !atEnd && consumed + 1 < n)
                {
                    var after = Vertex(consumed + 2);
                    if (!ExactlyCollinearForward(current, next, after))
                    { failure = $"candidate edge {i}, output vertex {nextIndex}: unmatched or numerically ambiguous turn within edge corridor."; return false; }
                }
                if (projected.Distance > corridor && !atStart && !atEnd)
                { failure = $"candidate edge {i}, output vertex {nextIndex}: segment distance={projected.Distance:R}, corridor={corridor:R}."; return false; }
                // Do not certify a chord which cuts across a corner neighborhood
                // merely because its endpoints lie on the two supporting lines.
                // This midpoint guard is still diagnostic, not full coverage proof.
                if (previous.Distance > corridor || projected.Distance > corridor)
                {
                    var midpoint = new Point2(current.X + (next.X-current.X)/2, current.Y + (next.Y-current.Y)/2);
                    if (!SegmentCovered(current, next))
                    {
                        var currentIndex = (start + direction * consumed % n + n) % n;
                        var startIndex = i; var endIndex = (i + 1) % candidate.Count;
                        var m0 = CornerDistances(midpoint, startIndex); var m1 = CornerDistances(midpoint, endIndex);
                        var c0 = CornerDistances(current, startIndex); var c1 = CornerDistances(current, endIndex);
                        var n0 = CornerDistances(next, startIndex); var n1 = CornerDistances(next, endIndex);
                        static string Detail((double RadiusDistance, double PreviousLineDistance, double NextLineDistance) d, double radius)
                            => $"radiusDistance={d.RadiusDistance:R}, radius={radius:R}, previousLine={d.PreviousLineDistance:R}, nextLine={d.NextLineDistance:R}";
                        failure = $"candidate edge {i}, output edge {currentIndex}->{nextIndex}: corner segment leaves local corridor; corridor={corridor:R}; " +
                            $"start@corner{i}({Detail(c0, cornerRadius[startIndex])}), start@corner{endIndex}({Detail(c1, cornerRadius[endIndex])}); " +
                            $"mid@corner{i}({Detail(m0, cornerRadius[startIndex])}), mid@corner{endIndex}({Detail(m1, cornerRadius[endIndex])}); " +
                            $"end@corner{i}({Detail(n0, cornerRadius[startIndex])}), end@corner{endIndex}({Detail(n1, cornerRadius[endIndex])}).";
                        return false;
                    }
                }
                if (projected.Along < previous.Along - 1e-12 && !atStart && !atEnd)
                { failure = $"candidate edge {i}, output vertex {nextIndex}: backward projection {previous.Along:R} to {projected.Along:R}."; return false; }
                maximum = Math.Max(maximum, projected.Distance);
                consumed++; progress = consumed; current = next; previous = projected;
            }
        }
        if (consumed != n)
        { failure = $"candidate loop ended with {n - consumed} unconsumed output segments."; return false; }
        return true;
    }

    // Interpret each finite IEEE-754 coordinate as its exact dyadic rational,
    // put all six coordinates on one integer scale, then evaluate cross and dot
    // products without any rounding or cancellation. This establishes only exact
    // collinearity in the stored binary inputs, not an input-error bound.
    private static bool ExactlyCollinearForward(Point2 a, Point2 b, Point2 c)
    {
        static (BigInteger Significand, int Exponent) Parts(double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            var exponent = (int)((bits >> 52) & 0x7ffL);
            var fraction = (ulong)bits & 0x000f_ffff_ffff_ffffUL;
            if (exponent == 0x7ff) throw new ArgumentException("Non-finite coordinate.");
            var significand = new BigInteger(exponent == 0 ? fraction : fraction | (1UL << 52));
            if (bits < 0) significand = -significand;
            return (significand, exponent == 0 ? -1074 : exponent - 1023 - 52);
        }

        var values = new[] { Parts(a.X), Parts(a.Y), Parts(b.X), Parts(b.Y), Parts(c.X), Parts(c.Y) };
        // Zero has no meaningful binary exponent. Including its conventional
        // subnormal exponent would needlessly inflate ordinary integer inputs.
        var nonzero = values.Where(v => !v.Significand.IsZero).ToArray();
        if (nonzero.Length == 0) return false;
        var scale = nonzero.Min(v => v.Exponent);
        BigInteger Scaled(int index) => values[index].Significand.IsZero ? BigInteger.Zero
            : values[index].Significand << (values[index].Exponent - scale);
        var ax = Scaled(0); var ay = Scaled(1); var bx = Scaled(2); var by = Scaled(3);
        var cx = Scaled(4); var cy = Scaled(5);
        var ux = bx - ax; var uy = by - ay; var vx = cx - bx; var vy = cy - by;
        return ux * vy == uy * vx && ux * vx + uy * vy > BigInteger.Zero;
    }

    private static List<Point2> CopyContour(IReadOnlyList<Point2> source, string parameter)
    {
        if (source.Count > MaximumVertices + 1) throw new ArgumentException("Diagnostic contour exceeds the vertex limit.", parameter);
        var copy = source.ToList();
        if (copy.Count > 1 && copy[0] == copy[^1]) copy.RemoveAt(copy.Count - 1);
        if (copy.Count > MaximumVertices) throw new ArgumentException("Diagnostic contour exceeds the vertex limit.", parameter);
        Geometry.ValidateContour(copy);
        return copy;
    }
}
