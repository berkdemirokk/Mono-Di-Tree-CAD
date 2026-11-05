namespace FittedResearch;

// Back body candidate: analytic neck and armhole, approximate waist/darts.
// Source BFGS and full curved topology are not certified.
internal static class BackBodiceCandidate
{
    internal readonly record struct P(double X,double Y);
    internal sealed record Edge(int Id,string Kind,P Start,P End,double? ArcControlY,IReadOnlyList<P> Controls);
    internal sealed record Path(string Snapshot,string Panel,IReadOnlyList<Edge> Edges)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    internal static Path Create(string id,bool left)
    {
        var waist=BackWaistCandidate.Create(id);waist.RequireResearchComposition();var neck=BackNeckCut.Create(id);var arm=SyntheticArmholeCut.Create(id).Curves.Single(c=>c.Panel=="right_btorso");
        P W(BackWaistCandidate.P p)=>new(p.X,p.Y);P N(BackNeckCut.P p)=>new(p.X,p.Y);P A(SyntheticArmholeCut.P p)=>new(p.X,p.Y);
        Edge L(int i,P a,P b)=>new(i,"L",a,b,null,Array.Empty<P>());
        var right=new List<Edge>();
        for(int i=0;i<=7;i++) { var e=waist.Edges[i];right.Add(new(i,e.Kind,W(e.Start),W(e.End),null,e.RelativeControl is { } c ? Array.AsReadOnly(new[]{W(c)}) : Array.Empty<P>())); }
        right.Add(L(8,W(waist.Edges[8].Start),A(arm.Start)));
        right.Add(new(9,"C",A(arm.Start),A(arm.End),null,Array.AsReadOnly(new[]{A(arm.Control1),A(arm.Control2)})));
        right.Add(L(10,A(arm.End),N(neck.Right.Start)));
        right.Add(new(11,"A",N(neck.Right.Start),N(neck.Right.End),neck.Right.RelativeControlY,Array.Empty<P>()));
        right.Add(L(12,N(neck.Right.End),W(waist.Edges[0].Start)));
        if(!left)return new(id,"right_btorso",right.AsReadOnly());
        P Mirror(P p)=>new(-p.X,p.Y);
        var reversed=new List<Edge>();
        for(int i=0;i<right.Count;i++)
        {
            var e=right[right.Count-1-i];
            var controls=e.Controls.Reverse().Select(c=>new P(1-c.X,c.Y)).ToArray();
            reversed.Add(new(i,e.Kind,Mirror(e.End),Mirror(e.Start),e.ArcControlY,Array.AsReadOnly(controls)));
        }
        return new(id,"left_btorso",reversed.AsReadOnly());
    }
}
