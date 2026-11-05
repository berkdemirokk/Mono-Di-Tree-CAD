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
        internal UnverifiedResult(IReadOnlyList<Lengths> rules) { Rules=rules; }
        public IReadOnlyList<Lengths> Rules { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified seam lengths; composition blocked.");
    }
    internal static UnverifiedResult Calculate(ResearchBody body,ResearchDesign design)
    {
        ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(design);
        var graph=BodySeamStudy.Calculate(body,design);
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
        foreach(var e in fr.Edges)Register("right_ftorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray());
        foreach(var e in fl.Edges)Register("left_ftorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray());
        foreach(var e in br.Edges)Register("right_btorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray());
        foreach(var e in bl.Edges)Register("left_btorso",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray());
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
        return new(Array.AsReadOnly(graph.Rules.Select(r=>new Lengths(r.Owner,r.Kind,r.Index,Side(r.A),Side(r.B))).ToArray()));
    }
}
