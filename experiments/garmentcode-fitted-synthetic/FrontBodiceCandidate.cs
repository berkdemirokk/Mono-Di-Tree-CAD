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
