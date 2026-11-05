namespace FittedResearch;

// Audit of represented C# line/cubic controls only. Arcs and undecidable
// contacts are deliberately unresolved; this cannot approve a pattern.
internal static class FrontBezierAudit
{
    internal sealed record Result(int Separated,int EndpointOnly,int Unresolved,int ArcPairs,int ArcSeparated,int ArcEndpointOnly,int ArcUnresolved,int NonMonotone)
    {
        public string Status=>"unresolved"; public bool ExportAllowed=>false;
    }
    internal static Result Inspect(FrontBodiceCandidate.Path path)
    {
        var edges=path.Edges.ToArray();if(edges.Length!=12)throw new ArgumentException("Unexpected path");
        ExactBezierSeparation.Point[]? Controls(FrontBodiceCandidate.Edge e)
        {
            ExactBezierSeparation.Point P(FrontBodiceCandidate.P p)=>new(p.X,p.Y);
            if(e.Kind=="A")return null;
            if(e.Kind=="L")return [P(e.Start),P(e.End)];
            if(e.Kind!="C" || e.Controls.Count!=2)throw new ArgumentException("Unsupported curve");
            double dx=e.End.X-e.Start.X,dy=e.End.Y-e.Start.Y;
            ExactBezierSeparation.Point Convert(FrontBodiceCandidate.P c)=>new(e.Start.X+dx*c.X-dy*c.Y,e.Start.Y+dy*c.X+dx*c.Y);
            return [P(e.Start),Convert(e.Controls[0]),Convert(e.Controls[1]),P(e.End)];
        }
        var all=edges.Select(Controls).ToArray();int separated=0,endpoint=0,unresolved=0,arcPairs=0,arcSeparated=0,arcEndpoint=0,arcUnresolved=0,nonMonotone=0;
        foreach(var c in all) if(c is not null&&!ExactBezierSeparation.HasMonotoneCoordinate(c))nonMonotone++;
        for(int i=0;i<edges.Length;i++) for(int j=i+1;j<edges.Length;j++)
        {
            if(all[i] is null || all[j] is null)
            {
                arcPairs++;
                int arcIndex=all[i] is null?i:j,otherIndex=arcIndex==i?j:i;
                if(all[otherIndex] is null) { arcUnresolved++;continue; }
                var arc=edges[arcIndex];
                if(arc.ArcControlY is null)throw new ArgumentException("Missing circle control");
                var controls=all[otherIndex]!.Select(p=>new CircleSeparation.Point(p.X,p.Y)).ToArray();
                bool arcAdjacent=j==i+1 || (i==0 && j==edges.Length-1);
                var c=CircleSeparation.Compare(new(arc.Start.X,arc.Start.Y),new(arc.End.X,arc.End.Y),arc.ArcControlY.Value,controls,arcAdjacent);
                switch(c) { case CircleSeparation.Verdict.Separated:arcSeparated++;break;case CircleSeparation.Verdict.OnlySharedEndpoint:arcEndpoint++;break;default:arcUnresolved++;break; }
                continue;
            }
            bool adjacent=j==i+1 || (i==0 && j==edges.Length-1);
            var result=i==0&&j==edges.Length-1
                ?ExactBezierSeparation.Compare(all[j]!,all[i]!,adjacent,12,2000)
                :ExactBezierSeparation.Compare(all[i]!,all[j]!,adjacent,12,2000);
            switch(result) { case ExactBezierSeparation.Verdict.Separated: separated++;break;case ExactBezierSeparation.Verdict.OnlySharedEndpoint:endpoint++;break;default:unresolved++;break; }
        }
        return new(separated,endpoint,unresolved,arcPairs,arcSeparated,arcEndpoint,arcUnresolved,nonMonotone);
    }
}
