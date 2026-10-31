using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental combination of an indexed grid candidate, exact grid/binary64
/// topology comparison, and the normal millimeter contour checks. Success is
/// not production approval: notch dimensions and feature correspondence are
/// not established by these checks.
/// </summary>
internal static class OffsetCandidateAssessment
{
    private const int MaximumVertices = 1024;
    // Conservative combined ceiling for the quadratic scans in both helpers
    // and the two floating-point contour validations. This is a work bound,
    // not a latency guarantee. Each helper also enforces its own limits.
    private const long MaximumCombinedPairBudget = 4_000_000;
    private const double GridPerMillimeter = 1_000_000.0;

    /// <remarks>The caller must not mutate <paramref name="source"/> while its snapshot is copied.</remarks>
    internal static bool TryAssess(IReadOnlyList<GridPoint> source, long delta,
        out IReadOnlyList<Point2> candidateMm, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        candidateMm = Array.Empty<Point2>();
        reason = "";
        token.ThrowIfCancellationRequested();
        int count = source.Count;
        if (count < 3 || count > MaximumVertices || delta <= 0 ||
            8L * count * count > MaximumCombinedPairBudget)
        {
            reason = "Invalid input or combined contour work budget exceeded.";
            return false;
        }

        // Every subsequent operation receives the same private snapshot, so a
        // caller changing its collection cannot alter an already-copied point.
        var snapshot = new GridPoint[count];
        var sourceMm = new Point2[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            snapshot[i] = source[i];
            sourceMm[i] = new Point2(snapshot[i].X / GridPerMillimeter, snapshot[i].Y / GridPerMillimeter);
        }

        if (!OffsetCandidateProof.TryCreate(snapshot, delta, out var candidateGrid, out var candidateReason, token))
        {
            reason = "Grid candidate rejected: " + candidateReason;
            return false;
        }
        if (candidateGrid.Count != count)
        {
            reason = "Grid candidate does not preserve the vertex count.";
            return false;
        }

        var output = new Point2[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            var point = candidateGrid[i];
            output[i] = new Point2(point.X / GridPerMillimeter, point.Y / GridPerMillimeter);
        }
        if (!OffsetMillimeterProof.TryValidate(snapshot, candidateGrid, sourceMm, output, out var mmReason, token))
        {
            reason = "Millimeter topology rejected: " + mmReason;
            return false;
        }

        try
        {
            Geometry.ValidateContour(sourceMm, token);
            Geometry.ValidateContour(output, token);
        }
        catch (ArgumentException ex)
        {
            reason = "Millimeter contour rejected: " + ex.Message;
            return false;
        }
        catch (ArithmeticException ex)
        {
            reason = "Millimeter contour rejected: " + ex.Message;
            return false;
        }

        token.ThrowIfCancellationRequested();
        candidateMm = Array.AsReadOnly(output);
        reason = "Experimental assessment only; feature dimensions and production fitness are not certified.";
        return true;
    }
}
