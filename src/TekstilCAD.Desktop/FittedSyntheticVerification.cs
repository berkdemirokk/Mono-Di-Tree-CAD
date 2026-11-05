using System.Windows;
using System.Windows.Media;

namespace TekstilCAD.Desktop;

// Noninteractive verification entry point; does not open a window or write files.
internal static class FittedSyntheticVerification
{
    internal static int Run()
    {
        try {
            foreach(var id in new[]{"synthetic-base","synthetic-distinct"}) {
                var panels=FittedSyntheticAdapter.Create(id);
                if(panels.Count!=6 || panels.Any(p=>p.Geometry.Bounds.IsEmpty || p.Geometry.Figures.Count!=1)) throw new InvalidOperationException("Six bounded panels required.");
            }
            try { FittedSyntheticAdapter.Create("personal"); throw new InvalidOperationException("Unknown catalogue entry accepted."); } catch(ArgumentException) { }
            var edges=new FittedSyntheticAdapter.Edge[] {
                new("Q",new(0,0),new(2,0),[new(.5,.5)],null),
                new("L",new(2,0),new(0,0),[],null) };
            var q=(QuadraticBezierSegment)FittedSyntheticAdapter.Geometry(edges).Figures[0].Segments[0];
            if(q.Point1!=new Point(1,1) || q.Point2!=new Point(2,0)) throw new InvalidOperationException("Relative control conversion failed.");
            try { FittedSyntheticAdapter.Geometry([edges[0],edges[1] with { Start=new Point(3,0) }]); throw new InvalidOperationException("Disconnected contour accepted."); } catch(ArgumentException) { }
            var arc=new FittedSyntheticAdapter.Edge[] { new("A",new(0,0),new(2,0),[],.5),new("L",new(2,0),new(0,0),[],null) };
            var a=(ArcSegment)FittedSyntheticAdapter.Geometry(arc).Figures[0].Segments[0];
            if(a.Size!=new Size(1,1)||a.SweepDirection!=SweepDirection.Counterclockwise||a.IsLargeArc) throw new InvalidOperationException("Arc conversion failed.");
            return 0;
        } catch { return 1; }
    }
}
