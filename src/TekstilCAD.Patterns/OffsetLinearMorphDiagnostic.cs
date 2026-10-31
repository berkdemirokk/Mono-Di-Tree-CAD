using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Experimental exact sufficient proof for an indexed linear morph. It
/// is independent of Clipper and checks a strong sufficient condition for every
/// intermediate polygon to remain simple; it does not prove that either path is
/// an offset, that source features retain their intended dimensions, or that the
/// result is suitable for production. Failure includes unproved safe cases.</summary>
public static class OffsetLinearMorphDiagnostic
{
    private const int MaximumVertices=1024;
    private const long MaximumPairs=550_000;
    private readonly record struct Coefficients(BigInteger Start,BigInteger TwiceMiddle,BigInteger End);

    /// <remarks>Each vertex moves linearly from source[i] to candidate[i]. Inputs
    /// must be stable while copied. An explicitly repeated closing vertex is not
    /// accepted. All orientation-polynomial Bernstein coefficients are evaluated
    /// exactly in integer arithmetic; zero and inconclusive coefficients are rejected.</remarks>
    public static bool TryVerify(IReadOnlyList<GridPoint> source,IReadOnlyList<GridPoint> candidate,
        out string reason,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();reason="";
        var n=source.Count;
        if(n<3 || n>MaximumVertices || candidate.Count!=n || source[0]==source[n-1] || candidate[0]==candidate[n-1])
        { reason="Invalid vertex count or repeated closing vertex.";return false; }
        var a=new GridPoint[n];var b=new GridPoint[n];
        for(var i=0;i<n;i++) { cancellationToken.ThrowIfCancellationRequested();a[i]=source[i];b[i]=candidate[i]; }
        for(var i=0;i<n;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();var j=(i+1)%n;
            BigInteger ux=(BigInteger)a[j].X-a[i].X,uy=(BigInteger)a[j].Y-a[i].Y;
            BigInteger vx=(BigInteger)b[j].X-b[i].X,vy=(BigInteger)b[j].Y-b[i].Y;
            if(ux*vx+uy*vy<=0) { reason=$"Edge {i} may collapse or rotate beyond the proven range.";return false; }
            var previous=(i+n-1)%n;
            var turn=Orientation(a[previous],a[i],a[j],b[previous],b[i],b[j]);
            if(!StrictPositive(turn) && !StrictNegative(turn))
            { reason=$"Adjacent edges at vertex {i} may become collinear or overlap.";return false; }
        }
        long pairs=0;
        for(var i=0;i<n;i++)for(var j=i+1;j<n;j++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(j==i+1 || (i==0 && j==n-1))continue;
            if(++pairs>MaximumPairs) { reason="Pair budget exceeded.";return false; }
            var inext=(i+1)%n;var jnext=(j+1)%n;
            var ac=Orientation(a[i],a[inext],a[j],b[i],b[inext],b[j]);
            var ad=Orientation(a[i],a[inext],a[jnext],b[i],b[inext],b[jnext]);
            if(SameStrictSide(ac,ad))continue;
            var ca=Orientation(a[j],a[jnext],a[i],b[j],b[jnext],b[i]);
            var cb=Orientation(a[j],a[jnext],a[inext],b[j],b[jnext],b[inext]);
            if(!SameStrictSide(ca,cb))
            { reason=$"Edges {i} and {j} lack a strict separating certificate over the morph.";return false; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        reason="The indexed linear morph has nonzero, nonoverlapping adjacent edges and separated nonadjacent edges; offset correctness and feature dimensions remain unverified.";
        return true;
    }
    private static Coefficients Orientation(GridPoint a0,GridPoint b0,GridPoint c0,GridPoint a1,GridPoint b1,GridPoint c1)
    {
        BigInteger ux0=(BigInteger)b0.X-a0.X,uy0=(BigInteger)b0.Y-a0.Y,vx0=(BigInteger)c0.X-a0.X,vy0=(BigInteger)c0.Y-a0.Y;
        BigInteger ux1=(BigInteger)b1.X-a1.X,uy1=(BigInteger)b1.Y-a1.Y,vx1=(BigInteger)c1.X-a1.X,vy1=(BigInteger)c1.Y-a1.Y;
        static BigInteger Cross(BigInteger ux,BigInteger uy,BigInteger vx,BigInteger vy)=>ux*vy-uy*vx;
        return new(Cross(ux0,uy0,vx0,vy0),Cross(ux0,uy0,vx1,vy1)+Cross(ux1,uy1,vx0,vy0),Cross(ux1,uy1,vx1,vy1));
    }
    private static bool StrictPositive(Coefficients c)=>c.Start>0 && c.TwiceMiddle>0 && c.End>0;
    private static bool StrictNegative(Coefficients c)=>c.Start<0 && c.TwiceMiddle<0 && c.End<0;
    private static bool SameStrictSide(Coefficients a,Coefficients b)=>(StrictPositive(a)&&StrictPositive(b)) || (StrictNegative(a)&&StrictNegative(b));
}
