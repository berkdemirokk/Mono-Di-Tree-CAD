using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Experimental exact local evidence tying one short forward edge to one
/// supplied near-straight source corner. It does not identify garment notches,
/// prove edge correspondence/contour topology, compare an inverse boundary, or
/// authorize simplification or production. Bounds are explicit caller inputs.</summary>
public static class OffsetMicroEdgeCornerDiagnostic
{
    public sealed record Evidence(int SourceVertex,int ForwardEdge,BigInteger EdgeLengthSquared,
        BigInteger TurnCrossSquared,BigInteger TurnDotSquared,BigInteger MaximumMiterBoxDistanceSquaredScaled);
    private const int MaximumVertices=1024;

    /// <remarks>All values use integer grid units, except the positive rational
    /// maxTurnNumerator/maxTurnDenominator which bounds |cross|/dot. The
    /// minOtherVertexDistance excludes adjacent source vertices but checks only
    /// vertices, not the distance to other source edges. Inputs must not mutate
    /// while copied. False means rejected or unproved, never unsafe classification.</remarks>
    public static bool TryVerify(IReadOnlyList<GridPoint> source,int corner,IReadOnlyList<GridPoint> forward,int edge,long delta,
        long maxEdgeLength,long maxMiterDistance,long minOtherVertexDistance,long maxTurnNumerator,long maxTurnDenominator,
        out Evidence? evidence,out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(forward);token.ThrowIfCancellationRequested();
        evidence=null;reason="";
        if(source.Count is <3 or >MaximumVertices || forward.Count is <3 or >MaximumVertices || corner<0 || corner>=source.Count || edge<0 || edge>=forward.Count ||
           delta<=0 || maxEdgeLength<=0 || maxMiterDistance<=0 || minOtherVertexDistance<=0 || maxTurnNumerator<=0 || maxTurnDenominator<=0)
        { reason="Invalid input or diagnostic budget.";return false; }
        var points=new GridPoint[source.Count];for(var i=0;i<points.Length;i++) { token.ThrowIfCancellationRequested();points[i]=source[i]; }
        var first=forward[edge];var second=forward[(edge+1)%forward.Count];
        var a=points[(corner+points.Length-1)%points.Length];var b=points[corner];var c=points[(corner+1)%points.Length];
        BigInteger ux=(BigInteger)b.X-a.X,uy=(BigInteger)b.Y-a.Y,vx=(BigInteger)c.X-b.X,vy=(BigInteger)c.Y-b.Y;
        var dot=ux*vx+uy*vy;var cross=ux*vy-uy*vx;
        var nn=(BigInteger)maxTurnNumerator;var dd=(BigInteger)maxTurnDenominator;
        if(dot<=0 || cross*cross*dd*dd>dot*dot*nn*nn)
        { reason="Corner is not proven near-straight under the supplied turn bound.";return false; }
        var ex=(BigInteger)second.X-first.X;var ey=(BigInteger)second.Y-first.Y;var edgeSquared=ex*ex+ey*ey;
        if(edgeSquared.IsZero || edgeSquared>(BigInteger)maxEdgeLength*maxEdgeLength)
        { reason="Forward edge is zero or exceeds the supplied length bound.";return false; }
        var minimumSquared=(BigInteger)minOtherVertexDistance*minOtherVertexDistance;
        for(int i=0;i<points.Length;i++)
        {
            token.ThrowIfCancellationRequested();if(i==corner || i==(corner+1)%points.Length || i==(corner+points.Length-1)%points.Length)continue;
            var x=(BigInteger)points[i].X-b.X;var y=(BigInteger)points[i].Y-b.Y;
            if(x*x+y*y<minimumSquared) { reason="A nonadjacent source vertex is within the supplied exclusion radius.";return false; }
        }
        BigInteger area=0;
        for(int i=0;i<points.Length;i++) { token.ThrowIfCancellationRequested();var p=points[i];var q=points[(i+1)%points.Length];area+=(BigInteger)p.X*q.Y-(BigInteger)p.Y*q.X; }
        if(area.IsZero || !TryBound(a,b,c,delta,area.Sign,out var box))
        { reason="Source orientation or ideal miter enclosure is unavailable.";return false; }
        BigInteger UpperDistanceSquared(GridPoint point)
        {
            var px=(BigInteger)point.X*Scale;var py=(BigInteger)point.Y*Scale;
            var dx=BigInteger.Max(BigInteger.Abs(px-box.X.Lower),BigInteger.Abs(px-box.X.Upper));
            var dy=BigInteger.Max(BigInteger.Abs(py-box.Y.Lower),BigInteger.Abs(py-box.Y.Upper));
            return dx*dx+dy*dy;
        }
        var upper=BigInteger.Max(UpperDistanceSquared(first),UpperDistanceSquared(second));
        if(upper>(BigInteger)maxMiterDistance*maxMiterDistance*Scale*Scale)
        { reason="Forward endpoints cannot be proven within the supplied ideal-miter distance.";return false; }
        token.ThrowIfCancellationRequested();evidence=new(corner,edge,edgeSquared,cross*cross,dot*dot,upper);
        reason="Local bounds hold only; feature identity, other edges, inverse geometry and production suitability remain unverified.";
        return true;
    }
}
