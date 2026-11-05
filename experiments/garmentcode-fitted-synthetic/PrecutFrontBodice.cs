namespace FittedResearch;

// Source BodiceFrontHalf before collar/armhole corner operations. This is not
// the final garment panel. Coordinates are reconstructed in centimetres.
internal static class PrecutFrontBodice
{
    internal sealed record Path(string Snapshot,IReadOnlyList<PartialFittedEngine.P> Vertices,IReadOnlyList<GeneratedGraph.Seam> DartRules)
    {
        public string Status=>"unresolved";
        public bool ProductionAllowed=>false;
        public bool ExportAllowed=>false;
    }
    internal static Path Create(string id)
    {
        var generated=PartialFittedEngine.Create(id).Segments.Where(e=>e.Panel=="right_ftorso").ToDictionary(e=>e.Edge);
        PartialFittedEngine.Segment Edge(int i)=>generated.TryGetValue(i,out var edge)?edge:throw new InvalidOperationException("Missing reconstructed edge");
        var v=new List<PartialFittedEngine.P>();
        foreach(int n in new[]{11,10,9,8,7,6,5}) v.Add(Edge(n).End);
        v.Add(Edge(5).Start);
        double shoulder=18, angle=22*Math.PI/180;
        double frontLength=id switch { "synthetic-base"=>41,"synthetic-distinct"=>42,_=>throw new ArgumentException("Unknown synthetic case") };
        v.Add(new(-shoulder,frontLength)); v.Add(new(0,frontLength+Math.Tan(angle)*shoulder)); v.Add(new(0,0));
        // The first vertex is (0,0); last appended duplicate is not part of loop.
        v.RemoveAt(v.Count-1);
        var seams=new[]{new GeneratedGraph.Seam(id,"precut_front",5,false,"precut_front",6,false),new GeneratedGraph.Seam(id,"precut_front",1,false,"precut_front",2,false)};
        return new(id,v.AsReadOnly(),Array.AsReadOnly(seams));
    }
}

