using System.Numerics;
namespace FittedResearch;

// Numerical research classification only; caller must separately establish
// source binding. The band is NOT a physical tolerance or production gate.
internal static class SeamNumericalStudy
{
    internal enum Verdict { Within, Outside, Unresolved }
    internal sealed record Finding(string Owner,string Kind,int Index,Verdict Assessment);
    internal sealed record Result(IReadOnlyList<Finding> Findings,double NumericalBandCm)
    {
        public string Status=>"unresolved";public bool Accepted=>false;public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Numerical seam diagnostics cannot authorize composition");
    }
    internal static Verdict Classify(ProjectedLengthBounds.Interval a,ProjectedLengthBounds.Interval b,double band)
    {
        ArgumentNullException.ThrowIfNull(a);ArgumentNullException.ThrowIfNull(b);
        if(!double.IsFinite(band)||band<=0)throw new ArgumentException("Invalid numerical band");
        // Disjoint validates each rational interval without changing it.
        _=a.Disjoint(b);
        var eps=ProjectedLengthBounds.Project(BezierLengthBounds.Enclose([new(0,0),new(band,0)],0),1);
        if(eps.Lower!=eps.Upper)throw new ArgumentException("Uncertain numerical band");
        BigInteger den=a.Denominator*b.Denominator;
        BigInteger maximum=BigInteger.Max(a.Upper*b.Denominator-b.Lower*a.Denominator,b.Upper*a.Denominator-a.Lower*b.Denominator);
        BigInteger minimum=BigInteger.Max(BigInteger.Zero,BigInteger.Max(a.Lower*b.Denominator-b.Upper*a.Denominator,b.Lower*a.Denominator-a.Upper*b.Denominator));
        BigInteger threshold=eps.Lower*den;
        if(maximum*eps.Denominator<=threshold)return Verdict.Within;
        if(minimum*eps.Denominator>threshold)return Verdict.Outside;
        return Verdict.Unresolved;
    }
    internal static Result Evaluate(IReadOnlyList<BodySeamProjection.Lengths> rules,double band)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if(rules.Count!=23||rules.Any(r=>r is null||string.IsNullOrWhiteSpace(r.Owner)||string.IsNullOrWhiteSpace(r.Kind))||rules.Select(r=>(r.Owner,r.Kind,r.Index)).Distinct().Count()!=23)throw new ArgumentException("Incomplete or duplicated source-bound seam set");
        var findings=rules.Select(r=>new Finding(r.Owner,r.Kind,r.Index,Classify(r.A,r.B,band))).ToArray();
        return new(Array.AsReadOnly(findings),band);
    }
}
