namespace FittedResearch;

// Source-design front bodice candidate built from C# neck, armhole and darts.
// Curve topology and physical sewing remain unresolved.
internal static class FrontBodiceCandidate
{
    internal readonly record struct P(double X,double Y);
    internal sealed record Edge(int Id,string Kind,P Start,P End,double? ArcControlY,IReadOnlyList<P> Controls);
    internal sealed record Path(string Snapshot,string Panel,IReadOnlyList<Edge> Edges,IReadOnlyList<PartialDartRelations.Rule> Darts)
    {
        public string Status=>"unresolved"; public bool ProductionAllowed=>false; public bool ExportAllowed=>false;
    }
    internal sealed class UnverifiedRight
    {
        internal UnverifiedRight(string snapshot,IReadOnlyList<Edge> edges) { Snapshot=snapshot;Edges=edges; }
        public string Snapshot { get; } public IReadOnlyList<Edge> Edges { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved";public bool ProductionAllowed=>false;public bool ExportAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified front contour; composition blocked.");
    }
    // Diagnostic contour only; curved intersections and source optimization are unproven.
    internal static UnverifiedRight CreateFromBody(ResearchBody body,ResearchDesign design)
    {
        var front=PrecutFrontBodice.CreateFromBody(body,design);var neck=FrontNeckCut.CreateFromBody(body,design);
        var arm=SyntheticArmholeCut.CreateFromBody(body,design);
        if(front.Snapshot!=neck.Snapshot || front.Snapshot!=arm.Snapshot)throw new ArgumentException("Body snapshot mismatch");
        var curve=arm.Curves.Single(c=>c.Panel=="right_ftorso");var arc=neck.Right;var v=front.Vertices;
        P F(PartialFittedEngine.P p)=>new(p.X,p.Y);P A(SyntheticArmholeCut.P p)=>new(p.X,p.Y);P N(FrontNeckCut.P p)=>new(p.X,p.Y);
        if(arc.End.X<=curve.Start.X || arc.End.X>=0 || curve.End.Y<=v[7].Y)throw new ArgumentException("Unsupported neck/armhole placement");
        Edge L(int i,P a,P b)=>new(i,"L",a,b,null,Array.Empty<P>());
        var edges=new List<Edge>{L(0,new(0,0),N(arc.Start)),new(1,"A",N(arc.Start),N(arc.End),arc.RelativeControlY,Array.Empty<P>()),
            L(2,N(arc.End),A(curve.Start)),new(3,"C",A(curve.Start),A(curve.End),null,Array.AsReadOnly(new[]{A(curve.Control1),A(curve.Control2)})),L(4,A(curve.End),F(v[7]))};
        for(int i=5;i<=11;i++)edges.Add(L(i,F(v[12-i]),F(v[11-i])));
        if(edges.Any(e=>e.Start==e.End) || edges.Where((e,i)=>e.End!=edges[(i+1)%edges.Count].Start).Any())throw new ArgumentException("Unsupported front contour continuity");
        return new(front.Snapshot,edges.AsReadOnly());
    }
    internal sealed class UnverifiedLeft
    {
        internal UnverifiedLeft(string snapshot,IReadOnlyList<Edge> edges) { Snapshot=snapshot;Edges=edges; }
        public string Snapshot { get; } public IReadOnlyList<Edge> Edges { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved"; public bool ExportAllowed=>false; public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified front contour; composition blocked.");
    }
    internal static UnverifiedLeft CreateLeftFromBody(ResearchBody body,ResearchDesign design)
    {
        var right=CreateFromBody(body,design);P Mirror(P p)=>new(-p.X,p.Y);
        var reversed=new List<Edge>();
        for(int i=0;i<right.Edges.Count;i++) { var e=right.Edges[right.Edges.Count-1-i];var controls=e.Controls.Reverse().Select(c=>new P(1-c.X,c.Y)).ToArray();reversed.Add(new(i,e.Kind,Mirror(e.End),Mirror(e.Start),e.ArcControlY,Array.AsReadOnly(controls))); }
        return new(right.Snapshot,reversed.AsReadOnly());
    }
    internal static Path Create(string id,bool left)
    {
        string panel=left?"left_ftorso":"right_ftorso";
        var part=PartialFittedEngine.Create(id).Segments.Where(e=>e.Panel==panel).ToDictionary(e=>e.Edge);
        var neck=FrontNeckCut.Create(id);var arc=left?neck.Left:neck.Right;
        var arm=SyntheticArmholeCut.Create(id).Curves.Single(c=>c.Panel==panel);
        P N(FrontNeckCut.P p)=>new(p.X,p.Y); P A(SyntheticArmholeCut.P p)=>new(p.X,p.Y); P G(PartialFittedEngine.P p)=>new(p.X,p.Y);
        Edge L(int index,P a,P b)=>new(index,"L",a,b,null,Array.Empty<P>());
        Edge Existing(int i) { var x=part[i];return L(i,G(x.Start),G(x.End)); }
        Edge Neck(int i)=>new(i,"A",N(arc.Start),N(arc.End),arc.RelativeControlY,Array.Empty<P>());
        Edge Arm(int i)=>new(i,"C",A(arm.Start),A(arm.End),null,Array.AsReadOnly(new[]{A(arm.Control1),A(arm.Control2)}));
        Edge[] edges;
        if(!left)
        {
            edges=[L(0,new(0,0),N(arc.Start)),Neck(1),L(2,N(arc.End),A(arm.Start)),Arm(3),L(4,A(arm.End),G(part[5].Start)),Existing(5),Existing(6),Existing(7),Existing(8),Existing(9),Existing(10),Existing(11)];
        }
        else
        {
            edges=[Existing(0),Existing(1),Existing(2),Existing(3),Existing(4),Existing(5),Existing(6),L(7,G(part[6].End),A(arm.Start)),Arm(8),L(9,A(arm.End),N(arc.Start)),Neck(10),L(11,N(arc.End),new(0,0))];
        }
        var darts=PartialDartRelations.Create(id).Where(r=>r.Owner==panel).ToArray();
        return new(id,panel,Array.AsReadOnly(edges),Array.AsReadOnly(darts));
    }
}
