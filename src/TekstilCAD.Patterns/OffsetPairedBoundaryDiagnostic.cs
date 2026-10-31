using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental fixed-grid comparison against the independently constructed miter candidate.
/// This is not connected to offset production and does not certify physical tolerances,
/// the interval constructor, all feature dimensions, or a machine process.
/// </summary>
public static class OffsetPairedBoundaryDiagnostic
{
    public sealed record Result(bool CandidateConstructed,bool Matched,int CandidateCount,int OutputCount,string Reason);
    private const int MaximumVertices=1024;
    private const long MaximumPairChecks=2_000_000;

    /// <summary>
    /// Requires the same number of vertices, unique cyclic alignment, equal nonzero turn signs,
    /// positive edge progress and an exact endpoint distance bound. Linear interpolation
    /// between matched endpoints then stays within the same bound in both directions.
    /// Extra output vertices, ambiguity, crossings and uncertain construction are rejected.
    /// The caller supplies the grid-unit tolerance; no manufacturing tolerance is inferred.
    /// Inputs must remain stable during their snapshot creation.
    /// </summary>
    public static Result Compare(IReadOnlyList<GridPoint> source,long delta,IReadOnlyList<GridPoint> output,long tolerance,
        CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(output);
        token.ThrowIfCancellationRequested();
        if(source.Count<3 || source.Count>MaximumVertices || output.Count<3 || output.Count>MaximumVertices || tolerance<0)
            return new(false,false,source.Count,output.Count,"Invalid count or tolerance.");
        var actual=new GridPoint[output.Count];
        for(int i=0;i<actual.Length;i++) { token.ThrowIfCancellationRequested();actual[i]=output[i]; }
        if(!OffsetCandidateProof.TryCreate(source,delta,out var candidate,out var reason,token))
            return new(false,false,source.Count,actual.Length,reason);
        int n=candidate.Count;
        if(actual.Length!=n)return new(true,false,n,actual.Length,"Vertex counts differ; no paired correspondence is claimed.");
        if(!Simple(actual,token))return new(true,false,n,n,"Output crosses, touches, or contains a zero edge.");
        var limit=(BigInteger)tolerance*tolerance;
        int matches=0;
        for(int start=0;start<n;start++)
        {
            token.ThrowIfCancellationRequested();
            if(DistanceSquared(candidate[0],actual[start])>limit)continue;
            bool valid=true;
            for(int i=0;i<n;i++)
            {
                if((i&127)==0)token.ThrowIfCancellationRequested();
                var previous=(i+n-1)%n;var next=(i+1)%n;
                var a=candidate[i];var b=actual[(start+i)%n];
                if(DistanceSquared(a,b)>limit) { valid=false;break; }
                var sourceTurn=Cross(candidate[previous],a,candidate[next]);
                var actualTurn=Cross(actual[(start+previous)%n],b,actual[(start+next)%n]);
                if(sourceTurn.IsZero || sourceTurn.Sign!=actualTurn.Sign ||
                   Dot(a,candidate[next],b,actual[(start+next)%n])<=0) { valid=false;break; }
            }
            if(valid && ++matches>1)return new(true,false,n,n,"More than one cyclic alignment is possible.");
        }
        token.ThrowIfCancellationRequested();
        return matches==1 ? new(true,true,n,n,"Paired boundary within supplied tolerance; feature dimensions and production suitability are not certified.")
            : new(true,false,n,n,"No unique forward cyclic pairing satisfies the distance and turn checks.");
    }
    private static BigInteger DistanceSquared(GridPoint a,GridPoint b)
    { var x=(BigInteger)a.X-b.X;var y=(BigInteger)a.Y-b.Y;return x*x+y*y; }
    private static BigInteger Cross(GridPoint a,GridPoint b,GridPoint c) =>
        ((BigInteger)b.X-a.X)*((BigInteger)c.Y-a.Y)-((BigInteger)b.Y-a.Y)*((BigInteger)c.X-a.X);
    private static BigInteger Dot(GridPoint a,GridPoint b,GridPoint c,GridPoint d) =>
        ((BigInteger)b.X-a.X)*((BigInteger)d.X-c.X)+((BigInteger)b.Y-a.Y)*((BigInteger)d.Y-c.Y);
    private static bool On(GridPoint a,GridPoint b,GridPoint p) => p.X>=Math.Min(a.X,b.X) && p.X<=Math.Max(a.X,b.X) && p.Y>=Math.Min(a.Y,b.Y) && p.Y<=Math.Max(a.Y,b.Y);
    private static bool Intersect(GridPoint a,GridPoint b,GridPoint c,GridPoint d)
    {
        var x=Cross(a,b,c);var y=Cross(a,b,d);var z=Cross(c,d,a);var w=Cross(c,d,b);
        return (x.Sign*y.Sign<0 && z.Sign*w.Sign<0) || (x.IsZero && On(a,b,c)) || (y.IsZero && On(a,b,d)) ||
            (z.IsZero && On(c,d,a)) || (w.IsZero && On(c,d,b));
    }
    private static bool Simple(IReadOnlyList<GridPoint> points,CancellationToken token)
    {
        var n=points.Count;long checks=0;
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();var a=points[i];var b=points[(i+1)%n];if(a==b)return false;
            for(int j=i+1;j<n;j++)
            {
                if((++checks&1023)==0)token.ThrowIfCancellationRequested();
                if(checks>MaximumPairChecks)return false;
                var c=points[j];var d=points[(j+1)%n];
                if(j==i+1 || (i==0 && j==n-1))
                {
                    var common=j==i+1 ? b : a;var first=j==i+1 ? a : b;var last=j==i+1 ? d : c;
                    if(Cross(first,common,last).IsZero && Dot(common,first,common,last)>0)return false;
                }
                else if(Intersect(a,b,c,d))return false;
            }
        }
        return true;
    }
}
