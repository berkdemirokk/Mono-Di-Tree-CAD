using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental certificate for one freshly generated indexed grid-miter
/// boundary. It does not find or measure garment features, authorize production,
/// approve Clipper output, or establish machine compatibility. A failed proof
/// is inconclusive. The source must remain stable during snapshot copying.
/// </summary>
internal static class OffsetIndexedSweepCertificate
{
    internal static bool TryCreate(IReadOnlyList<GridPoint> source,long delta,
        out IReadOnlyList<GridPoint> candidate,out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);
        token.ThrowIfCancellationRequested(); candidate=Array.Empty<GridPoint>();
        reason="Invalid contour size or allowance."; int n=source.Count;
        // Downstream millimeter topology proof has a 2 million pair budget.
        if(n<3 || n>707 || delta<=1)return false;
        var snapshot=new GridPoint[n];
        for(int i=0;i<n;i++){token.ThrowIfCancellationRequested();snapshot[i]=source[i];}
        if(!OffsetCandidateProof.TryCreate(snapshot,delta,out var generated,out var candidateReason,token))
        {reason="Indexed candidate: "+candidateReason;return false;}
        if(!OffsetIdealBoundaryEvidenceExperiment.TryVerify(snapshot,generated,delta,out var evidenceReason,token))
        {reason="Indexed sweep evidence: "+evidenceReason;return false;}
        const double scale=1_000_000;
        var sourceMm=new Point2[n];var outputMm=new Point2[n];var result=new GridPoint[n];
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();result[i]=generated[i];
            sourceMm[i]=new(snapshot[i].X/scale,snapshot[i].Y/scale);
            outputMm[i]=new(result[i].X/scale,result[i].Y/scale);
        }
        try { Geometry.ValidateContour(sourceMm,token);Geometry.ValidateContour(outputMm,token); }
        catch(ArgumentException ex){reason="Millimeter contour: "+ex.Message;return false;}
        catch(ArithmeticException ex){reason="Millimeter contour: "+ex.Message;return false;}
        token.ThrowIfCancellationRequested();
        candidate=Array.AsReadOnly(result);
        reason="Indexed miter, 1-grid edge band and sufficient sweep/topology checks passed; feature dimensions and production fitness are not certified.";
        return true;
    }
}
