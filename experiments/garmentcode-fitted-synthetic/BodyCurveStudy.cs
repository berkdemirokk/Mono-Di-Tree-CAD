namespace FittedResearch;

// Conservative tests on binary64 control points already represented by candidates.
// They do not enclose source optimizer or coordinate-rounding errors.
internal static class BodyCurveStudy
{
    internal sealed record Edge(string Panel,int Id,string Kind,double Ax,double Ay,double Bx,double By,IReadOnlyList<(double X,double Y)> RelativeControls,double? ArcY);
    internal enum ClosureEvidence { ClosedRepresented, InvalidIdentity, Open, Degenerate }
    internal sealed record Result(string Panel,ClosureEvidence Closure,int Pairs,int Separated,int EndpointOnly,int Unresolved,int NonMonotone,IReadOnlyList<(int A,int B)> UnresolvedEdges)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    internal static IReadOnlyList<Result> Calculate(IResearchPatternBody body,ResearchDesign design)
    {
        ResearchBody.RequireSupported(body);
        var fr=FrontBodiceCandidate.CreateFromBody(body,design);var fl=FrontBodiceCandidate.CreateLeftFromBody(body,design);
        var br=BackBodiceCandidate.CreateFromBody(body,design,false);var bl=BackBodiceCandidate.CreateFromBody(body,design,true);
        var sf=FrontSkirtCandidate.CreateFromMatchedBody(body,design);var sb=BackSkirtCandidate.CreateFromBody(body,design);
        Edge F(string panel,FrontBodiceCandidate.Edge e)=>new(panel,e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY);
        Edge B(string panel,BackBodiceCandidate.Edge e)=>new(panel,e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Controls.Select(c=>(c.X,c.Y)).ToArray(),e.ArcControlY);
        Edge S(FrontSkirtCandidate.Edge e)=>new("skirt_front",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is { } c ? new[]{(c.X,c.Y)} : [],null);
        Edge T(BackSkirtCandidate.Edge e)=>new("skirt_back",e.Id,e.Kind,e.Start.X,e.Start.Y,e.End.X,e.End.Y,e.Control is { } c ? new[]{(c.X,c.Y)} : [],null);
        return Array.AsReadOnly(new[]{Inspect("right_ftorso",fr.Edges.Select(e=>F("right_ftorso",e)).ToArray(),12),Inspect("left_ftorso",fl.Edges.Select(e=>F("left_ftorso",e)).ToArray(),12),Inspect("right_btorso",br.Edges.Select(e=>B("right_btorso",e)).ToArray(),13),Inspect("left_btorso",bl.Edges.Select(e=>B("left_btorso",e)).ToArray(),13),Inspect("skirt_front",sf.Edges.Select(S).ToArray(),13),Inspect("skirt_back",sb.Edges.Select(T).ToArray(),19)});
    }
    internal static Result Inspect(string panel,IReadOnlyList<Edge> edges,int? expectedEdgeCount=null)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if(string.IsNullOrWhiteSpace(panel) || edges.Count<3)throw new ArgumentException("Incomplete contour");
        Result Failure(ClosureEvidence evidence)=>new(panel,evidence,0,0,0,0,0,Array.Empty<(int,int)>());
        if(expectedEdgeCount.HasValue && edges.Count!=expectedEdgeCount.Value || edges.Where((e,i)=>e is null || e.Panel!=panel || e.Id!=i).Any())return Failure(ClosureEvidence.InvalidIdentity);
        if(edges.Any(e=>!double.IsFinite(e.Ax)||!double.IsFinite(e.Ay)||!double.IsFinite(e.Bx)||!double.IsFinite(e.By)||e.Ax==e.Bx&&e.Ay==e.By))return Failure(ClosureEvidence.Degenerate);
        // Equality is exact for the represented binary64 endpoints only. It is
        // not an enclosure of earlier rounding or the source optimizer.
        if(edges.Where((e,i)=>e.Bx!=edges[(i+1)%edges.Count].Ax || e.By!=edges[(i+1)%edges.Count].Ay).Any())return Failure(ClosureEvidence.Open);
        ExactBezierSeparation.Point[]? Controls(Edge e) {
            if(e.Kind=="A")return null;
            if(e.Kind=="L" && e.RelativeControls.Count==0)return [new(e.Ax,e.Ay),new(e.Bx,e.By)];
            if(e.Kind!="Q" && e.Kind!="C" || e.RelativeControls.Count!=(e.Kind=="Q"?1:2))throw new ArgumentException("Unsupported curve");
            double dx=e.Bx-e.Ax,dy=e.By-e.Ay;
            return new[]{new ExactBezierSeparation.Point(e.Ax,e.Ay)}.Concat(e.RelativeControls.Select(c=>new ExactBezierSeparation.Point(e.Ax+dx*c.X-dy*c.Y,e.Ay+dy*c.X+dx*c.Y))).Append(new(e.Bx,e.By)).ToArray();
        }
        var all=edges.Select(Controls).ToArray();int separated=0,endpoint=0,unresolved=0,nonmonotone=all.Count(c=>c is not null&&!ExactBezierSeparation.HasMonotoneCoordinate(c));
        var unresolvedEdges=new List<(int A,int B)>();
        void Mark(int a,int b) { unresolved++;unresolvedEdges.Add((a,b)); }
        for(int i=0;i<edges.Count;i++)for(int j=i+1;j<edges.Count;j++) {
            bool adjacent=j==i+1 || i==0&&j==edges.Count-1;bool reverse=i==0&&j==edges.Count-1;
            if(all[i] is null || all[j] is null) {
                int ai=all[i] is null?i:j,oi=ai==i?j:i;if(all[oi] is null || edges[ai].ArcY is null){Mark(i,j);continue;}
                var a=edges[ai];var p=all[oi]!.Select(x=>new CircleSeparation.Point(x.X,x.Y)).ToArray();
                var verdict=CircleSeparation.Compare(new(a.Ax,a.Ay),new(a.Bx,a.By),a.ArcY.GetValueOrDefault(),p,adjacent);
                if(verdict==CircleSeparation.Verdict.Separated)separated++;else if(verdict==CircleSeparation.Verdict.OnlySharedEndpoint)endpoint++;else Mark(i,j);
            } else {
                var verdict=reverse?ExactBezierSeparation.Compare(all[j]!,all[i]!,adjacent,12,2000):ExactBezierSeparation.Compare(all[i]!,all[j]!,adjacent,12,2000);
                if(verdict==ExactBezierSeparation.Verdict.Separated)separated++;else if(verdict==ExactBezierSeparation.Verdict.OnlySharedEndpoint)endpoint++;else Mark(i,j);
            }
        }
        return new(panel,ClosureEvidence.ClosedRepresented,edges.Count*(edges.Count-1)/2,separated,endpoint,unresolved,nonmonotone,unresolvedEdges.AsReadOnly());
    }
}
