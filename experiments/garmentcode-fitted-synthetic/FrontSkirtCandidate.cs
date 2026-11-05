namespace FittedResearch;

// Experimental closed front-skirt path, based solely on synthetic body
// formulas. Waist split and curve controls are candidates, not source-exact.
internal static class FrontSkirtCandidate
{
    internal sealed record Edge(int Id,string Kind,PartialFittedEngine.P Start,PartialFittedEngine.P End,TangentCandidate.Point? Control);
    internal sealed record Candidate(string Snapshot,IReadOnlyList<Edge> Edges)
    {
        public string Status=>"unresolved";
        public bool ProductionAllowed=>false;
        public bool ExportAllowed=>false;
    }
    internal static Candidate Create(string id)
    {
        var parts=PartialFittedEngine.Create(id).Segments.Where(e=>e.Panel=="skirt_front").ToDictionary(e=>e.Edge);
        PartialFittedEngine.Segment Required(int i)=>parts.TryGetValue(i,out var value)?value:throw new InvalidOperationException("Missing required edge.");
        var a=Required(2);var b=Required(4);var c=Required(5);var d=Required(8);var e=Required(9);var f=Required(11);
        // Central split is an explicit symmetric candidate. The source uses
        // interface matching, which can place this point elsewhere.
        var middle=new PartialFittedEngine.P((c.End.X+d.Start.X)/2,(c.End.Y+d.Start.Y)/2);
        parts.Add(3,new("skirt_front",3,a.End,b.Start,"Edge"));
        parts.Add(6,new("skirt_front",6,c.End,middle,"Edge"));
        parts.Add(7,new("skirt_front",7,middle,d.Start,"Edge"));
        parts.Add(10,new("skirt_front",10,e.End,f.Start,"Edge"));
        var result=new List<Edge>();
        for(int i=0;i<13;i++)
        {
            var part=Required(i); bool curve=part.SourceKind=="CurveEdgeEndpointsOnly";
            TangentCandidate.Point? control=curve?TangentCandidate.RelativeControl(new(part.Start.X,part.Start.Y),new(part.End.X,part.End.Y),i==2):null;
            result.Add(new(i,curve?"Q":"L",part.Start,part.End,control));
        }
        return new(id,result.AsReadOnly());
    }
}
