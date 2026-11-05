namespace FittedResearch;

// Back skirt assembled from body-derived partial geometry and the matched
// source-derived synthetic interface. Curves are tangent candidates only.
internal static class BackSkirtCandidate
{
    internal sealed record Edge(int Id,string Kind,PartialFittedEngine.P Start,PartialFittedEngine.P End,TangentCandidate.Point? Control);
    internal sealed record Candidate(string Snapshot,IReadOnlyList<Edge> Edges)
    {
        public string Status=>"unresolved";
        public bool ProductionAllowed=>false;
        public bool ExportAllowed=>false;
    }
    internal static Candidate Create(string id,InterfaceMatcher.Result matched) { InterfaceMatcher.ValidateBound(id,matched);return CreateCore(id,matched); }
    internal static Candidate CreateFromDerived(string id,InterfaceMatcher.Result matched) { InterfaceMatcher.ValidateDerived(id,matched);return CreateCore(id,matched); }
    private static Candidate CreateCore(string id,InterfaceMatcher.Result matched)
    {
        var pieces=PartialFittedEngine.Create(id).Segments.Where(e=>e.Panel=="skirt_back").ToDictionary(e=>e.Edge);
        PartialFittedEngine.Segment Required(int n)=>pieces.TryGetValue(n,out var p)?p:throw new InvalidOperationException("Missing back edge");
        var back=matched.Lower.Where(e=>e.Panel=="skirt_back").ToArray();
        if(back.Length!=6 || !back[2].Flipped || !back[3].Flipped || (back[2].Start-back[3].End).Norm()>1e-8) throw new ArgumentException("Unsupported back interface topology");
        double split=back[2].Start.X;
        var p1=Required(1);var p3=Required(3);var p4=Required(4);var p6=Required(6);var p7=Required(7);
        var p10=Required(10);var p11=Required(11);var p13=Required(13);var p14=Required(14);var p16=Required(16);
        if(!double.IsFinite(split)||split<=p7.End.X||split>=p10.Start.X) throw new ArgumentException("Invalid back split");
        var middle=new PartialFittedEngine.P(split,(p7.End.Y+p10.Start.Y)/2);
        void Add(int n,PartialFittedEngine.P a,PartialFittedEngine.P b)=>pieces.Add(n,new("skirt_back",n,a,b,"Edge"));
        Add(2,p1.End,p3.Start);Add(5,p4.End,p6.Start);Add(8,p7.End,middle);Add(9,middle,p10.Start);Add(12,p11.End,p13.Start);Add(15,p14.End,p16.Start);
        var result=new List<Edge>();
        for(int i=0;i<19;i++)
        {
            var piece=Required(i); bool curve=piece.SourceKind=="CurveEdgeEndpointsOnly";
            var control=curve?(TangentCandidate.Point?)TangentCandidate.RelativeControl(new(piece.Start.X,piece.Start.Y),new(piece.End.X,piece.End.Y),i==1):null;
            result.Add(new(i,curve?"Q":"L",piece.Start,piece.End,control));
        }
        return new(id,result.AsReadOnly());
    }
}
