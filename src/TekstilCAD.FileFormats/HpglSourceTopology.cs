using System.Numerics;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>
/// Validates topology of the captured source coordinates before HPGL rounding.
/// Double coordinates are compared exactly as stored: orientation and dot
/// products use a common dyadic integer representation, while bounding-box
/// comparisons only order the original finite values. This is a per-path check;
/// intersections between different pieces and physical output are not checked.
/// </summary>
internal static class HpglSourceTopology
{
    private const int MaximumPoints=10_000;
    private const long MaximumPairs=50_000_000;
    private readonly record struct Edge(int Start,int End);

    /// <summary>
    /// Rejects self-crossings, non-adjacent touches/overlaps and adjacent
    /// backtracking, retaining the same nonzero-edge and adjacency rules as
    /// <see cref="HpglIntersectionDiagnostic.Validate"/>. Caller must keep the
    /// input stable while the snapshot is copied. The caller also enforces an
    /// aggregate job budget across paths; this method enforces its own path budget.
    /// </summary>
    internal static void Validate(IReadOnlyList<Point2> points,bool closed,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();
        if(points.Count<2 || points.Count>MaximumPoints)
            throw new ArgumentException("Kaynak kontur doğrulaması 2–10000 nokta gerektirir.",nameof(points));
        var snapshot=new Point2[points.Count];
        for(var i=0;i<snapshot.Length;i++)
        {
            if((i&1023)==0)cancellationToken.ThrowIfCancellationRequested();
            var p=points[i];
            if(!double.IsFinite(p.X) || !double.IsFinite(p.Y))
                throw new InvalidDataException("Kaynak konturda sonlu olmayan koordinat var.");
            snapshot[i]=p;
        }
        var edges=new List<Edge>(closed ? snapshot.Length : snapshot.Length-1);
        int edgeCount=closed ? snapshot.Length : snapshot.Length-1;
        for(var i=0;i<edgeCount;i++)
        {
            if((i&1023)==0)cancellationToken.ThrowIfCancellationRequested();
            int next=(i+1)%snapshot.Length;
            if(snapshot[i]!=snapshot[next])edges.Add(new(i,next));
        }
        var pairCount=(long)edges.Count*(edges.Count-1)/2;
        if(pairCount>MaximumPairs)throw new InvalidDataException("Kaynak kontur kenar çifti iş sınırını aşıyor.");
        var exact=HpglFormat.ExactCoordinates(snapshot,cancellationToken);
        long examined=0;
        for(var i=0;i<edges.Count;i++)
        for(var j=i+1;j<edges.Count;j++)
        {
            if((examined++&1023)==0)cancellationToken.ThrowIfCancellationRequested();
            var a=edges[i].Start;var b=edges[i].End;var c=edges[j].Start;var d=edges[j].End;
            if(j==i+1 || (closed && i==0 && j==edges.Count-1))
            {
                var shared=j==i+1 ? b : a;
                var first=j==i+1 ? a : b;
                var second=j==i+1 ? d : c;
                if(Cross(exact[first],exact[shared],exact[second]).IsZero)
                {
                    var dx1=exact[first].X-exact[shared].X;var dy1=exact[first].Y-exact[shared].Y;
                    var dx2=exact[second].X-exact[shared].X;var dy2=exact[second].Y-exact[shared].Y;
                    if((dx1*dx2+dy1*dy2).Sign>0)
                        throw new InvalidDataException("Kaynak kontur aynı kenarda geri dönüyor.");
                }
                continue;
            }
            if(Separated(snapshot[a],snapshot[b],snapshot[c],snapshot[d]))continue;
            var abC=Cross(exact[a],exact[b],exact[c]).Sign;var abD=Cross(exact[a],exact[b],exact[d]).Sign;
            var cdA=Cross(exact[c],exact[d],exact[a]).Sign;var cdB=Cross(exact[c],exact[d],exact[b]).Sign;
            if((abC*abD<0 && cdA*cdB<0) || (abC==0 && InBounds(snapshot[a],snapshot[b],snapshot[c])) ||
                (abD==0 && InBounds(snapshot[a],snapshot[b],snapshot[d])) || (cdA==0 && InBounds(snapshot[c],snapshot[d],snapshot[a])) ||
                (cdB==0 && InBounds(snapshot[c],snapshot[d],snapshot[b])))
                throw new InvalidDataException("Kaynak konturda kesişen veya üst üste gelen kenarlar var.");
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
    private static BigInteger Cross((BigInteger X,BigInteger Y) a,(BigInteger X,BigInteger Y) b,(BigInteger X,BigInteger Y) c)
        => (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    private static bool Separated(Point2 a,Point2 b,Point2 c,Point2 d)
        => Math.Max(a.X,b.X)<Math.Min(c.X,d.X) || Math.Max(c.X,d.X)<Math.Min(a.X,b.X) ||
           Math.Max(a.Y,b.Y)<Math.Min(c.Y,d.Y) || Math.Max(c.Y,d.Y)<Math.Min(a.Y,b.Y);
    private static bool InBounds(Point2 a,Point2 b,Point2 p)
        => p.X>=Math.Min(a.X,b.X) && p.X<=Math.Max(a.X,b.X) && p.Y>=Math.Min(a.Y,b.Y) && p.Y<=Math.Max(a.Y,b.Y);
}
