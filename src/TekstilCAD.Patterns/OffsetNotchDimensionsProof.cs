using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental check for one caller-selected, indexed feature. The chord
/// between its endpoints is its measured mouth width; the caller must establish
/// feature identity, correspondence and completeness independently. This is
/// not an offset, nested-feature, or production-preservation certificate.
/// </summary>
internal static class OffsetNotchDimensionsProof
{
    // Callers must not mutate inputs while the snapshot is being copied.
    internal static bool TryValidate(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        int start, int end, long sourceDepth, long candidateDepth, long sourceWidth, long candidateWidth,
        out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();
        reason = "Invalid feature, contour size, or width threshold.";
        int n = source.Count;
        if (n < 4 || n > 1024 || candidate.Count != n || start < 0 || start >= n ||
            end < 0 || end >= n || sourceWidth <= 0 || candidateWidth <= 0) return false;
        var a = new GridPoint[n]; var b = new GridPoint[n];
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            a[i] = source[i]; b[i] = candidate[i];
        }
        if (!WidthAtLeast(a[start], a[end], sourceWidth) || !WidthAtLeast(b[start], b[end], candidateWidth))
        { reason = "Selected feature mouth width is below its requested minimum."; return false; }
        if (!OffsetNotchWitness.TryValidate(a, b, start, end, sourceDepth, candidateDepth, out reason, token))
            return false;
        reason = "Selected endpoint chords and depths meet caller thresholds only; correspondence, nested features and production fitness are not certified.";
        return true;
    }

    private static bool WidthAtLeast(GridPoint a, GridPoint b, long width)
    {
        BigInteger x = (BigInteger)b.X - a.X, y = (BigInteger)b.Y - a.Y;
        return x * x + y * y >= (BigInteger)width * width;
    }
}
