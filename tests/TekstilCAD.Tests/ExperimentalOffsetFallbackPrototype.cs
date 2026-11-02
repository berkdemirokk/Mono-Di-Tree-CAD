using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

// Test-only integration sketch. It calls the actual public Offset first; no
// production caller invokes this type. Caller must keep source stable. A local
// guard failure is returned unchanged unless ALL required evidence exists.
// No complete feature-discovery/acceptance evidence exists yet, so the sketch
// deliberately never promotes an experimental fallback to a PatternPiece.
internal static class ExperimentalOffsetFallbackPrototype
{
    internal sealed record Outcome(PatternPiece? Piece,OffsetLocalDetailUnverifiedException? OriginalFailure,
        bool NumericalChecksPassed,string Reason);

    internal static Outcome TryOffset(PatternPiece source,double mm,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var original=PatternTools.Offset(source,mm,token);
            return new(original,null,false,"Original production offset succeeded.");
        }
        catch(OffsetLocalDetailUnverifiedException failure)
        {
            token.ThrowIfCancellationRequested();
            if(source.Points.Count>707 || source.Points.Count<3)
                return new(null,failure,false,"Experimental vertex budget exceeded.");
            foreach(var p in source.Points)
            {
                token.ThrowIfCancellationRequested();
                if(!ExperimentalExactGridRounding.TryRound(p.X,out var x) || !ExperimentalExactGridRounding.TryRound(p.Y,out var y) ||
                   x!=(long)Math.Round(p.X*1_000_000,MidpointRounding.AwayFromZero) ||
                   y!=(long)Math.Round(p.Y*1_000_000,MidpointRounding.AwayFromZero))
                    return new(null,failure,false,"Exact source grid differs from existing production grid.");
            }
            // This test helper independently executes the existing source/grid,
            // indexed, inverse sample and XOR checks. It invokes Offset again.
            var evaluation=ExperimentalExactGridRoundingTests.Evaluate(source,mm);
            if(!evaluation.Ok)return new(null,failure,false,evaluation.Reason);
            // No automatic complete identification of physical garment features
            // or their allowable dimensional changes is currently available.
            return new(null,failure,true,"Feature presence and dimensional tolerances remain unverified; original failure retained.");
        }
    }
}
