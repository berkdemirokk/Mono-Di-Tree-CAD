using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental bound between an ideal indexed miter path and the linear
/// interpolation from grid source to a supplied grid candidate. Success does
/// not establish simplicity, edge correspondence, Clipper equivalence, feature
/// dimensions or production suitability. A failed interval is inconclusive.
/// </summary>
internal static class OffsetIdealPathBoundExperiment
{
    // Each coordinate of each corresponding moving vertex is at most one grid
    // unit apart. The matching points of any indexed edge have the same bound,
    // by convexity. This L-infinity bound is not a Euclidean one-grid bound.
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        long delta, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();
        int n = source.Count;
        reason = "Invalid contour size or allowance.";
        if (n < 3 || n > 1024 || candidate.Count != n || delta <= 0) return false;
        var s = new GridPoint[n]; var c = new GridPoint[n];
        for (int i = 0; i < n; i++) { token.ThrowIfCancellationRequested(); s[i] = source[i]; c[i] = candidate[i]; }
        BigInteger area = 0;
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            var next = s[(i + 1) % n];
            area += (BigInteger)s[i].X * next.Y - (BigInteger)s[i].Y * next.X;
        }
        if (area.IsZero) { reason = "Source area is zero."; return false; }
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            if (!TryBound(s[(i + n - 1) % n], s[i], s[(i + 1) % n], delta, area.Sign, out var box) ||
                !WithinOne(box.X, c[i].X) || !WithinOne(box.Y, c[i].Y))
            { reason = $"Ideal miter {i} cannot be bounded within one grid unit per axis."; return false; }
        }
        // With fixed source edge normals, each ideal miter is affine in offset
        // distance: ideal(t) = source + t*(ideal(1)-source). Thus its
        // difference from source + t*(candidate-source) is t times the
        // end-point difference, bounded for every real t in [0,1].
        reason = "One grid unit per axis for the indexed ideal path; topology and production fitness are not certified.";
        return true;
    }

    private static bool WithinOne(Interval interval, long value) =>
        interval.Lower >= ((BigInteger)value - 1) * Scale && interval.Upper <= ((BigInteger)value + 1) * Scale;
}
