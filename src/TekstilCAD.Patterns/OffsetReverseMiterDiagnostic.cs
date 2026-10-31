using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Experimental sampled inverse-boundary investigation only. Distances and
/// angles use binary64 after integer subtraction. This is not a Hausdorff bound,
/// feature-preservation proof, offset certification or production acceptance test.</summary>
public static class OffsetReverseMiterDiagnostic
{
    public sealed record Finding(string Direction,int SampleEdge,double SampleFraction,int NearestEdge,double NearestFraction,
        double DistanceGrid,bool SegmentsIntersect,double SampleStartMiterRatio,double SampleEndMiterRatio,
        double NearestStartMiterRatio,double NearestEndMiterRatio);
    public sealed record Report(IReadOnlyList<Finding> WorstFindings,int SourceVertices,int RecoveredVertices);
    private const int MaximumVertices=1024;
    private const int MaximumFindings=64;
    private const long CoordinateLimit=1L<<60;

    /// <remarks>Inputs use identical integer grid units. They must remain unchanged
    /// while copied. Reports the largest caller-requested vertex/midpoint distances
    /// in both directions; it does not construct or validate either contour.</remarks>
    public static Report Analyze(IReadOnlyList<GridPoint> source,IReadOnlyList<GridPoint> recovered,int findingCount=16,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(recovered);token.ThrowIfCancellationRequested();
        if(source.Count is <3 or >MaximumVertices || recovered.Count is <3 or >MaximumVertices || findingCount is <1 or >MaximumFindings)
            throw new ArgumentException("Diagnostic contour or result limit exceeded.");
        GridPoint[] Copy(IReadOnlyList<GridPoint> input)
        {
            var a=new GridPoint[input.Count];
            for(int i=0;i<a.Length;i++)
            {
                token.ThrowIfCancellationRequested();var p=input[i];
                if(p.X < -CoordinateLimit || p.X > CoordinateLimit || p.Y < -CoordinateLimit || p.Y > CoordinateLimit)
                    throw new ArgumentException("Diagnostic coordinate limit exceeded.");
                a[i]=p;
            }
            return a;
        }
        var original=Copy(source);var inverse=Copy(recovered);
        var best=new PriorityQueue<Finding,double>();
        void Evaluate(GridPoint[] from,GridPoint[] to,string direction)
        {
            for(int i=0;i<from.Length;i++)
            {
                token.ThrowIfCancellationRequested();var a=from[i];var b=from[(i+1)%from.Length];
                double dx=(double)(b.X-a.X),dy=(double)(b.Y-a.Y);
                if(dx==0 && dy==0)throw new ArgumentException("Diagnostic source contains a zero edge.");
                for(int s=0;s<2;s++)
                {
                    double fraction=s*.5,px=dx*fraction,py=dy*fraction;
                    double minimum=double.PositiveInfinity,nearestFraction=0;int nearest=-1;
                    for(int j=0;j<to.Length;j++)
                    {
                        if((j&255)==0)token.ThrowIfCancellationRequested();
                        var c=to[j];var d=to[(j+1)%to.Length];
                        double cx=(double)(c.X-a.X),cy=(double)(c.Y-a.Y),ex=(double)(d.X-c.X),ey=(double)(d.Y-c.Y);
                        double length=ex*ex+ey*ey;
                        if(length==0)throw new ArgumentException("Diagnostic target contains a zero edge.");
                        double t=Math.Clamp(((px-cx)*ex+(py-cy)*ey)/length,0,1);
                        double x=px-cx-t*ex,y=py-cy-t*ey,dist=x*x+y*y;
                        if(dist<minimum) { minimum=dist;nearest=j;nearestFraction=t; }
                    }
                    var distance=Math.Sqrt(minimum);
                    if(best.Count<findingCount || distance>best.Peek().DistanceGrid)
                    {
                        var finding=new Finding(direction,i,fraction,nearest,nearestFraction,distance,
                            Intersects(a,b,to[nearest],to[(nearest+1)%to.Length]),
                            MiterRatio(from,i),MiterRatio(from,(i+1)%from.Length),
                            MiterRatio(to,nearest),MiterRatio(to,(nearest+1)%to.Length));
                        best.Enqueue(finding,distance);if(best.Count>findingCount)best.Dequeue();
                    }
                }
            }
        }
        Evaluate(original,inverse,"source-to-recovered");Evaluate(inverse,original,"recovered-to-source");
        token.ThrowIfCancellationRequested();
        return new Report(best.UnorderedItems.Select(entry=>entry.Element).OrderByDescending(f=>f.DistanceGrid).ToArray(),original.Length,inverse.Length);
    }
    private static double MiterRatio(GridPoint[] contour,int vertex)
    {
        var a=contour[(vertex+contour.Length-1)%contour.Length];var b=contour[vertex];var c=contour[(vertex+1)%contour.Length];
        double ux=(double)(b.X-a.X),uy=(double)(b.Y-a.Y),vx=(double)(c.X-b.X),vy=(double)(c.Y-b.Y);
        var ul=Math.Sqrt(ux*ux+uy*uy);var vl=Math.Sqrt(vx*vx+vy*vy);
        if(ul==0 || vl==0)return double.PositiveInfinity;
        var cosine=Math.Clamp((ux*vx+uy*vy)/(ul*vl),-1,1);
        return cosine<=-1 ? double.PositiveInfinity : Math.Sqrt(2/(1+cosine));
    }
    private static bool Intersects(GridPoint a,GridPoint b,GridPoint c,GridPoint d)
    {
        static BigInteger Cross(GridPoint p,GridPoint q,GridPoint r)=>((BigInteger)q.X-p.X)*((BigInteger)r.Y-p.Y)-((BigInteger)q.Y-p.Y)*((BigInteger)r.X-p.X);
        static bool On(GridPoint p,GridPoint q,GridPoint r)=>r.X>=Math.Min(p.X,q.X) && r.X<=Math.Max(p.X,q.X) && r.Y>=Math.Min(p.Y,q.Y) && r.Y<=Math.Max(p.Y,q.Y);
        var x=Cross(a,b,c);var y=Cross(a,b,d);var z=Cross(c,d,a);var w=Cross(c,d,b);
        return x.IsZero && On(a,b,c) || y.IsZero && On(a,b,d) || z.IsZero && On(c,d,a) || w.IsZero && On(c,d,b) ||
            x.Sign*y.Sign<0 && z.Sign*w.Sign<0;
    }
}
