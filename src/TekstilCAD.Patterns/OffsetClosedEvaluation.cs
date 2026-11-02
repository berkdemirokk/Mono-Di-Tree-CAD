using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>A diagnostic result, never an authorization or approved PatternPiece.</summary>
internal sealed class OffsetClosedEvaluationResult(bool evaluationPassed,IReadOnlyList<Point2> diagnosticCandidate,
    bool originalPreLocalChecksPassed,bool sourceGridBudgetPassed,bool indexedSweepPassed,
    bool candidateInverseGuardsPassed,double sourcePointBudgetMm,double sourceLengthBudgetMm,double sourceHeightBudgetMm,string reason)
{
    public bool EvaluationPassed {get;}=evaluationPassed;
    public IReadOnlyList<Point2> DiagnosticCandidate {get;}=diagnosticCandidate;
    public bool OriginalPreLocalChecksPassed {get;}=originalPreLocalChecksPassed;
    // Production returned at its local guard; its later checks did not execute.
    public bool OriginalPostLocalChecksPassed => false;
    public bool SourceGridBudgetPassed {get;}=sourceGridBudgetPassed;
    public bool IndexedSweepPassed {get;}=indexedSweepPassed;
    public bool CandidateInverseGuardsPassed {get;}=candidateInverseGuardsPassed;
    public bool FeaturesCertified => false;
    public bool ProductionApproved => false;
    public double SourcePointBudgetMm {get;}=sourcePointBudgetMm;
    public double SourceLengthBudgetMm {get;}=sourceLengthBudgetMm;
    public double SourceHeightBudgetMm {get;}=sourceHeightBudgetMm;
    public int CandidateEdgeBandGrid => 1;
    public int InverseSampleToleranceGrid => 8;
    public string Reason {get;}=reason;
}

/// <summary>
/// Closed experimental evaluation for an input rejected specifically by the
/// existing local-detail guard. It invokes the real production path to retain
/// all earlier checks, then independently evaluates a candidate with explicit
/// source/grid budgets and the existing inverse and area thresholds. It never
/// changes production behavior, approves garment features, or returns a piece.
/// Caller must keep the input stable during snapshot copying.
/// </summary>
internal static class OffsetClosedEvaluation
{
    internal static OffsetClosedEvaluationResult Evaluate(PatternPiece source,double mm,
        double pointMm,double lengthMm,double heightMm,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);token.ThrowIfCancellationRequested();
        bool pre=false,gridOk=false,sweep=false,inverse=false;
        OffsetClosedEvaluationResult Reject(string reason)=>new(false,Array.Empty<Point2>(),pre,gridOk,sweep,inverse,
            pointMm,lengthMm,heightMm,reason);
        if(source.Points is null || source.Points.Count<3 || source.Points.Count>708 || source.Notches is not null ||
            !double.IsFinite(mm) || mm<=0 || mm>100_000 || !double.IsFinite(pointMm) || pointMm<=0 ||
            !double.IsFinite(lengthMm) || lengthMm<=0 || !double.IsFinite(heightMm) || heightMm<=0)
            return Reject("Invalid input, marked notch, or absent physical budgets.");
        var snapshot=source.Clone();token.ThrowIfCancellationRequested();
        try
        {
            _=PatternTools.Offset(snapshot,mm,token);
            return Reject("Existing production offset already succeeded; no rejected-case evaluation applies.");
        }
        catch(OffsetLocalDetailUnverifiedException) { pre=true; }
        catch(ArgumentException ex) { return Reject("Existing production veto: "+ex.Message); }
        catch(ArithmeticException ex) { return Reject("Existing production arithmetic veto: "+ex.Message); }
        token.ThrowIfCancellationRequested();
        var points=snapshot.Points.ToList();
        if(points[0]==points[^1])points.RemoveAt(points.Count-1);
        if(points.Count<3 || points.Count>707)return Reject("Experimental vertex budget exceeded.");
        const double scale=1_000_000;
        var grid=new GridPoint[points.Count];var sourcePath=new Path64();
        for(int i=0;i<points.Count;i++)
        {
            token.ThrowIfCancellationRequested();
            var p=points[i];var x=checked((long)Math.Round(p.X*scale,MidpointRounding.AwayFromZero));
            var y=checked((long)Math.Round(p.Y*scale,MidpointRounding.AwayFromZero));
            grid[i]=new(x,y);sourcePath.Add(new Point64(x,y));
        }
        if(!OffsetBinaryGridPhysicalBudget.TryVerify(points,grid,pointMm,lengthMm,heightMm,out var gridReason,token))
            return Reject("Source/grid budget: "+gridReason);
        gridOk=true;
        var delta=checked((long)Math.Round(mm*scale,MidpointRounding.AwayFromZero));
        if(!OffsetIndexedSweepCertificate.TryCreate(grid,delta,out var candidate,out var candidateReason,token))
            return Reject("Indexed sweep: "+candidateReason);
        sweep=true;
        var candidatePath=new Path64();var candidateMm=new Point2[candidate.Count];
        for(int i=0;i<candidate.Count;i++)
        {
            token.ThrowIfCancellationRequested();var p=candidate[i];candidatePath.Add(new Point64(p.X,p.Y));
            candidateMm[i]=new Point2(p.X/scale,p.Y/scale);
        }
        var recovered=Clipper.InflatePaths(new Paths64 {candidatePath},-delta,JoinType.Miter,EndType.Polygon,2.0);
        token.ThrowIfCancellationRequested();
        if(recovered.Count!=1)return Reject("Candidate inverse changes contour count.");
        // Invoke the exact production local guard without changing its visibility
        // or numerical behavior. Missing/changed method signature fails closed.
        var method=typeof(PatternTools).GetMethod("LocallyMatchesWithCancellation",BindingFlags.NonPublic|BindingFlags.Static);
        if(method is null)return Reject("Production local guard is unavailable.");
        Func<Path64,Path64,double,CancellationToken,bool> matches;
        try { matches=method.CreateDelegate<Func<Path64,Path64,double,CancellationToken,bool>>(); }
        catch(ArgumentException) { return Reject("Production local guard signature changed."); }
        if(!matches(sourcePath,recovered[0],8,token) || !matches(recovered[0],sourcePath,8,token))
            return Reject("Candidate fails the existing 8-grid local comparison.");
        token.ThrowIfCancellationRequested();var difference=Clipper.Xor(new Paths64 {sourcePath},recovered,FillRule.NonZero);
        token.ThrowIfCancellationRequested();var area=difference.Sum(p=>Math.Abs(Clipper.Area(p)));double perimeter=0;
        for(int i=0;i<points.Count;i++)
        {
            token.ThrowIfCancellationRequested();var a=grid[i];var b=grid[(i+1)%grid.Length];
            perimeter+=Geometry.Distance(new Point2(a.X/scale,a.Y/scale),new Point2(b.X/scale,b.Y/scale))*scale;
        }
        if(area>perimeter*8+64)return Reject("Candidate fails the existing area comparison.");
        token.ThrowIfCancellationRequested();inverse=true;
        return new(true,Array.AsReadOnly(candidateMm),pre,gridOk,sweep,inverse,pointMm,lengthMm,heightMm,
            "Experimental evaluation only; complete feature identity and dimensions, machine safety, and production suitability are not certified.");
    }
}
