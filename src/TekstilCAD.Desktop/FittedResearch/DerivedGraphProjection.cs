namespace FittedResearch;

// Projected seam bounds for the fixed, C#-derived synthetic graph only.
internal static class DerivedGraphProjection
{
    internal sealed record Lengths(string Owner,string Kind,int Index,ProjectedLengthBounds.Interval A,ProjectedLengthBounds.Interval B)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;
    }
    internal static IReadOnlyList<Lengths> Calculate(SyntheticFullSeamGraph.Graph graph,InterfaceMatcher.Result match)
    {
        if(graph.Origin!="CSharpDerived" || graph.Snapshot!=match.Snapshot)throw new ArgumentException("Derived graph snapshot required");
        SyntheticFullSeamGraph.ValidateDerived(graph,match);
        string id=graph.Snapshot;
        var fr=FrontBodiceCandidate.Create(id,false);var fl=FrontBodiceCandidate.Create(id,true);
        var br=BackBodiceCandidate.Create(id,false);var bl=BackBodiceCandidate.Create(id,true);
        var sf=FrontSkirtCandidate.CreateFromDerived(id,match);var sb=BackSkirtCandidate.CreateFromDerived(id,match);
        var geometries=new Dictionary<(string,int),BezierLengthBounds.Point[]>();
        void Register(string panel,int index,string kind,double ax,double ay,double bx,double by,IReadOnlyList<(double x,double y)> rel)
        {
            if(kind is not ("L" or "Q" or "C"))return; // unsupported arcs are not used by this graph
            double dx=bx-ax,dy=by-ay;var controls=new List<BezierLengthBounds.Point>{new(ax,ay)};
            controls.AddRange(rel.Select(c=>new BezierLengthBounds.Point(ax+dx*c.x-dy*c.y,ay+dy*c.x+dx*c.y)));
            controls.Add(new(bx,by));
            if((kind=="L"&&rel.Count!=0)||(kind=="Q"&&rel.Count!=1)||(kind=="C"&&rel.Count!=2))throw new ArgumentException("Invalid graph curve controls");
            geometries.Add((panel,index),controls.ToArray());
        }
        void F(FrontBodiceCandidate.Path p) { foreach(var e in p.Edges)Register(p.Panel,e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray()); }
        void B(BackBodiceCandidate.Path p) { foreach(var e in p.Edges)Register(p.Panel,e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray()); }
        F(fr);F(fl);B(br);B(bl);
        foreach(var e in sf.Edges)Register("skirt_front",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is {} c?new[]{(c.X,c.Y)}:[]);
        foreach(var e in sb.Edges)Register("skirt_back",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is {} c?new[]{(c.X,c.Y)}:[]);
        var cache=new Dictionary<(string,int),BezierLengthBounds.Bounds>();
        ProjectedLengthBounds.Interval Side(SyntheticFullSeamGraph.Side side)
        {
            var coeff=new double[side.Edges.Count];int covered=0;
            foreach(var part in side.Ruffles)
            {
                if(part.Start!=covered || part.End<=part.Start || part.End>coeff.Length || !double.IsFinite(part.Coefficient) || part.Coefficient<=0)throw new ArgumentException("Invalid ruffle sections");
                for(int i=part.Start;i<part.End;i++)coeff[i]=part.Coefficient;covered=part.End;
            }
            if(covered!=coeff.Length)throw new ArgumentException("Incomplete ruffle sections");
            var sum=ProjectedLengthBounds.Interval.Zero;
            for(int i=0;i<side.Edges.Count;i++)
            {
                var e=side.Edges[i];var key=(e.Panel,e.Edge);
                if(!geometries.TryGetValue(key,out var controls))throw new ArgumentException("Unsupported seam edge geometry");
                if(!cache.TryGetValue(key,out var bound)) { bound=BezierLengthBounds.Enclose(controls,6);cache.Add(key,bound); }
                sum=ProjectedLengthBounds.Interval.Add(sum,ProjectedLengthBounds.Project(bound,coeff[i]));
            }
            return sum;
        }
        return Array.AsReadOnly(graph.Rules.Select(r=>new Lengths(r.Owner,r.Kind,r.Index,Side(r.A),Side(r.B))).ToArray());
    }
}
