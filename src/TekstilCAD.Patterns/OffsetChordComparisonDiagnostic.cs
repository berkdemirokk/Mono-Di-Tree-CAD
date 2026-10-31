using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

// Experimental indexed comparison only. A successful result means the selected
// chord witnesses retain the requested fractions, conditional on the caller's
// index correspondence. It does not establish correspondence, completeness,
// physical aperture, or correct offset geometry and must not authorize output.
internal static class OffsetChordComparisonDiagnostic
{
    internal static bool TryCompare(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        long numerator, long denominator, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested();
        reason = "Invalid fraction or incompatible indexed contours";
        if (numerator <= 0 || denominator <= 0 || numerator > denominator || source.Count != candidate.Count)
            return false;
        if (!OffsetChordRecessDiagnostic.TryMeasure(source, out var originals, out reason, token)) return false;
        if (!OffsetChordRecessDiagnostic.TryMeasure(candidate, out var results, out reason, token)) return false;
        var byEndpoints = results.ToDictionary(x => (x.Start, x.End));
        var p2 = (BigInteger)numerator * numerator;
        var q2 = (BigInteger)denominator * denominator;
        foreach (var original in originals)
        {
            token.ThrowIfCancellationRequested();
            if (!byEndpoints.TryGetValue((original.Start, original.End), out var result))
            { reason = "Indexed source witness missing in candidate"; return false; }
            if (result.WidthSquared * q2 < original.WidthSquared * p2 ||
                result.DepthNumeratorSquared * original.DepthDenominator * q2 <
                original.DepthNumeratorSquared * result.DepthDenominator * p2)
            { reason = "Indexed witness width or depth below requested fraction"; return false; }
        }
        reason = $"Conditional indexed chord comparison passed for {originals.Length} witnesses; correspondence and completeness are unproved";
        return true;
    }
}
