using System.Text.Json;

namespace FittedResearch;

// Fixed-design hybrid graph for two synthetic snapshots. Waist splitting is
// sourced from the pinned Python pre-seam fixture via InterfaceMatcher.
internal static class SyntheticFullSeamGraph
{
    internal readonly record struct Ref(string Panel,int Edge,bool Flipped,bool RightWrong);
    internal readonly record struct Ruffle(int Start,int End,double Coefficient);
    internal sealed record Side(IReadOnlyList<Ref> Edges,IReadOnlyList<Ruffle> Ruffles);
    internal sealed record Rule(string Owner,string Kind,int Index,Side A,Side B);
    internal sealed record Graph(string Snapshot,IReadOnlyList<Rule> Rules)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    internal static Graph Create(string id,InterfaceMatcher.Result matched)
    {
        InterfaceMatcher.ValidateBound(id,matched);
        var fr=FrontBodiceCandidate.Create(id,false);var fl=FrontBodiceCandidate.Create(id,true);
        var br=BackBodiceCandidate.Create(id,false);var bl=BackBodiceCandidate.Create(id,true);
        var sf=FrontSkirtCandidate.CreateFromMatchedInterface(id,matched);var sb=BackSkirtCandidate.Create(id,matched);
        var points=new Dictionary<(string,int),(double ax,double ay,double bx,double by)>();
        void F(FrontBodiceCandidate.Path p) { foreach(var e in p.Edges)points.Add((p.Panel,e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y)); }
        void B(BackBodiceCandidate.Path p) { foreach(var e in p.Edges)points.Add((p.Panel,e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y)); }
        F(fr);F(fl);B(br);B(bl);
        foreach(var e in sf.Edges)points.Add(("skirt_front",e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y));
        foreach(var e in sb.Edges)points.Add(("skirt_back",e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y));
        Ref E(string panel,int index,bool flip=false)
        {
            if(!points.ContainsKey((panel,index)))throw new InvalidOperationException("Missing generated graph edge");
            return new(panel,index,flip,false);
        }
        Side S(Ref[] edges,params Ruffle[] parts)=>new(Array.AsReadOnly(edges),Array.AsReadOnly(parts));
        Side Simple(params Ref[] edges)=>S(edges,new Ruffle(0,edges.Length,1));
        Rule R(string owner,string kind,int index,Side a,Side b)=>new(owner,kind,index,a,b);
        var rules=new List<Rule>();
        foreach(var d in PartialDartRelations.Create(id))rules.Add(R(d.Owner,d.Owner.Contains("torso")?"BodiceFrontHalf":"FittedSkirtPanel",d.SourceRule,Simple(E(d.Owner,d.EdgeA,d.FlipA)),Simple(E(d.Owner,d.EdgeB,d.FlipB))));
        rules.AddRange(new[]{
            R("left_btorso","BodiceBackHalf",0,Simple(E("left_btorso",8)),Simple(E("left_btorso",7))),
            R("left_btorso","BodiceBackHalf",1,Simple(E("left_btorso",11)),Simple(E("left_btorso",10))),
            R("right_btorso","BodiceBackHalf",0,Simple(E("right_btorso",4)),Simple(E("right_btorso",5))),
            R("right_btorso","BodiceBackHalf",1,Simple(E("right_btorso",1)),Simple(E("right_btorso",2)))
        });
        foreach(var u in UpperSeamGraph.Create(id).Rules)
        {
            Ref Convert(UpperSeamGraph.EdgeRef r)=>E(r.Panel,r.Edge,r.Flipped);
            rules.Add(R(u.Owner,u.OwnerType,u.Index,Simple(u.A.Select(Convert).ToArray()),Simple(u.B.Select(Convert).ToArray())));
        }
        rules.Add(R("PencilSkirt","PencilSkirt",0,Simple(E("skirt_front",12),E("skirt_front",11,true)),S([E("skirt_back",0),E("skirt_back",1,true)],new Ruffle(0,1,1),new Ruffle(1,2,1.05))));
        rules.Add(R("PencilSkirt","PencilSkirt",1,Simple(E("skirt_front",2,true),E("skirt_front",1)),S([E("skirt_back",16,true),E("skirt_back",17)],new Ruffle(0,1,1.05),new Ruffle(1,2,1))));
        Ref[] top=[E("right_ftorso",8),E("right_ftorso",11),E("left_ftorso",0),E("left_ftorso",3),E("left_btorso",6),E("left_btorso",9),E("left_btorso",12),E("right_btorso",0),E("right_btorso",3),E("right_btorso",6)];
        Ref[] bottom=[E("skirt_front",10,true),E("skirt_front",7,true),E("skirt_front",6,true),E("skirt_front",3,true),E("skirt_back",15,true),E("skirt_back",12,true),E("skirt_back",9,true),E("skirt_back",8,true),E("skirt_back",5,true),E("skirt_back",2,true)];
        void Bind(IReadOnlyList<InterfaceMatcher.Edge> actual,Ref[] refs)
        {
            if(actual.Count!=refs.Length)throw new InvalidOperationException("Waist edge count mismatch");
            for(int i=0;i<refs.Length;i++)
            {
                var a=actual[i];var r=refs[i];var p=points[(r.Panel,r.Edge)];
                double Dist(double x,double y,double u,double v)=>Math.Sqrt((x-u)*(x-u)+(y-v)*(y-v));
                if(a.Panel!=r.Panel||a.Flipped!=r.Flipped||a.RightWrong!=r.RightWrong || Dist(a.Start.X,a.Start.Y,p.ax,p.ay)>.001 || Dist(a.End.X,a.End.Y,p.bx,p.by)>.001) throw new InvalidOperationException("Hybrid waist cannot bind candidate edge");
            }
        }
        Bind(matched.Upper,top);Bind(matched.Lower,bottom);
        rules.Add(R(id,"MetaGarment",0,S(top,new Ruffle(0,2,1),new Ruffle(2,4,1),new Ruffle(4,7,1),new Ruffle(7,10,1)),S(bottom,new Ruffle(0,4,1),new Ruffle(4,10,1))));
        if(rules.Count!=23)throw new InvalidOperationException("Incomplete source-design rules");
        return new(id,rules.AsReadOnly());
    }
    internal static void Compare(Rule rule,JsonElement source)
    {
        if(source.GetProperty("owner").GetString()!=rule.Owner || source.GetProperty("owner_type").GetString()!=rule.Kind || source.GetProperty("index").GetInt32()!=rule.Index)throw new InvalidDataException("Graph owner mismatch");
        void Part(string label,Side expected)
        {
            var side=source.GetProperty(label);var edges=side.GetProperty("edges");var ruffles=side.GetProperty("ruffle");
            if(edges.GetArrayLength()!=expected.Edges.Count || ruffles.GetArrayLength()!=expected.Ruffles.Count)throw new InvalidDataException("Graph side size mismatch");
            for(int i=0;i<expected.Edges.Count;i++)
            {
                var v=edges[i];var e=expected.Edges[i];
                if(v.GetProperty("panel").GetString()!=e.Panel || v.GetProperty("edge").GetInt32()!=e.Edge || v.GetProperty("flipped").GetBoolean()!=e.Flipped || v.GetProperty("right_wrong").GetBoolean()!=e.RightWrong)throw new InvalidDataException("Graph edge mismatch");
            }
            for(int i=0;i<expected.Ruffles.Count;i++)
            {
                var v=ruffles[i];var r=expected.Ruffles[i];var sec=v.GetProperty("sec");
                if(sec[0].GetInt32()!=r.Start||sec[1].GetInt32()!=r.End||v.GetProperty("coeff").GetDouble()!=r.Coefficient)throw new InvalidDataException("Graph ruffle mismatch");
            }
        }
        Part("a",rule.A);Part("b",rule.B);
    }
}
