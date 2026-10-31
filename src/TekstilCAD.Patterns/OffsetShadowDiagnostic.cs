using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>Experimental independent construction for investigation only; never authorizes production output.</summary>
public static class OffsetShadowDiagnostic
{
    public sealed record Result(bool CandidateConstructed, bool CandidateSimple, bool EdgeOrderPreserved,
        int SourceVertices, double MaximumMiterRatio, string? Reason);

    /// <summary>Constructs intersections of adjacent offset edge lines. Collinear or nearly parallel adjacent edges are conservatively rejected. No guarantee is made about quantization or the output of another offset engine.</summary>
    public static Result Analyze(IReadOnlyList<Point2> source, double allowance)
    {
        return AnalyzeCore(source, allowance, out _);
    }

    private static Result AnalyzeCore(IReadOnlyList<Point2> source, double allowance, out List<Point2> candidate)
    {
        candidate = [];
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(allowance) || allowance <= 0) throw new ArgumentOutOfRangeException(nameof(allowance));
        Geometry.ValidateContour(source);
        var points = source.ToList();
        if (points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        var orientation = Math.Sign(Geometry.SignedArea(points));
        candidate = new List<Point2>(points.Count);
        var maximumMiter = 0.0;
        static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[(i + points.Count - 1) % points.Count]; var b = points[i]; var c = points[(i + 1) % points.Count];
            var u = b - a; var v = c - b;
            var ul = Geometry.Distance(a, b); var vl = Geometry.Distance(b, c);
            var determinant = Cross(u, v);
            if (Math.Abs(determinant) <= 1e-12 * ul * vl)
                return new(false, false, false, points.Count, maximumMiter, "Nearly parallel adjacent edges.");
            var q = b + new Point2(orientation * u.Y / ul, -orientation * u.X / ul) * allowance;
            var r = b + new Point2(orientation * v.Y / vl, -orientation * v.X / vl) * allowance;
            var point = q + u * (Cross(r - q, v) / determinant);
            if (!point.IsFinite) return new(false, false, false, points.Count, maximumMiter, "Non-finite intersection.");
            if (Math.Abs(point.X) > Geometry.MaxCoordinate || Math.Abs(point.Y) > Geometry.MaxCoordinate)
                return new(false, false, false, points.Count, maximumMiter, "Candidate exceeds the supported coordinate range.");
            maximumMiter = Math.Max(maximumMiter, Geometry.Distance(point, b) / allowance);
            candidate.Add(point);
        }
        var order = true;
        for (var i = 0; i < points.Count; i++)
        {
            var edge = points[(i + 1) % points.Count] - points[i];
            var mapped = candidate[(i + 1) % points.Count] - candidate[i];
            if (edge.X * mapped.X + edge.Y * mapped.Y <= 0) { order = false; break; }
        }
        var simple = !Geometry.HasSelfIntersections(candidate) && Math.Sign(Geometry.SignedArea(candidate)) == orientation;
        return new(true, simple, order, points.Count, maximumMiter, null);
    }
    public sealed record CoverageResult(Result Construction, bool Compared, bool FullyCovered,
        double CandidateUncoveredMm, double OutputUncoveredMm, string? Reason)
    {
        /// <summary>Whether both measured uncovered lengths are within tolerance; null when no comparison was made.</summary>
        public bool? AggregateWithinTolerance { get; init; }
        /// <summary>Whether an unmatched sharp-feature edge vetoed the comparison; null when no comparison was made.</summary>
        public bool? SharpFeatureVeto { get; init; }
    }

    /// <summary>Experimental near-collinear segment coverage comparison, not a production or topology certificate.</summary>
    public static CoverageResult Compare(IReadOnlyList<Point2> source, double allowance, IReadOnlyList<Point2> outline, double toleranceMm)
    {
        ArgumentNullException.ThrowIfNull(outline);
        if (!double.IsFinite(toleranceMm) || toleranceMm <= 0 || toleranceMm > 1) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        var construction = AnalyzeCore(source, allowance, out var candidate);
        if (!construction.CandidateConstructed || !construction.CandidateSimple || !construction.EdgeOrderPreserved)
            return new(construction, false, false, 0, 0, "Candidate is not a simple order-preserving contour.");
        Geometry.ValidateContour(outline);
        var output = outline.ToList();
        if (output[0] == output[^1]) output.RemoveAt(output.Count - 1);
        var (forward, forwardUnsupported, forwardIndex) = Uncovered(candidate, output, toleranceMm);
        var (backward, backwardUnsupported, backwardIndex) = Uncovered(output, candidate, toleranceMm);
        var aggregateWithinTolerance = forward <= toleranceMm && backward <= toleranceMm;
        var sharpFeatureVeto = forwardUnsupported || backwardUnsupported;
        var fullyCovered = !sharpFeatureVeto && aggregateWithinTolerance;
        // Explain the independent sharp-feature veto: the aggregate lengths alone can
        // be below tolerance while an unmatched edge still forces a conservative reject.
        // In particular, this makes false negatives measurable without removing the
        // safeguard for arbitrarily short notches.
        string? reason = fullyCovered ? null
            : forwardUnsupported && backwardUnsupported ? $"Unmatched sharp-feature edge in both contours (candidate index {forwardIndex}, output index {backwardIndex})."
            : forwardUnsupported ? $"Unmatched sharp-feature edge in candidate contour (index {forwardIndex})."
            : backwardUnsupported ? $"Unmatched sharp-feature edge in output contour (index {backwardIndex})."
            : "Aggregate uncovered length exceeds tolerance.";
        return new(construction, true, fullyCovered, forward, backward, reason)
        {
            AggregateWithinTolerance = aggregateWithinTolerance,
            SharpFeatureVeto = sharpFeatureVeto
        };
    }

    // Conservatively retain unmatched sharp features even if their short length fits
    // the aggregate tolerance. This heuristic is diagnostic, not a topology proof.
    private static bool IsSharpFeatureEdge(IReadOnlyList<Point2> path, int index)
    {
        static bool Sharp(Point2 a, Point2 b, Point2 c)
        {
            var u = b - a; var v = c - b;
            var lengths = Math.Sqrt((u.X*u.X + u.Y*u.Y) * (v.X*v.X + v.Y*v.Y));
            return lengths > 0 && (u.X*v.X + u.Y*v.Y) / lengths < 0.5;
        }
        var n = path.Count;
        return Sharp(path[(index+n-1)%n], path[index], path[(index+1)%n])
            || Sharp(path[index], path[(index+1)%n], path[(index+2)%n]);
    }
    private static (double Missing, bool UnsupportedEdge, int? FirstUnsupportedIndex) Uncovered(IReadOnlyList<Point2> from, IReadOnlyList<Point2> to, double tolerance)
    {
        double missing = 0; bool unsupportedEdge = false; int? firstUnsupportedIndex = null;
        for (var i = 0; i < from.Count; i++)
        {
            var a = from[i]; var b = from[(i + 1) % from.Count];
            var d = b - a; var length = Geometry.Distance(a, b); var ux = d.X / length; var uy = d.Y / length;
            var intervals = new List<(double Start, double End)>();
            for (var j = 0; j < to.Count; j++)
            {
                var p = to[j] - a; var q = to[(j + 1) % to.Count] - a;
                if (Math.Abs(p.X * uy - p.Y * ux) > tolerance || Math.Abs(q.X * uy - q.Y * ux) > tolerance) continue;
                var ex = q.X - p.X; var ey = q.Y - p.Y; var edgeLength = Math.Sqrt(ex * ex + ey * ey);
                if (edgeLength == 0 || Math.Abs(ex * ux + ey * uy) < edgeLength * 0.7071067811865476) continue;
                var start = Math.Max(0, Math.Min(p.X * ux + p.Y * uy, q.X * ux + q.Y * uy));
                var end = Math.Min(length, Math.Max(p.X * ux + p.Y * uy, q.X * ux + q.Y * uy));
                if (end > start) intervals.Add((start, end));
            }
            if (intervals.Count == 0 && IsSharpFeatureEdge(from, i)) { unsupportedEdge = true; firstUnsupportedIndex ??= i; }
            double covered = 0, cursor = 0;
            foreach (var interval in intervals.OrderBy(x => x.Start))
            {
                var start = Math.Max(cursor, interval.Start);
                if (interval.End > start) covered += interval.End - start;
                cursor = Math.Max(cursor, interval.End);
            }
            missing += Math.Max(0, length - covered);
        }
        return (missing, unsupportedEdge, firstUnsupportedIndex);
    }

}
