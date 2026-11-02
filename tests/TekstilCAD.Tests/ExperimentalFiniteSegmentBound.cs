using System.Numerics;
using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// Test-only sufficient two-way continuous finite-segment coverage. Inputs are
// grid points, tolerance is in grid units. This is conservative, not a solver
// for the exact Hausdorff distance or a production/feature certificate.
internal static class ExperimentalFiniteSegmentBound
{
    private const long WorkLimit=2_000_000;
    internal static bool Verify(IReadOnlyList<GridPoint> first,IReadOnlyList<GridPoint> second,bool closed,
        long toleranceGrid,int subdivisions,out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(first);ArgumentNullException.ThrowIfNull(second);token.ThrowIfCancellationRequested();
        reason="Invalid inputs, tolerance, subdivision or pair budget.";
        int n=first.Count,m=second.Count;
        if(n<(closed?3:2) || m<(closed?3:2) || n>2048 || m>2048 || toleranceGrid<0 || subdivisions<1 || subdivisions>64)return false;
        int ne=closed?n:n-1,me=closed?m:m-1;
        if(2L*ne*me*subdivisions>WorkLimit)return false;
        var a=new GridPoint[n];var b=new GridPoint[m];
        for(int i=0;i<n;i++){token.ThrowIfCancellationRequested();a[i]=first[i];}
        for(int i=0;i<m;i++){token.ThrowIfCancellationRequested();b[i]=second[i];}
        for(int i=0;i<ne;i++)if(a[i]==a[(i+1)%n]){reason="First path has zero edge.";return false;}
        for(int i=0;i<me;i++)if(b[i]==b[(i+1)%m]){reason="Second path has zero edge.";return false;}
        BigInteger toleranceSquared=(BigInteger)toleranceGrid*toleranceGrid;
        string failure="";
        if(!Covered(a,ne,b,me) || !Covered(b,me,a,ne)){reason=failure;return false;}
        reason="Both continuous finite grid paths satisfy the supplied grid-distance bound; topology, original mm conversion, features and production are not certified.";
        return true;

        bool Covered(GridPoint[] from,int fromEdges,GridPoint[] to,int toEdges)
        {
            for(int i=0;i<fromEdges;i++)for(int k=0;k<subdivisions;k++)
            {
                token.ThrowIfCancellationRequested();var a0=from[i];var a1=from[(i+1)%from.Length];bool matched=false;
                BigInteger x0=(BigInteger)a0.X*(subdivisions-k)+(BigInteger)a1.X*k;
                BigInteger y0=(BigInteger)a0.Y*(subdivisions-k)+(BigInteger)a1.Y*k;
                BigInteger x1=(BigInteger)a0.X*(subdivisions-k-1)+(BigInteger)a1.X*(k+1);
                BigInteger y1=(BigInteger)a0.Y*(subdivisions-k-1)+(BigInteger)a1.Y*(k+1);
                for(int j=0;j<toEdges;j++)
                {
                    token.ThrowIfCancellationRequested();var b0=to[j];var b1=to[(j+1)%to.Length];
                    if(Within(x0,y0,subdivisions,b0,b1,toleranceSquared) && Within(x1,y1,subdivisions,b0,b1,toleranceSquared))
                    {matched=true;break;}
                }
                if(!matched){failure=$"A complete subsegment lacks one shared target segment within the tolerance (edge {i}, part {k}).";return false;}
            }
            return true;
        }
    }
    private static bool Within(BigInteger x,BigInteger y,int denominator,GridPoint a,GridPoint b,BigInteger t2)
    {
        BigInteger dx=(BigInteger)b.X-a.X,dy=(BigInteger)b.Y-a.Y;
        BigInteger px=x-(BigInteger)a.X*denominator,py=y-(BigInteger)a.Y*denominator;
        BigInteger len=dx*dx+dy*dy,projection=px*dx+py*dy,scale=(BigInteger)denominator*denominator;
        if(len.IsZero || projection<=0)return px*px+py*py<=t2*scale;
        if(projection>=len*denominator)
        {BigInteger ex=px-dx*denominator,ey=py-dy*denominator;return ex*ex+ey*ey<=t2*scale;}
        BigInteger cross=px*dy-py*dx;return cross*cross<=t2*scale*len;
    }
    internal static bool VerifyIdealGridMiter(GridPoint[] source,GridPoint[] candidate,long delta,long totalBoundGrid,
        int subdivisions,out string reason,CancellationToken token=default)
    {
        reason="Total bound must allow the two-grid ideal enclosure.";
        if(totalBoundGrid<2)return false;
        var assembly=typeof(PatternTools).Assembly;
        var constructor=assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!.GetMethod("TryCreate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args=[source,delta,null,null,token];
        if(!(bool)constructor.Invoke(null,args)!){reason="Indexed ideal proxy: "+args[3];return false;}
        var proxy=(IReadOnlyList<GridPoint>)args[2]!;
        var proof=assembly.GetType("TekstilCAD.Patterns.OffsetIdealPathBoundExperiment",true)!.GetMethod("TryVerify",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] bound=[source,proxy,delta,null,token];
        if(!(bool)proof.Invoke(null,bound)!){reason="Ideal enclosure: "+bound[3];return false;}
        if(!Verify(proxy,candidate,true,totalBoundGrid-2,subdivisions,out reason,token))return false;
        // Every ideal and proxy indexed point differs <=1 grid per axis; 2
        // grid bounds Euclidean displacement. Triangle inequality then adds
        // the requested polygon bound on both directed continuous distances.
        reason="Grid-defined ideal miter and candidate finite boundaries satisfy the caller's total grid bound, conditional on the interval proof; source binary64, features and production are unverified.";
        return true;
    }
}
