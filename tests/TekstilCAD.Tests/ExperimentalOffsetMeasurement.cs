using System.Numerics;
using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

// Test-only, read-only diagnostic. Source and feature inputs must be stable
// during copying. A passing line-distance band is not a Hausdorff, full feature,
// manufacturing or production certificate. It creates no PatternPiece.
internal static class ExperimentalOffsetMeasurement
{
    internal sealed record Feature(int Start,int Witness,int End,double WidthToleranceMm,double DepthToleranceMm);
    internal sealed record Result(bool Evaluated,IReadOnlyList<Point2> Preview,int SelectedFeatures,int FailedFeatures,
        double ObservedApproximateMaxLineDeviationMm,string Reason)
    {
        internal bool NeedsReview=>true;
        internal bool ProductionApproved=>false;
    }
    internal static Result Evaluate(PatternPiece input,double allowance,double pointBudget,double lengthBudget,double heightBudget,
        double lineToleranceMm,IReadOnlyList<Feature> selected,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(input);ArgumentNullException.ThrowIfNull(selected);
        Result Fail(string why)=>new(false,Array.Empty<Point2>(),0,0,double.NaN,why);
        if(input.Points is null || input.Points.Count<3 || input.Points.Count>707 || input.Notches is not null || selected.Count>64 ||
           !double.IsFinite(allowance) || allowance<=0 || allowance>100_000 || !double.IsFinite(lineToleranceMm) || lineToleranceMm<0)
            return Fail("Invalid input or missing explicit physical line tolerance.");
        var source=input.Clone();var features=selected.ToArray();token.ThrowIfCancellationRequested();
        var delta=checked((long)Math.Round(allowance*1_000_000,MidpointRounding.AwayFromZero));
        if(delta<=0 || lineToleranceMm>=delta/1_000_000d)return Fail("Invalid line tolerance relative to quantized allowance.");
        var assembly=typeof(PatternTools).Assembly;
        var method=assembly.GetType("TekstilCAD.Patterns.OffsetClosedEvaluation",true)!.GetMethod("Evaluate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object evaluation;
        try{evaluation=method.Invoke(null,[source,allowance,pointBudget,lengthBudget,heightBudget,token])!;}
        catch(TargetInvocationException ex) when(ex.InnerException is OperationCanceledException){throw ex.InnerException;}
        var type=evaluation.GetType();bool passed=(bool)type.GetProperty("EvaluationPassed")!.GetValue(evaluation)!;
        if(!passed)return Fail((string)type.GetProperty("Reason")!.GetValue(evaluation)!);
        var original=source.Points.ToArray();var candidate=((IReadOnlyList<Point2>)type.GetProperty("DiagnosticCandidate")!.GetValue(evaluation)!).ToArray();
        if(original.Length!=candidate.Length)return Fail("Indexed source/candidate count differs.");
        if(!WithinPhysicalLineBand(original,candidate,delta,lineToleranceMm,token,out var observed))
            return Fail("Candidate exceeds the supplied physical line-distance band or outward side is ambiguous.");
        var measure=assembly.GetType("TekstilCAD.Patterns.OffsetUserFeatureToleranceExperiment",true)!.GetMethod("TryVerify",BindingFlags.NonPublic|BindingFlags.Static)!;
        int failed=0;
        foreach(var f in features)
        {
            token.ThrowIfCancellationRequested();if(f is null)return Fail("Invalid selected feature.");
            object?[] args=[original,candidate,f.Start,f.Witness,f.End,f.WidthToleranceMm,f.DepthToleranceMm,null,token];
            bool ok;
            try{ok=(bool)measure.Invoke(null,args)!;}
            catch(TargetInvocationException ex) when(ex.InnerException is OperationCanceledException){throw ex.InnerException;}
            if(!ok)failed++;
        }
        return new(true,Array.AsReadOnly(candidate),features.Length,failed,observed,
            "Diagnostic only: exact binary64 endpoint-to-source supporting-line band, selected feature measurements and existing guards; feature discovery, finite-edge coverage, Hausdorff distance and production are unverified.");
    }
    private static BigInteger Units(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int e=(int)((bits>>52)&0x7ff);
        long f=bits&0x000f_ffff_ffff_ffffL;BigInteger m=e==0?f:f|(1L<<52);if(bits<0)m=-m;
        return m<<(e==0?0:e-1);
    }
    private static bool WithinPhysicalLineBand(Point2[] source,Point2[] candidate,long delta,double tolerance,CancellationToken token,out double observed)
    {
        observed=0;int n=source.Length;BigInteger area=0;
        for(int i=0;i<n;i++){token.ThrowIfCancellationRequested();int j=(i+1)%n;area+=Units(source[i].X)*Units(source[j].Y)-Units(source[i].Y)*Units(source[j].X);}
        if(area.IsZero)return false;int orientation=area.Sign;
        BigInteger target=(BigInteger)delta*(BigInteger.One<<1074),band=Units(tolerance)*1_000_000;
        BigInteger low=target-band,high=target+band;if(low.Sign<0)return false;
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();int j=(i+1)%n;var a=source[i];var b=source[j];
            BigInteger ux=Units(b.X)-Units(a.X),uy=Units(b.Y)-Units(a.Y),length2=ux*ux+uy*uy;
            if(length2.IsZero)return false;
            foreach(var p in new[]{candidate[i],candidate[j]})
            {
                BigInteger px=Units(p.X)-Units(a.X),py=Units(p.Y)-Units(a.Y);
                var signed=-orientation*(ux*py-uy*px);if(signed.Sign<=0)return false;
                BigInteger scaled=signed*1_000_000,squared=scaled*scaled;
                if(squared<low*low*length2 || squared>high*high*length2)return false;
                double ex=b.X-a.X,ey=b.Y-a.Y,rx=p.X-a.X,ry=p.Y-a.Y;
                double distance=-orientation*(ex*ry-ey*rx)/Math.Sqrt(ex*ex+ey*ey);
                observed=Math.Max(observed,Math.Abs(distance-delta/1_000_000d));
            }
        }
        return true;
    }
}
