using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Trial composition for a restricted, indexed miter offset. Success proves an
/// exact grid round trip of this construction, source-to-grid isotopy, and the
/// separate experimental per-vertex depth condition. It does not prove global
/// pocket dimensions, the continuous swept region, or equivalence to Clipper.
/// It is not a production acceptance criterion.
/// </summary>
internal static class OffsetIndexedRoundTripExperiment
{
    internal static bool TryConstruct(IReadOnlyList<Point2> source, IReadOnlyList<GridPoint> grid,
        long delta, out GridPoint[] candidate, CancellationToken token = default)
    {
        candidate = [];
        if (!OffsetSourceQuantizationProof.TryValidate(source, grid, token) ||
            !OffsetSourceGridIsotopyExperiment.TryVerify(source, grid, token) ||
            !OffsetIndexedMiterPrototype.TryConstruct(grid, delta, out var forward, token) ||
            !OffsetSourceVertexDepthExperiment.TryVerify(source, forward, token) ||
            !OffsetIndexedMiterPrototype.TryConstructInward(forward, delta, out var recovered, token)) return false;
        for (int i = 0; i < grid.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (grid[i] != recovered[i]) return false;
        }
        candidate = forward;
        return true;
    }
}
