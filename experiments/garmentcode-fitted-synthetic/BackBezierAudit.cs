namespace FittedResearch;
internal static class BackBezierAudit
{
    internal sealed record Result(int Separated,int EndpointOnly,int Unresolved,int NonMonotone)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;
    }
    internal static Result Inspect(BackWaistCandidate.Path path)
    {
        var edges=path.Edges.ToArray();if(edges.Length!=11)throw new ArgumentException("Unexpected back path");
        ExactBezierSeparation.Point[] Controls(BackWaistCandidate.Edge edge)
        {
            var a=new ExactBezierSeparation.Point(edge.Start.X,edge.Start.Y);var b=new ExactBezierSeparation.Point(edge.End.X,edge.End.Y);
            if(edge.Kind=="L")return [a,b];
            if(edge.Kind!="Q"||edge.RelativeControl is null)throw new ArgumentException("Unsupported curve");
            var c=edge.RelativeControl.Value;double dx=b.X-a.X,dy=b.Y-a.Y;
            return [a,new(a.X+dx*c.X-dy*c.Y,a.Y+dy*c.X+dx*c.Y),b];
        }
        var all=edges.Select(Controls).ToArray();int separated=0,endpoint=0,unresolved=0,nonMonotone=all.Count(c=>!ExactBezierSeparation.HasMonotoneCoordinate(c));
        for(int i=0;i<edges.Length;i++)for(int j=i+1;j<edges.Length;j++)
        {
            bool adjacent=j==i+1 || i==0&&j==edges.Length-1;
            var verdict=i==0&&j==edges.Length-1?ExactBezierSeparation.Compare(all[j],all[i],adjacent,12,2000):ExactBezierSeparation.Compare(all[i],all[j],adjacent,12,2000);
            switch(verdict) { case ExactBezierSeparation.Verdict.Separated:separated++;break;case ExactBezierSeparation.Verdict.OnlySharedEndpoint:endpoint++;break;default:unresolved++;break; }
        }
        return new(separated,endpoint,unresolved,nonMonotone);
    }
}
