namespace FittedResearch;

// Conservative tests on binary64 control points already represented by candidates.
// They do not enclose source optimizer or coordinate-rounding errors.
internal static class BodyCurveStudy
{
    internal sealed record Edge(string Kind,double Ax,double Ay,double Bx,double By,IReadOnlyList<(double X,double Y)> RelativeControls,double? ArcY);
    internal sealed record Result(string Panel,int Pairs,int Separated,int EndpointOnly,int Unresolved,int NonMonotone)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    internal static IReadOnlyList<Result> Calculate(ResearchBody body,ResearchDesign design)
    {
        var fr=FrontBodiceCandidate.CreateFromBody(body,design);var fl=FrontBodiceCandidate.CreateLeftFromBody(body,design);
        var br=BackBodiceCandidate.CreateFromBody(body,design,false);var bl=BackBodiceCandidate.CreateFromBody(body,design,true);
        var sf=FrontSkirtCandidate.CreateFromMatchedBody(body,design);var sb=BackSkirtCandidate.CreateFromBody(body,design);
        Edge F(FrontBodiceCandidate.Edge e)=>new(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY);
        Edge B(BackBodiceCandidate.Edge e)=>new(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY);
        Edge S(FrontSkirtCandidate.Edge e)=>new(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is { } c ? new[]{(c.X,c.Y)} : [],null);
        Edge T(BackSkirtCandidate.Edge e)=>new(e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is { } c ? new[]{(c.X,c.Y)} : [],null);
        return Array.AsReadOnly(new[]{Inspect("right_ftorso",fr.Edges.Select(F).ToArray()),Inspect("left_ftorso",fl.Edges.Select(F).ToArray()),Inspect("right_btorso",br.Edges.Select(B).ToArray()),Inspect("left_btorso",bl.Edges.Select(B).ToArray()),Inspect("skirt_front",sf.Edges.Select(S).ToArray()),Inspect("skirt_back",sb.Edges.Select(T).ToArray())});
    }
    internal static Result Inspect(string panel,IReadOnlyList<Edge> edges)
    {
        if(edges.Count<3)throw new ArgumentException("Incomplete contour");
        ExactBezierSeparation.Point[]? Controls(Edge e) {
            if(e.Kind=="A")return null;
            if(e.Kind=="L" && e.RelativeControls.Count==0)return [new(e.Ax,e.Ay),new(e.Bx,e.By)];
            if(e.Kind!="Q" && e.Kind!="C" || e.RelativeControls.Count!=(e.Kind=="Q"?1:2))throw new ArgumentException("Unsupported curve");
            double dx=e.Bx-e.Ax,dy=e.By-e.Ay;
            return new[]{new ExactBezierSeparation.Point(e.Ax,e.Ay)}.Concat(e.RelativeControls.Select(c=>new ExactBezierSeparation.Point(e.Ax+dx*c.X-dy*c.Y,e.Ay+dy*c.X+dx*c.Y))).Append(new(e.Bx,e.By)).ToArray();
        }
        var all=edges.Select(Controls).ToArray();int separated=0,endpoint=0,unresolved=0,nonmonotone=all.Count(c=>c is not null&&!ExactBezierSeparation.HasMonotoneCoordinate(c));
        for(int i=0;i<edges.Count;i++)for(int j=i+1;j<edges.Count;j++) {
            bool adjacent=j==i+1 || i==0&&j==edges.Count-1;bool reverse=i==0&&j==edges.Count-1;
            if(all[i] is null || all[j] is null) {
                int ai=all[i] is null?i:j,oi=ai==i?j:i;if(all[oi] is null || edges[ai].ArcY is null){unresolved++;continue;}
                var a=edges[ai];var p=all[oi]!.Select(x=>new CircleSeparation.Point(x.X,x.Y)).ToArray();
                var verdict=CircleSeparation.Compare(new(a.Ax,a.Ay),new(a.Bx,a.By),a.ArcY.GetValueOrDefault(),p,adjacent);
                if(verdict==CircleSeparation.Verdict.Separated)separated++;else if(verdict==CircleSeparation.Verdict.OnlySharedEndpoint)endpoint++;else unresolved++;
            } else {
                var verdict=reverse?ExactBezierSeparation.Compare(all[j]!,all[i]!,adjacent,12,2000):ExactBezierSeparation.Compare(all[i]!,all[j]!,adjacent,12,2000);
                if(verdict==ExactBezierSeparation.Verdict.Separated)separated++;else if(verdict==ExactBezierSeparation.Verdict.OnlySharedEndpoint)endpoint++;else unresolved++;
            }
        }
        return new(panel,edges.Count*(edges.Count-1)/2,separated,endpoint,unresolved,nonmonotone);
    }
}
