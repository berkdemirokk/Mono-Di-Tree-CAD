using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

// Noninteractive verification entry point; does not open a window or write files.
internal static class FittedSyntheticVerification
{
    internal static int Run(string? reportPath)
    {
        string stage="başlangıç";
        try {
            foreach(var id in new[]{"synthetic-base","synthetic-distinct"}) {
                stage="katalog: "+id; var panels=FittedSyntheticAdapter.Create(id);
                if(panels.Count!=6 || panels.Any(p=>p.Geometry.Bounds.IsEmpty || p.Geometry.Figures.Count!=1)) throw new InvalidOperationException("Six bounded panels required.");
            }
            stage="bilinmeyen kimlik";
            try { FittedSyntheticAdapter.Create("personal"); throw new InvalidOperationException("Unknown catalogue entry accepted."); } catch(ArgumentException) { }
            stage="yatay kuadratik";
            var edges=new FittedSyntheticAdapter.Edge[] {
                new("Q",new(0,0),new(2,0),[new(.5,.5)],null),
                new("L",new(2,0),new(0,0),[],null) };
            var q=(QuadraticBezierSegment)FittedSyntheticAdapter.Geometry(edges).Figures[0].Segments[0];
            if(q.Point1!=new Point(1,1) || q.Point2!=new Point(2,0)) throw new InvalidOperationException("Relative control conversion failed.");
            try { FittedSyntheticAdapter.Geometry([edges[0],edges[1] with { Start=new Point(3,0) }]); throw new InvalidOperationException("Disconnected contour accepted."); } catch(ArgumentException) { }
            stage="yarım daire";
            var arc=new FittedSyntheticAdapter.Edge[] { new("A",new(0,0),new(2,0),[],.5),new("L",new(2,0),new(0,0),[],null) };
            var a=(ArcSegment)FittedSyntheticAdapter.Geometry(arc).Figures[0].Segments[0];
            if(a.Size!=new Size(1,1)||a.SweepDirection!=SweepDirection.Counterclockwise||a.IsLargeArc) throw new InvalidOperationException("Arc conversion failed.");
            // Skewed chords exercise both the rotation and translation of relative controls.
            stage="eğik kübik";
            var cubic=new FittedSyntheticAdapter.Edge[] { new("C",new(1,2),new(5,5),[new(.25,.5),new(.75,-.25)],null),new("L",new(5,5),new(1,2),[],null) };
            var c=(BezierSegment)FittedSyntheticAdapter.Geometry(cubic).Figures[0].Segments[0];
            if(c.Point1!=new Point(.5,4.75)||c.Point2!=new Point(4.75,3.25)||c.Point3!=new Point(5,5)) throw new InvalidOperationException("Skewed cubic controls failed.");
            stage="eğik kuadratik";
            var skewQ=new FittedSyntheticAdapter.Edge[] { new("Q",new(1,2),new(5,5),[new(.5,.25)],null),new("L",new(5,5),new(1,2),[],null) };
            var sq=(QuadraticBezierSegment)FittedSyntheticAdapter.Geometry(skewQ).Figures[0].Segments[0];
            if(sq.Point1!=new Point(2.25,4.5))throw new InvalidOperationException("Skewed quadratic control failed.");
            foreach(double y in new[]{-.75,-.25,.25,.75}) {
                stage="eğik yay: "+y.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var skewArc=new FittedSyntheticAdapter.Edge[] { new("A",new(1,2),new(5,5),[],y),new("L",new(5,5),new(1,2),[],null) };
                var geometry=FittedSyntheticAdapter.Geometry(skewArc);
                var segment=(ArcSegment)geometry.Figures[0].Segments[0]; double radius=5*(.25+y*y)/(2*Math.Abs(y));
                if(Math.Abs(segment.Size.Width-radius)>1e-12||segment.Point!=new Point(5,5)||segment.IsLargeArc!=(Math.Abs(y)>.5)||segment.SweepDirection!=(y>0?SweepDirection.Counterclockwise:SweepDirection.Clockwise)) throw new InvalidOperationException("Skewed arc flags failed.");
                // Flattening is an area operation and can drop an unfilled open figure.
                // Widen the stroke first, then inspect its outline and render the original.
                var pen=new Pen(Brushes.Black,.01);
                var bounds=geometry.GetRenderBounds(pen);
                var flattened=geometry.GetWidenedPathGeometry(pen,.0001,ToleranceType.Absolute).GetFlattenedPathGeometry(.0001,ToleranceType.Absolute);
                if(geometry.Transform.Value.M22!=-1 || bounds.IsEmpty || bounds.Width<=0 || bounds.Height<=0) {
                    string F(double value)=>value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                    string PointText(Point p)=>$"({F(p.X)},{F(p.Y)})";
                    string figures=string.Join("; ",flattened.Figures.Select((f,i)=>$"figure {i} start={PointText(f.StartPoint)} filled={f.IsFilled} closed={f.IsClosed} segments=["+string.Join(", ",f.Segments.Select(seg=>seg switch {
                        PolyLineSegment poly=>"PolyLine "+string.Join(" ",poly.Points.Select(PointText)),
                        LineSegment line=>"Line "+PointText(line.Point),
                        _=>seg.GetType().Name
                    }))+"]"));
                    var m=geometry.Transform.Value;
                    throw new InvalidOperationException($"Y reflection or stroked arc bounds missing: M22={F(m.M22)}, matrix={m}, empty={bounds.IsEmpty}, x={F(bounds.X)}, y={F(bounds.Y)}, width={F(bounds.Width)}, height={F(bounds.Height)}; flattened transform={flattened.Transform?.Value.ToString() ?? "null"}; {figures}");
                }
                // Relative arc control y is the signed chord-normal sagitta. The SVG
                // reference applies the same final Y reflection as this WPF geometry.
                var expectedMidpoint=new Point(3-3*y,-(3.5+4*y));
                var samples=flattened.Figures.SelectMany(f=>new[]{f.StartPoint}.Concat(f.Segments.SelectMany(seg=>seg switch {
                    PolyLineSegment poly=>poly.Points.AsEnumerable(), LineSegment line=>new[]{line.Point}, _=>Array.Empty<Point>() })));
                if(!samples.Any(p=>(p-expectedMidpoint).Length<.02))throw new InvalidOperationException($"Arc midpoint missing for {y}: expected {expectedMidpoint}.");
                // Render the original unfilled path, not the widened test outline.
                var visual=new DrawingVisual();
                using(var dc=visual.RenderOpen()) {
                    dc.DrawRectangle(Brushes.White,null,new Rect(0,0,120,100));
                    dc.PushTransform(new MatrixTransform(10,0,0,10,40,75));
                    dc.DrawGeometry(null,new Pen(Brushes.Black,.15),geometry); dc.Pop();
                }
                var bitmap=new RenderTargetBitmap(120,100,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
                var pixels=new byte[120*100*4]; bitmap.CopyPixels(pixels,120*4,0);
                if(!Enumerable.Range(0,120*100).Any(i=>pixels[i*4]<200 && pixels[i*4+1]<200 && pixels[i*4+2]<200)) throw new InvalidOperationException("Original unfilled arc did not render.");
            }
            stage="arka plan";
            var threaded=Task.Run(()=>FittedSyntheticAdapter.Create("synthetic-base")).GetAwaiter().GetResult();
            if(threaded.Any(p=>!p.Geometry.IsFrozen || p.Geometry.Bounds.IsEmpty))throw new InvalidOperationException("Background geometry cannot cross the dispatcher.");
            if(reportPath is not null) File.WriteAllText(reportPath,"Sentetik doğrulama başarılı.");
            return 0;
        } catch(Exception ex) {
            string detail="Sentetik doğrulama başarısız · "+stage+Environment.NewLine+ex;
            Console.Error.WriteLine(detail);
            if(reportPath is not null) { try { File.WriteAllText(reportPath,detail); } catch(Exception writeError) { Console.Error.WriteLine("Rapor yazılamadı: "+writeError.Message); } }
            return 1;
        }
    }
}
