using System.Windows;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Finds one non-adjacent crossing in a bounded pixel-space preview.</summary>
internal static class PhotoPathCrossingDiagnostic
{
    internal static Point[]? Find(IReadOnlyList<Point> points,bool closed,CancellationToken token)
    {
        if(points.Count<2)return null;
        int count=closed ? points.Count : points.Count-1;
        for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)
        {
            token.ThrowIfCancellationRequested();
            if(j==i+1 || (closed && i==0 && j==count-1))continue;
            var a=points[i];var b=points[(i+1)%points.Count];var c=points[j];var d=points[(j+1)%points.Count];
            if(Geometry.SegmentsIntersect(new Point2(a.X,a.Y),new Point2(b.X,b.Y),new Point2(c.X,c.Y),new Point2(d.X,d.Y)))
                return [a,b,c,d];
        }
        return null;
    }
}
