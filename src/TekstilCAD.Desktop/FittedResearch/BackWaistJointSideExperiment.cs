namespace FittedResearch;

// Synthetic, bounded, two-variable feasibility experiment. It is not a
// garment-graph input or a physical pattern correction.
internal static class BackWaistJointSideExperiment
{
    internal sealed class Result
    {
        internal Result(string id,double shift,double oldY,double newY,BezierLengthBounds.Bounds waist,BezierLengthBounds.Bounds frontSide,BezierLengthBounds.Bounds backSideBefore,BezierLengthBounds.Bounds backSideAfter,BodyCurveStudy.Result beforeTopology,BodyCurveStudy.Result afterTopology,IReadOnlyList<BodyCurveStudy.Edge> contour)
        { Id=id;ShiftCm=shift;OriginalJunctionY=oldY;ProposedJunctionY=newY;Waist=waist;FrontSide=frontSide;BackSideBefore=backSideBefore;BackSideAfter=backSideAfter;BeforeTopology=beforeTopology;AfterTopology=afterTopology;Contour=contour; }
        public string Id { get; } public double ShiftCm { get; } public double OriginalJunctionY { get; } public double ProposedJunctionY { get; }
        public BezierLengthBounds.Bounds Waist { get; } public BezierLengthBounds.Bounds FrontSide { get; } public BezierLengthBounds.Bounds BackSideBefore { get; } public BezierLengthBounds.Bounds BackSideAfter { get; }
        public BodyCurveStudy.Result BeforeTopology { get; } public BodyCurveStudy.Result AfterTopology { get; } public IReadOnlyList<BodyCurveStudy.Edge> Contour { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Joint side experiment cannot be composed.");
    }
    static BezierLengthBounds.Bounds Line(double ax,double ay,double bx,double by)=>BezierLengthBounds.Enclose([new(ax,ay),new(bx,by)],0);
    static BezierLengthBounds.Bounds Pair(BezierLengthBounds.Bounds a,BezierLengthBounds.Bounds b)=>BezierLengthBounds.Bounds.Add(a,b);
    internal static bool WithinDiagnosticTolerance(BezierLengthBounds.Bounds a,BezierLengthBounds.Bounds b,double tolerance)
    {
        if(a is null || b is null || a.Lower<0 || b.Lower<0 || a.Upper<a.Lower || b.Upper<b.Lower || a.Scale is <0 or >36 || b.Scale is <0 or >36)
            throw new ArgumentException("Invalid represented length bounds.");
        if(!double.IsFinite(tolerance)||tolerance<=0)throw new ArgumentException("Invalid diagnostic tolerance.");
        var eps=Line(0,0,tolerance,0);int scale=Math.Max(a.Scale,b.Scale);
        var al=a.Lower<<(scale-a.Scale);var au=a.Upper<<(scale-a.Scale);var bl=b.Lower<<(scale-b.Scale);var bu=b.Upper<<(scale-b.Scale);
        var bound=eps.Lower<<scale;
        // Every exact value in either represented interval differs by at most
        // this numerical diagnostic, not a physical sewing tolerance.
        return au-bl<=bound && bu-al<=bound;
    }
    // For a fixed horizontal offset and fixed endpoints, side length is
    // sqrt(dx² + y²) + (height - y). Only interior junctions are supported.
    internal static double SolveJunction(double startX,double startY,double sideX,double topY,double targetLength)
    {
        if(new[]{startX,startY,sideX,topY,targetLength}.Any(v=>!double.IsFinite(v)) || targetLength<=0 || topY<=startY || sideX==startX)
            throw new ArgumentException("Unsupported side range.");
        double height=topY-startY,dx=sideX-startX,excess=targetLength-height;
        if(!double.IsFinite(excess)||excess<=0)throw new ArgumentException("No side-length solution in supported range.");
        double relativeY=(dx*dx-excess*excess)/(2*excess),newY=startY+relativeY;
        if(!double.IsFinite(newY)||newY<=startY || newY>=topY)throw new ArgumentException("Side junction leaves supported segment.");
        return newY;
    }
    internal static Result Create(string id)
    {
        var waist=BackWaistSideExperiment.Create(id); // rejects unknown and known-mismatched IDs
        var body=SyntheticBodyCatalog.Create(id);
        var back=BackBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted,false);
        var front=FrontBodiceCandidate.CreateFromBody(body,ResearchDesign.PinnedFitted);
        var b7=back.Edges[7];var b8=back.Edges[8];var f4=front.Edges[4];var f7=front.Edges[7];
        if(b7.Kind!="L" || b8.Kind!="L" || f4.Kind!="L" || f7.Kind!="L" || b7.End!=b8.Start || b8.Start.X!=b8.End.X)
            throw new ArgumentException("Unsupported side-junction geometry.");
        var proposedStart=waist.Edges[7].Start;
        var frontBounds=Pair(Line(f4.Start.X,f4.Start.Y,f4.End.X,f4.End.Y),Line(f7.Start.X,f7.Start.Y,f7.End.X,f7.End.Y));
        var backBefore=Pair(Line(b7.Start.X,b7.Start.Y,b7.End.X,b7.End.Y),Line(b8.Start.X,b8.Start.Y,b8.End.X,b8.End.Y));
        double newY=SolveJunction(proposedStart.X,proposedStart.Y,b8.Start.X,b8.End.Y,waist.FrontSideCm);
        var backAfter=Pair(Line(proposedStart.X,proposedStart.Y,b8.Start.X,newY),Line(b8.Start.X,newY,b8.End.X,b8.End.Y));
        if(!WithinDiagnosticTolerance(frontBounds,backAfter,1e-5))throw new ArgumentException("Side-length certificate outside numerical tolerance.");
        var contour=back.Edges.Select(e=>new BodyCurveStudy.Edge("right_btorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,Array.AsReadOnly(e.Controls.Select(c=>(c.X,c.Y)).ToArray()),e.ArcControlY)).ToArray();
        var before=BodyCurveStudy.Inspect("right_btorso",contour,13);
        if(before.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented || before.Unresolved!=0 || before.NonMonotone!=0)throw new ArgumentException("Baseline contour not proven at represented coordinates.");
        contour[6]=waist.TopologyWaistEdge;BackWaistSideExperiment.ValidateTopologyWaistEdge(waist.Edges[6],contour[6]);
        contour[7]=contour[7] with { Ax=proposedStart.X,Ay=proposedStart.Y,By=newY };
        contour[8]=contour[8] with { Ay=newY };
        var after=BodyCurveStudy.Inspect("right_btorso",contour,13);
        if(after.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented || after.Unresolved!=0 || after.NonMonotone!=0)throw new ArgumentException("Joint contour not proven at represented coordinates.");
        return new(id,waist.ShiftCm,b7.End.Y,newY,waist.After,frontBounds,backBefore,backAfter,before,after,Array.AsReadOnly(contour));
    }
}
