namespace FittedResearch;

// Diagnostic relation identities only; no authorization to sew or export.
internal static class BodySeamStudy
{
    internal sealed class UnverifiedGraph
    {
        internal UnverifiedGraph(string snapshot,IReadOnlyList<SyntheticFullSeamGraph.Rule> rules) { Snapshot=snapshot;Rules=rules; }
        public string Snapshot { get; } public IReadOnlyList<SyntheticFullSeamGraph.Rule> Rules { get; }
        public BackWaistCandidate.ReferenceState ReferenceAssessment=>BackWaistCandidate.ReferenceState.Unverified;
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
        public void RequireResearchComposition()=>throw new InvalidOperationException("Unverified seam graph; composition blocked.");
    }
    internal static UnverifiedGraph Calculate(ResearchBody body,ResearchDesign design)
    {
        var fr=FrontBodiceCandidate.CreateFromBody(body,design);var fl=FrontBodiceCandidate.CreateLeftFromBody(body,design);
        var br=BackBodiceCandidate.CreateFromBody(body,design,false);var bl=BackBodiceCandidate.CreateFromBody(body,design,true);
        var sf=FrontSkirtCandidate.CreateFromMatchedBody(body,design);var sb=BackSkirtCandidate.CreateFromBody(body,design);var match=BodyWaistStudy.Calculate(body,design);
        string snapshot=fr.Snapshot;if(new[]{fl.Snapshot,br.Snapshot,bl.Snapshot,sf.Snapshot,sb.Snapshot,match.Snapshot}.Any(x=>x!=snapshot))throw new ArgumentException("Body snapshot mismatch");
        var points=new Dictionary<(string,int),(double ax,double ay,double bx,double by)>();
        foreach(var e in fr.Edges)points.Add(("right_ftorso",e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y));
        foreach(var e in fl.Edges)points.Add(("left_ftorso",e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y));
        foreach(var e in br.Edges)points.Add(("right_btorso",e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y));
        foreach(var e in bl.Edges)points.Add(("left_btorso",e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y));
        foreach(var e in sf.Edges)points.Add(("skirt_front",e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y));
        foreach(var e in sb.Edges)points.Add(("skirt_back",e.Id),(e.Start.X,e.Start.Y,e.End.X,e.End.Y));
        var controls=new Dictionary<(string,int),IReadOnlyList<InterfaceMatcher.P>>();
        void BackControls(BackBodiceCandidate.UnverifiedPanel panel) {
            foreach(var e in panel.Edges) {
                var a=new InterfaceMatcher.P(e.Start.X,e.Start.Y);var d=new InterfaceMatcher.P(e.End.X-e.Start.X,e.End.Y-e.Start.Y);
                controls.Add((panel.Panel,e.Id),e.Controls.Select(c=>a+d*c.X+new InterfaceMatcher.P(-d.Y,d.X)*c.Y).ToArray());
            }
        }
        BackControls(br);BackControls(bl);
        SyntheticFullSeamGraph.Ref E(string panel,int index,bool flip=false) { if(!points.ContainsKey((panel,index)))throw new ArgumentException("Missing panel edge");return new(panel,index,flip,false); }
        SyntheticFullSeamGraph.Side S(SyntheticFullSeamGraph.Ref[] edges,params SyntheticFullSeamGraph.Ruffle[] parts)=>new(Array.AsReadOnly(edges),Array.AsReadOnly(parts));
        SyntheticFullSeamGraph.Side Simple(params SyntheticFullSeamGraph.Ref[] edges)=>S(edges,new SyntheticFullSeamGraph.Ruffle(0,edges.Length,1));
        SyntheticFullSeamGraph.Rule R(string owner,string kind,int index,SyntheticFullSeamGraph.Side a,SyntheticFullSeamGraph.Side b)=>new(owner,kind,index,a,b);
        var rules=new List<SyntheticFullSeamGraph.Rule>();
        void Dart(string panel,string kind,int i,int a,int b)=>rules.Add(R(panel,kind,i,Simple(E(panel,a)),Simple(E(panel,b))));
        Dart("left_ftorso","BodiceFrontHalf",0,5,6);Dart("left_ftorso","BodiceFrontHalf",1,1,2);Dart("right_ftorso","BodiceFrontHalf",0,6,5);Dart("right_ftorso","BodiceFrontHalf",1,10,9);
        int[] starts=[3,6,10,13];for(int i=0;i<4;i++)Dart("skirt_back","FittedSkirtPanel",i,starts[i],starts[i]+1);
        Dart("skirt_front","FittedSkirtPanel",0,9,8);Dart("skirt_front","FittedSkirtPanel",1,5,4);
        Dart("left_btorso","BodiceBackHalf",0,8,7);Dart("left_btorso","BodiceBackHalf",1,11,10);Dart("right_btorso","BodiceBackHalf",0,4,5);Dart("right_btorso","BodiceBackHalf",1,1,2);
        rules.AddRange(new[]{R("left","BodiceHalf",0,Simple(E("left_ftorso",9)),Simple(E("left_btorso",2))),R("left","BodiceHalf",1,Simple(E("left_ftorso",4),E("left_ftorso",7)),Simple(E("left_btorso",5),E("left_btorso",4))),R("right","BodiceHalf",0,Simple(E("right_ftorso",2)),Simple(E("right_btorso",10))),R("right","BodiceHalf",1,Simple(E("right_ftorso",7),E("right_ftorso",4)),Simple(E("right_btorso",7),E("right_btorso",8))),R("FittedShirt","FittedShirt",0,Simple(E("right_ftorso",0)),Simple(E("left_ftorso",11))),R("FittedShirt","FittedShirt",1,Simple(E("right_btorso",12)),Simple(E("left_btorso",0)))});
        rules.Add(R("PencilSkirt","PencilSkirt",0,Simple(E("skirt_front",12),E("skirt_front",11,true)),S([E("skirt_back",0),E("skirt_back",1,true)],new(0,1,1),new(1,2,1.05))));
        rules.Add(R("PencilSkirt","PencilSkirt",1,Simple(E("skirt_front",2,true),E("skirt_front",1)),S([E("skirt_back",16,true),E("skirt_back",17)],new(0,1,1.05),new(1,2,1))));
        SyntheticFullSeamGraph.Ref[] top=[E("right_ftorso",8),E("right_ftorso",11),E("left_ftorso",0),E("left_ftorso",3),E("left_btorso",6),E("left_btorso",9),E("left_btorso",12),E("right_btorso",0),E("right_btorso",3),E("right_btorso",6)];
        SyntheticFullSeamGraph.Ref[] bottom=[E("skirt_front",10,true),E("skirt_front",7,true),E("skirt_front",6,true),E("skirt_front",3,true),E("skirt_back",15,true),E("skirt_back",12,true),E("skirt_back",9,true),E("skirt_back",8,true),E("skirt_back",5,true),E("skirt_back",2,true)];
        void Bind(IReadOnlyList<InterfaceMatcher.Edge> actual,SyntheticFullSeamGraph.Ref[] refs)
        {
            if(actual.Count!=refs.Length)throw new ArgumentException("Unsupported waist topology");
            for(int i=0;i<refs.Length;i++) { var a=actual[i];var r=refs[i];var p=points[(r.Panel,r.Edge)];
                if(a.Panel!=r.Panel || a.Flipped!=r.Flipped || a.RightWrong!=r.RightWrong || Math.Sqrt(Math.Pow(a.Start.X-p.ax,2)+Math.Pow(a.Start.Y-p.ay,2))>1e-8 || Math.Sqrt(Math.Pow(a.End.X-p.bx,2)+Math.Pow(a.End.Y-p.by,2))>1e-8)throw new ArgumentException("Unbound waist edge");
                var expected=controls.TryGetValue((r.Panel,r.Edge),out var c)?c:Array.Empty<InterfaceMatcher.P>();
                if(a.Kind!=(expected.Count==0?"Edge":"CurveEdge") || a.Controls.Length!=expected.Count || !a.Controls.Zip(expected).All(pair=>(pair.First-pair.Second).Norm()<1e-8))throw new ArgumentException("Unbound waist control");
            }
        }
        Bind(match.Upper,top);Bind(match.Lower,bottom);
        rules.Add(R(snapshot,"MetaGarment",0,S(top,new(0,2,1),new(2,4,1),new(4,7,1),new(7,10,1)),S(bottom,new(0,4,1),new(4,10,1))));
        if(rules.Count!=23)throw new ArgumentException("Incomplete graph");return new(snapshot,rules.AsReadOnly());
    }
}
