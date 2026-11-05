using System.Numerics;
namespace FittedResearch;

// Local research-only feasibility proof. It never returns corrected geometry.
internal static class BackWaistConstraintStudy
{
    internal readonly record struct Segment(double Ax,double Ay,double Bx,double By);
    internal enum Finding { ImpossibleUnderConstraints, Unresolved }
    internal sealed record Result(Finding Assessment,double ApproximateVectorLowerBoundCm,double TargetCm,BezierLengthBounds.Bounds? CurrentLength)
    {
        public string Status=>"unresolved";
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public bool ProductionAllowed=>false;public bool ExportAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified constraint study; composition blocked.");
    }
    // Exact integer in units of 2^-1074 for the represented binary64 value.
    static BigInteger Scaled(double value)
    {
        if(!double.IsFinite(value))throw new ArgumentException("Non-finite geometry.");
        long bits=BitConverter.DoubleToInt64Bits(value);int exponent=(int)((bits>>52)&0x7ff);ulong fraction=(ulong)bits&0x000f_ffff_ffff_ffffUL;
        BigInteger integer=new BigInteger(exponent==0?fraction:fraction|(1UL<<52)) << (exponent==0?0:exponent-1);
        return bits<0?-integer:integer;
    }
    // Preconditions: segments are the three ordered waist intervals; global
    // ends and the two dart displacement vectors remain fixed. Translating a
    // whole dart without rotating/deforming it preserves its displacement.
    internal static Result AssessVectors(IReadOnlyList<Segment> intervals,double target)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        if(intervals.Count!=3 || !double.IsFinite(target) || target<=0)throw new ArgumentException("Invalid constraint input.");
        BigInteger dx=0,dy=0;double approxX=0,approxY=0;
        foreach(var e in intervals) {
            dx+=Scaled(e.Bx)-Scaled(e.Ax);dy+=Scaled(e.By)-Scaled(e.Ay);
            approxX+=e.Bx-e.Ax;approxY+=e.By-e.Ay;
        }
        BigInteger limit=Scaled(target);
        // Triangle inequality: sum of the three unknown interval lengths is
        // at least |sum of their displacement vectors|. The strict squared
        // comparison is exact for the represented inputs, without sqrt.
        var finding=dx*dx+dy*dy>limit*limit?Finding.ImpossibleUnderConstraints:Finding.Unresolved;
        double approx=Math.Sqrt(approxX*approxX+approxY*approxY);
        if(!double.IsFinite(approx))throw new ArgumentException("Unsupported geometry magnitude.");
        return new(finding,approx,target,null);
    }
    internal static Result Inspect(IResearchPatternBody body,ResearchDesign design)
    {
        ResearchBody.RequireSupported(body);ArgumentNullException.ThrowIfNull(design);
        var candidate=BackWaistCandidate.CreateFromBody(body,design);var e=candidate.Edges;
        if(e.Count!=11 || e.Where((edge,i)=>edge.Id!=i || edge.End!=e[(i+1)%e.Count].Start).Any() ||
           new[]{0,3,6}.Any(i=>e[i].Kind!="Q") || new[]{1,2,4,5}.Any(i=>e[i].Kind!="L"))
            throw new ArgumentException("Unsupported back waist topology.");
        var intervals=new[]{0,3,6}.Select(i=>new Segment(e[i].Start.X,e[i].Start.Y,e[i].End.X,e[i].End.Y)).ToArray();
        BezierLengthBounds.Bounds? total=null;
        foreach(int i in new[]{0,3,6}) {
            var edge=e[i];if(edge.RelativeControl is not { } c)throw new ArgumentException("Missing waist curve control.");
            double dx=edge.End.X-edge.Start.X,dy=edge.End.Y-edge.Start.Y;
            var controls=new[]{new BezierLengthBounds.Point(edge.Start.X,edge.Start.Y),new BezierLengthBounds.Point(edge.Start.X+dx*c.X-dy*c.Y,edge.Start.Y+dy*c.X+dx*c.Y),new BezierLengthBounds.Point(edge.End.X,edge.End.Y)};
            var bound=BezierLengthBounds.Enclose(controls,8);total=total is null?bound:BezierLengthBounds.Bounds.Add(total,bound);
        }
        return AssessVectors(intervals,(double)body["waist_back_width"]/2) with { CurrentLength=total };
    }
    // There is currently no certified solver for the unresolved branch.
    internal static void AttemptCorrection(IResearchPatternBody body,ResearchDesign design)
    {
        RefuseCorrection(Inspect(body,design));
    }
    internal static void RefuseCorrection(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if(result.Assessment==Finding.ImpossibleUnderConstraints)
            throw new InvalidOperationException("Target impossible with fixed outer endpoints and unrotated, undeformed darts.");
        throw new InvalidOperationException("No certified correction available for this geometry.");
    }
}
