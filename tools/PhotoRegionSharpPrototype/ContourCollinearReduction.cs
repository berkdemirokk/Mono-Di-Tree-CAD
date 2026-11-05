using OpenCvSharp;

namespace PhotoRegionSharpPrototype;

/// <summary>Removes only strictly forward collinear vertices from a cyclic pixel contour.</summary>
internal static class ContourCollinearReduction
{
    internal static Point[] Reduce(IReadOnlyList<Point> points,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(points);token.ThrowIfCancellationRequested();
        if(points.Count>RegionProposal.MaxPixels*4)throw new ArgumentException("Kontur nokta sınırı aşıldı.");
        var source=points.ToArray();
        for(var i=0;i<source.Length;i++)
        {
            if((i & 4095)==0)token.ThrowIfCancellationRequested();
            if(source[i].X<0 || source[i].Y<0 || source[i].X>=RegionProposal.MaxDimension || source[i].Y>=RegionProposal.MaxDimension)
                throw new ArgumentOutOfRangeException(nameof(points));
        }
        if(source.Length<3)return source;
        var kept=new List<Point>(source.Length);
        for(var i=0;i<source.Length;i++)
        {
            if((i & 4095)==0)token.ThrowIfCancellationRequested();
            var a=source[(i+source.Length-1)%source.Length];var p=source[i];var b=source[(i+1)%source.Length];
            var dx1=(long)p.X-a.X;var dy1=(long)p.Y-a.Y;var dx2=(long)b.X-p.X;var dy2=(long)b.Y-p.Y;
            if(dx1*dy2==dy1*dx2 && dx1*dx2+dy1*dy2>0)continue;
            kept.Add(p);
        }
        token.ThrowIfCancellationRequested();
        // Degenerate inputs stay unchanged; this operation does not validate topology.
        return kept.Count<3 ? source : kept.ToArray();
    }
}
