using System.Text.Json;

namespace FittedResearch;

// Fixed source-design seam relationships between C# generated upper candidates.
// Does not assert physical compatibility, curve topology, or production use.
internal static class UpperSeamGraph
{
    internal readonly record struct EdgeRef(string Panel,int Edge,bool Flipped,bool RightWrong);
    internal sealed record Rule(string Owner,string OwnerType,int Index,IReadOnlyList<EdgeRef> A,IReadOnlyList<EdgeRef> B);
    internal sealed record Result(string Snapshot,IReadOnlyList<Rule> Rules)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    internal static Result Create(string id)
    {
        var frontR=FrontBodiceCandidate.Create(id,false);var frontL=FrontBodiceCandidate.Create(id,true);
        var backR=BackBodiceCandidate.Create(id,false);var backL=BackBodiceCandidate.Create(id,true);
        if(new[]{frontR.Snapshot,frontL.Snapshot,backR.Snapshot,backL.Snapshot}.Any(x=>x!=id))throw new ArgumentException("Snapshot mismatch");
        var keys=frontR.Edges.Select(e=>(frontR.Panel,e.Id)).Concat(frontL.Edges.Select(e=>(frontL.Panel,e.Id))).Concat(backR.Edges.Select(e=>(backR.Panel,e.Id))).Concat(backL.Edges.Select(e=>(backL.Panel,e.Id))).ToHashSet();
        EdgeRef E(string panel,int edge)
        {
            if(!keys.Contains((panel,edge)))throw new InvalidOperationException("Missing generated seam edge");
            return new(panel,edge,false,false);
        }
        Rule R(string owner,string kind,int index,EdgeRef[] a,EdgeRef[] b)=>new(owner,kind,index,Array.AsReadOnly(a),Array.AsReadOnly(b));
        return new(id,Array.AsReadOnly(new[]{
            R("left","BodiceHalf",0,[E("left_ftorso",9)],[E("left_btorso",2)]),
            R("left","BodiceHalf",1,[E("left_ftorso",4),E("left_ftorso",7)],[E("left_btorso",5),E("left_btorso",4)]),
            R("right","BodiceHalf",0,[E("right_ftorso",2)],[E("right_btorso",10)]),
            R("right","BodiceHalf",1,[E("right_ftorso",7),E("right_ftorso",4)],[E("right_btorso",7),E("right_btorso",8)]),
            R("FittedShirt","FittedShirt",0,[E("right_ftorso",0)],[E("left_ftorso",11)]),
            R("FittedShirt","FittedShirt",1,[E("right_btorso",12)],[E("left_btorso",0)])
        }));
    }
    internal sealed record Lengths(string Owner,int Index,double SideA,double SideB)
    {
        public double SignedDifference=>SideA-SideB;
        public string Status=>"unresolved";
    }
    internal static IReadOnlyList<Lengths> Measure(Result graph)
    {
        var fr=FrontBodiceCandidate.Create(graph.Snapshot,false);var fl=FrontBodiceCandidate.Create(graph.Snapshot,true);
        var br=BackBodiceCandidate.Create(graph.Snapshot,false);var bl=BackBodiceCandidate.Create(graph.Snapshot,true);
        var edges=new Dictionary<(string,int),(string kind,double ax,double ay,double bx,double by)>();
        void Front(FrontBodiceCandidate.Path p) { foreach(var e in p.Edges)edges.Add((p.Panel,e.Id),(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y)); }
        void Back(BackBodiceCandidate.Path p) { foreach(var e in p.Edges)edges.Add((p.Panel,e.Id),(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y)); }
        Front(fr);Front(fl);Back(br);Back(bl);
        double Sum(IReadOnlyList<EdgeRef> refs)
        {
            double total=0;
            foreach(var r in refs)
            {
                if(!edges.TryGetValue((r.Panel,r.Edge),out var e)||e.kind!="L")throw new ArgumentException("Unsupported seam geometry");
                double dx=e.bx-e.ax,dy=e.by-e.ay;total+=Math.Sqrt(dx*dx+dy*dy);
            }
            return total;
        }
        return Array.AsReadOnly(graph.Rules.Select(r=>new Lengths(r.Owner,r.Index,Sum(r.A),Sum(r.B))).ToArray());
    }
    internal static void Compare(Rule rule,JsonElement source)
    {
        if(source.GetProperty("owner").GetString()!=rule.Owner || source.GetProperty("owner_type").GetString()!=rule.OwnerType || source.GetProperty("index").GetInt32()!=rule.Index)throw new InvalidDataException("Seam owner mismatch");
        void Side(string name,IReadOnlyList<EdgeRef> expected)
        {
            var side=source.GetProperty(name);var edges=side.GetProperty("edges");
            if(edges.GetArrayLength()!=expected.Count)throw new InvalidDataException("Seam side count");
            for(int i=0;i<expected.Count;i++) { var e=edges[i];var x=expected[i];if(e.GetProperty("panel").GetString()!=x.Panel || e.GetProperty("edge").GetInt32()!=x.Edge || e.GetProperty("flipped").GetBoolean()!=x.Flipped || e.GetProperty("right_wrong").GetBoolean()!=x.RightWrong)throw new InvalidDataException("Seam edge role mismatch"); }
            var r=side.GetProperty("ruffle");
            if(r.GetArrayLength()!=1 || r[0].GetProperty("coeff").GetDouble()!=1 || r[0].GetProperty("sec")[0].GetInt32()!=0 || r[0].GetProperty("sec")[1].GetInt32()!=expected.Count)throw new InvalidDataException("Seam ruffle mismatch");
        }
        Side("a",rule.A);Side("b",rule.B);
    }
}
