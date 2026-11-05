using System.Windows;
using System.Windows.Media;
using System.Globalization;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using BodiceWpfReader;

namespace FourPieceResearch;
internal sealed record Scenario(string Id,ALinePanelInput Lower)
{
    internal static Scenario Get(string id)=>id switch {
        "synthetic-base" => new(id,new(350,370,480,520,10,20,20,20,200,700,1600)),
        "synthetic-scaled" => new(id,new(392,414.4,537.6,582.4,11.2,22.4,22.4,22.4,224,784,1792)),
        "synthetic-distinct" => new(id,new(390,410,510,550,15,20,25,25,215,740,1800)),
        _ => throw new InvalidOperationException("Bilinmeyen sentetik senaryo.") };
    internal string Explain => $"Alt panel girdileri (mm): ön/arka bel yayı {Lower.FrontWaistArc}/{Lower.BackWaistArc}, ön/arka kalça yayı {Lower.FrontHipArc}/{Lower.BackHipArc}; ön/arka bel bolluğu {Lower.FrontWaistEase}/{Lower.BackWaistEase}, kalça bolluğu {Lower.FrontHipEase}/{Lower.BackHipEase}; bel–kalça {Lower.HipDepth}, bel–etek {Lower.WaistToHem}, bitmiş etek çevresi {Lower.FinishedHemCircumference}.";
    internal string UpperDeclaration => Id switch {
        "synthetic-base"=>"300, 500, 190, 900, 450, 270, 420, 450, 740, 200, 370, 390, 15, 580, 160",
        "synthetic-scaled"=>"336, 560, 212.8, 1008, 504, 302.4, 470.4, 504, 828.8, 224, 414.4, 436.8, 15, 649.6, 179.2",
        "synthetic-distinct"=>"315, 530, 205, 940, 465, 285, 435, 470, 800, 215, 385, 410, 12, 600, 170",
        _=>throw new InvalidOperationException("Bilinmeyen sentetik senaryo.")};
}
internal sealed record Panel(string Label,PathGeometry Geometry);
internal static class Layout
{
    static PathGeometry Lower(IReadOnlyList<Point2> points){var f=new PathFigure{StartPoint=new Point(points[0].X,points[0].Y),IsClosed=true,IsFilled=false};foreach(var p in points.Skip(1))f.Segments.Add(new LineSegment(new Point(p.X,p.Y),true));return new PathGeometry(new[]{f});}
    internal static Panel[] Build(Preview upper,ALinePanelPreview lower){var front=upper.Parts.Single(p=>p.Name=="breanna.front");var back=upper.Parts.Single(p=>p.Name=="breanna.back");return [new("Ön üst",BodiceResearch.Preview.Geometry(front)),new("Arka üst",BodiceResearch.Preview.Geometry(back)),new("Ön alt",Lower(lower.Front)),new("Arka alt",Lower(lower.Back))];}
    internal static double SharedScale(IReadOnlyList<Panel> panels,Size size){if(panels.Count!=4||size.Width<100||size.Height<100)return 0;var boxes=panels.Select(x=>x.Geometry.Bounds).ToArray();if(boxes.Any(x=>x.IsEmpty))return 0;double cellW=(size.Width-60)/2,cellH=(size.Height-100)/2;return Math.Min(cellW/Math.Max(1,boxes.Max(x=>x.Width)),cellH/Math.Max(1,boxes.Max(x=>x.Height)));}
}
internal sealed class FourView:FrameworkElement
{
    internal IReadOnlyList<Panel>? Panels{get;set;}
    protected override void OnRender(DrawingContext dc){base.OnRender(dc);dc.DrawRectangle(Brushes.White,null,new Rect(RenderSize));if(Panels is not {Count:4} panels)return;double scale=Layout.SharedScale(panels,RenderSize);if(scale<=0)return;double cw=RenderSize.Width/2,ch=RenderSize.Height/2;for(int i=0;i<4;i++){var p=panels[i];var b=p.Geometry.Bounds;double x=(i%2)*cw+20,y=(i/2)*ch+35;dc.DrawText(new FormattedText(p.Label,CultureInfo.GetCultureInfo("tr-TR"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),14,Brushes.Black,1),new Point(x,y-24));dc.PushTransform(new MatrixTransform(scale,0,0,scale,x-b.X*scale,y-b.Y*scale));dc.DrawGeometry(null,new Pen(i<2?Brushes.SteelBlue:Brushes.DarkOrange,2/scale),p.Geometry);dc.Pop();}}
}
