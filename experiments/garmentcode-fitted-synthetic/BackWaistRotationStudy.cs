namespace FittedResearch;

// Necessary bound with fixed outer endpoints and rigid darts, allowing each
// dart to translate and rotate arbitrarily in the plane. No geometry is output.
internal static class BackWaistRotationStudy
{
    internal sealed record Result(bool ImpossibleUnderConstraints,double ApproximateLowerBoundCm,double TargetCm)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    internal static Result Assess(BackWaistConstraintStudy.Segment outer,BackWaistConstraintStudy.Segment firstDart,BackWaistConstraintStudy.Segment secondDart,double target)
    {
        if(!double.IsFinite(target)||target<=0)throw new ArgumentException("Invalid target.");
        BezierLengthBounds.Bounds Line(BackWaistConstraintStudy.Segment s)=>BezierLengthBounds.Enclose([new(s.Ax,s.Ay),new(s.Bx,s.By)],0);
        var o=Line(outer);var d1=Line(firstDart);var d2=Line(secondDart);
        if(d1.Lower.IsZero||d2.Lower.IsZero)throw new ArgumentException("Degenerate dart chord.");
        var targetBounds=BezierLengthBounds.Enclose([new(0,0),new(target,0)],0);
        var upper=BezierLengthBounds.Bounds.Add(BezierLengthBounds.Bounds.Add(d1,d2),targetBounds);
        int scale=Math.Max(o.Scale,upper.Scale);
        bool impossible=(o.Lower<<(scale-o.Scale))>(upper.Upper<<(scale-upper.Scale));
        return new(impossible,o.ApproximateLowerCm-d1.ApproximateUpperCm-d2.ApproximateUpperCm,target);
    }
}
