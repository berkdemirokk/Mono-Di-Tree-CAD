namespace FittedResearch;

// Exact enclosures only for the already represented binary64 candidate
// controls and coefficients. These are not physical sewing tolerances.
internal static class BodySeamProjection
{
    internal sealed record Lengths(string Owner,string Kind,int Index,ProjectedLengthBounds.Interval A,ProjectedLengthBounds.Interval B)
    {
        public bool RepresentedIntervalsDisjoint=>A.Disjoint(B);
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    internal sealed class UnverifiedResult
    {
        internal UnverifiedResult(IReadOnlyList<Lengths> rules,IReadOnlyList<ProjectedLengthBounds.Interval> waistParts) { Rules=rules;WaistParts=waistParts; }
        public IReadOnlyList<Lengths> Rules { get; }
        // Order: upper front, lower front, upper back, lower back.
        public IReadOnlyList<ProjectedLengthBounds.Interval> WaistParts { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified seam lengths; composition blocked.");
    }
    internal static UnverifiedResult Calculate(IResearchPatternBody body,ResearchDesign design)
        =>CalculateCore(body,design,null,null,null);
    internal static UnverifiedResult CalculateChanged(IResearchPatternBody body,ResearchDesign design,BodySeamStudy.UnverifiedGraph graph,CombinedGraphStudy.VerifiedPanels panels)
    {
        ArgumentNullException.ThrowIfNull(panels);
        if(panels.Snapshot!=body.Snapshot(design))throw new ArgumentException("Changed panels belong to another body snapshot");
        if(panels.Shoulder is { } shoulder && shoulder.Snapshot!=panels.Snapshot)throw new ArgumentException("Shoulder snapshot differs");
        return CalculateCore(body,design,graph,panels.Right,panels.Left,panels.Shoulder);
    }
    static UnverifiedResult CalculateCore(IResearchPatternBody body,ResearchDesign design,BodySeamStudy.UnverifiedGraph? supplied,IReadOnlyList<BodyCurveStudy.Edge>? right,IReadOnlyList<BodyCurveStudy.Edge>? left,ShoulderSeamStudy.CertifiedPanelPair? shoulder=null)
    {
        ResearchBody.RequireSupported(body);
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        var graph=BodySeamStudy.Calculate(body,design);
        if(supplied is not null) {
            bool Same(SyntheticFullSeamGraph.Side a,SyntheticFullSeamGraph.Side b)=>a.Edges.SequenceEqual(b.Edges)&&a.Ruffles.SequenceEqual(b.Ruffles);
            if(supplied.Snapshot!=graph.Snapshot || supplied.Rules.Count!=23 || !supplied.Rules.Zip(graph.Rules).All(p=>p.First.Owner==p.Second.Owner&&p.First.Kind==p.Second.Kind&&p.First.Index==p.Second.Index&&Same(p.First.A,p.Second.A)&&Same(p.First.B,p.Second.B)))throw new ArgumentException("Changed graph identity or connection");
            if(right is null||left is null)throw new ArgumentException("Missing changed panels");
            foreach(var (panel,edges) in new[]{("right_btorso",right),("left_btorso",left)}) {
                var t=BodyCurveStudy.Inspect(panel,edges,13);
                if(t.Closure!=BodyCurveStudy.ClosureEvidence.ClosedRepresented||t.Unresolved!=0||t.NonMonotone!=0)throw new ArgumentException("Changed panel topology not established");
            }
        } else if(right is not null || left is not null)throw new ArgumentException("Changed panel requires graph");
        var fr=FrontBodiceCandidate.CreateFromBody(body,design);var fl=FrontBodiceCandidate.CreateLeftFromBody(body,design);
        var br=BackBodiceCandidate.CreateFromBody(body,design,false);var bl=BackBodiceCandidate.CreateFromBody(body,design,true);
        var sf=FrontSkirtCandidate.CreateFromMatchedBody(body,design);var sb=BackSkirtCandidate.CreateFromBody(body,design);
        if(new[]{fr.Snapshot,fl.Snapshot,br.Snapshot,bl.Snapshot,sf.Snapshot,sb.Snapshot}.Any(x=>x!=graph.Snapshot))throw new ArgumentException("Body snapshot mismatch");
        var geometries=new Dictionary<(string,int),BezierLengthBounds.Point[]>();
        void Register(string panel,int index,string kind,double ax,double ay,double bx,double by,IReadOnlyList<(double x,double y)> relative)
        {
            // These rules do not include circular neck edges; reject if they do.
            if(kind is not ("L" or "Q" or "C"))return;
            if((kind=="L"&&relative.Count!=0)||(kind=="Q"&&relative.Count!=1)||(kind=="C"&&relative.Count!=2))throw new ArgumentException("Invalid curve controls");
            double dx=bx-ax,dy=by-ay;var points=new List<BezierLengthBounds.Point>{new(ax,ay)};
            points.AddRange(relative.Select(c=>new BezierLengthBounds.Point(ax+dx*c.x-dy*c.y,ay+dy*c.x+dx*c.y)));
            points.Add(new(bx,by));geometries.Add((panel,index),points.ToArray());
        }
        if(shoulder is not null) {
            foreach(var e in shoulder.Right.Concat(shoulder.Left))Register(e.Panel,e.Id,e.Kind,e.Ax,e.Ay,e.Bx,e.By,e.RelativeControls);
        } else {
            foreach(var e in fr.Edges)Register("right_ftorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray());
            foreach(var e in fl.Edges)Register("left_ftorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray());
        }
        if(right is null)foreach(var e in br.Edges)Register("right_btorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray());
        else foreach(var e in right)Register(e.Panel,e.Id,e.Kind,e.Ax,e.Ay,e.Bx,e.By,e.RelativeControls);
        if(left is null)foreach(var e in bl.Edges)Register("left_btorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray());
        else foreach(var e in left)Register(e.Panel,e.Id,e.Kind,e.Ax,e.Ay,e.Bx,e.By,e.RelativeControls);
        foreach(var e in sf.Edges)Register("skirt_front",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is {} c?new[]{(c.X,c.Y)}:[]);
        foreach(var e in sb.Edges)Register("skirt_back",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is {} c?new[]{(c.X,c.Y)}:[]);
        var cache=new Dictionary<(string,int),BezierLengthBounds.Bounds>();
        ProjectedLengthBounds.Interval Side(SyntheticFullSeamGraph.Side side)
        {
            var coefficients=new double[side.Edges.Count];int covered=0;
            foreach(var section in side.Ruffles) {
                if(section.Start!=covered||section.End<=section.Start||section.End>coefficients.Length||!double.IsFinite(section.Coefficient)||section.Coefficient<=0)throw new ArgumentException("Invalid ruffle sections");
                for(int i=section.Start;i<section.End;i++)coefficients[i]=section.Coefficient;covered=section.End;
            }
            if(covered!=coefficients.Length || covered==0)throw new ArgumentException("Incomplete ruffle sections");
            var total=ProjectedLengthBounds.Interval.Zero;
            for(int i=0;i<side.Edges.Count;i++) {
                var e=side.Edges[i];var key=(e.Panel,e.Edge);
                if(!geometries.TryGetValue(key,out var points))throw new ArgumentException("Unsupported seam edge geometry");
                if(!cache.TryGetValue(key,out var bound)) { bound=BezierLengthBounds.Enclose(points,6);cache.Add(key,bound); }
                total=ProjectedLengthBounds.Interval.Add(total,ProjectedLengthBounds.Project(bound,coefficients[i]));
            }
            return total;
        }
        var full=graph.Rules.Single(r=>r.Kind=="MetaGarment"&&r.Index==0);
        if(full.A.Edges.Count!=10||full.B.Edges.Count!=10||full.A.Ruffles.Any(r=>r.Coefficient!=1)||full.B.Ruffles.Any(r=>r.Coefficient!=1))throw new ArgumentException("Unsupported waist sections");
        ProjectedLengthBounds.Interval Part(SyntheticFullSeamGraph.Side side,int start,int count) {
            var edges=side.Edges.Skip(start).Take(count).ToArray();
            return Side(new SyntheticFullSeamGraph.Side(Array.AsReadOnly(edges),Array.AsReadOnly(new[]{new SyntheticFullSeamGraph.Ruffle(0,count,1)})));
        }
        var parts=new[]{Part(full.A,0,4),Part(full.B,0,4),Part(full.A,4,6),Part(full.B,4,6)};
        return new(Array.AsReadOnly(graph.Rules.Select(r=>new Lengths(r.Owner,r.Kind,r.Index,Side(r.A),Side(r.B))).ToArray()),Array.AsReadOnly(parts));
    }
}
