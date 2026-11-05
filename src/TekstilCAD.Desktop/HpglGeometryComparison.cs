using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Screen-fitted overlay of the captured source and its quantized HPGL coordinates.</summary>
internal sealed class HpglGeometryComparison : FrameworkElement
{
    private HpglPieceGeometryPreview? geometry;
    private int[] examples=[];
    private int[] normalized=[];
    private int? focusedIndex;
    internal int? FocusedIndexForVerification=>focusedIndex;
    internal double ZoomFactorForVerification { get; private set; }=1;
    internal double LastLocalScaleForVerification { get; private set; }
    internal Point[] LastLocalSourceForVerification { get; private set; }=[];
    internal Point[] LastLocalPlotForVerification { get; private set; }=[];
    internal HpglGeometryComparison()
    {
        Focusable=true;ClipToBounds=true;
        AutomationProperties.SetName(this,"Kaynak ve HPGL geometrisi karşılaştırması");
        AutomationProperties.SetHelpText(this,"Kaynak kesik çizgi ve üçgen; HPGL düz çizgi ve daire ile gösterilir. İşaretler semboliktir. Ekrana sığdırılmış görünüm fiziksel ölçüm değildir. Nokta koordinatları ve farkları metin alanında okunabilir.");
    }
    internal void SetData(HpglPieceGeometryPreview value,IEnumerable<int> sampleIndices)
    {
        geometry=value;examples=sampleIndices.Where(i=>i>=0 && i<value.Points.Count).Distinct().ToArray();focusedIndex=null;ZoomFactorForVerification=1;
        var indices=new List<int>();var p=value.Points;
        bool Same(int a,int b)=>p[a].SourceX==p[b].SourceX && p[a].SourceY==p[b].SourceY;
        for(var i=0;i<p.Count;i++)if(i==0 || !Same(i,i-1))indices.Add(i);
        if(value.Closed && indices.Count>1 && Same(indices[0],indices[^1]))indices.RemoveAt(indices.Count-1);
        normalized=indices.ToArray();InvalidateVisual();
    }
    internal void ShowWhole() { focusedIndex=null;ZoomFactorForVerification=1;LastLocalScaleForVerification=0;LastLocalSourceForVerification=[];LastLocalPlotForVerification=[];InvalidateVisual(); }
    internal void ShowLocal(int sourceVertexIndex)
    { focusedIndex=examples.Contains(sourceVertexIndex) && Array.IndexOf(normalized,sourceVertexIndex)>=0 ? sourceVertexIndex : null;ZoomFactorForVerification=1;LastLocalScaleForVerification=0;LastLocalSourceForVerification=[];LastLocalPlotForVerification=[];InvalidateVisual(); }
    internal bool ShowVertex(int sourceVertexIndex)
    {
        if(geometry is null || normalized.Length<2 || Array.IndexOf(normalized,sourceVertexIndex)<0)return false;
        focusedIndex=sourceVertexIndex;ZoomFactorForVerification=1;LastLocalScaleForVerification=0;LastLocalSourceForVerification=[];LastLocalPlotForVerification=[];InvalidateVisual();return true;
    }
    internal void SetZoom(double factor) { if(!double.IsFinite(factor))throw new ArgumentOutOfRangeException(nameof(factor));if(focusedIndex is null)return;ZoomFactorForVerification=Math.Clamp(factor,1,8);InvalidateVisual(); }
    internal void Clear() { geometry=null;examples=[];normalized=[];focusedIndex=null;ZoomFactorForVerification=1;LastLocalSourceForVerification=[];LastLocalPlotForVerification=[];InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(StudioTheme.Raised,null,new Rect(RenderSize));
        if(geometry is null || geometry.Points.Count==0 || ActualWidth<24 || ActualHeight<24)return;
        var points=geometry.Points;var units=geometry.UnitsPerMillimeter;
        LastLocalSourceForVerification=[];LastLocalPlotForVerification=[];LastLocalScaleForVerification=0;
        int[] displayIndices;
        if(focusedIndex is int focus)
        {
            var position=Array.IndexOf(normalized,focus);
            if(position<0 || normalized.Length<2)return;
            var previous=position-1;var next=position+1;
            if(geometry.Closed) { previous=(position+normalized.Length-1)%normalized.Length;next=(position+1)%normalized.Length; }
            var local=new List<int>(3);if(previous>=0)local.Add(normalized[previous]);local.Add(focus);if(next<normalized.Length)local.Add(normalized[next]);
            displayIndices=local.ToArray();
        }
        else displayIndices=Enumerable.Range(0,points.Count).ToArray();
        double minX=double.PositiveInfinity,minY=double.PositiveInfinity,maxX=double.NegativeInfinity,maxY=double.NegativeInfinity;
        foreach(var index in displayIndices)
        {
            var p=points[index];
            minX=Math.Min(minX,Math.Min(p.SourceX,p.PlotX/units));maxX=Math.Max(maxX,Math.Max(p.SourceX,p.PlotX/units));
            minY=Math.Min(minY,Math.Min(p.SourceY,p.PlotY/units));maxY=Math.Max(maxY,Math.Max(p.SourceY,p.PlotY/units));
        }
        var width=Math.Max(maxX-minX,1e-9);var height=Math.Max(maxY-minY,1e-9);
        var focusPoint=focusedIndex is int selectedIndex ? points[selectedIndex] : default;
        var extentX=focusedIndex.HasValue ? Math.Max(Math.Abs(maxX-focusPoint.SourceX),Math.Abs(minX-focusPoint.SourceX)) : width/2;
        var extentY=focusedIndex.HasValue ? Math.Max(Math.Abs(maxY-focusPoint.SourceY),Math.Abs(minY-focusPoint.SourceY)) : height/2;
        var factor=Math.Min((ActualWidth-20)/Math.Max(2*extentX,1e-9),(ActualHeight-20)/Math.Max(2*extentY,1e-9));
        var ox=(ActualWidth-width*factor)/2;var oy=(ActualHeight-height*factor)/2;
        Point Map(double x,double y)=>focusedIndex.HasValue
            ? new(ActualWidth/2+(x-focusPoint.SourceX)*factor*ZoomFactorForVerification,ActualHeight/2-(y-focusPoint.SourceY)*factor*ZoomFactorForVerification)
            : new(ox+(x-minX)*factor,ActualHeight-oy-(y-minY)*factor);
        if(focusedIndex.HasValue)
        {
            LastLocalScaleForVerification=factor*ZoomFactorForVerification;
            LastLocalSourceForVerification=displayIndices.Select(i=>Map(points[i].SourceX,points[i].SourceY)).ToArray();
            LastLocalPlotForVerification=displayIndices.Select(i=>Map(points[i].PlotX/units,points[i].PlotY/units)).ToArray();
        }
        var sourcePen=new Pen(Brushes.DeepSkyBlue,1.7) { DashStyle=DashStyles.Dash };var plotPen=new Pen(Brushes.Orange,1.7);
        void DrawPath(bool source,Pen pen)
        {
            var path=new StreamGeometry();using(var context=path.Open())
            {
                var first=points[displayIndices[0]];context.BeginFigure(Map(source?first.SourceX:first.PlotX/units,source?first.SourceY:first.PlotY/units),false,geometry.Closed && !focusedIndex.HasValue);
                foreach(var i in displayIndices.Skip(1)) { var p=points[i];context.LineTo(Map(source?p.SourceX:p.PlotX/units,source?p.SourceY:p.PlotY/units),true,false); }
            }
            path.Freeze();dc.DrawGeometry(null,pen,path);
        }
        DrawPath(true,sourcePen);DrawPath(false,plotPen);
        foreach(var i in focusedIndex is int selected ? new[] { selected } : examples)
        {
            var p=points[i];var a=Map(p.SourceX,p.SourceY);var b=Map(p.PlotX/units,p.PlotY/units);
            var triangle=new StreamGeometry();using(var ctx=triangle.Open()) { ctx.BeginFigure(new Point(a.X,a.Y-5),true,true);ctx.LineTo(new Point(a.X-5,a.Y+4),true,false);ctx.LineTo(new Point(a.X+5,a.Y+4),true,false); }
            triangle.Freeze();dc.DrawGeometry(Brushes.DeepSkyBlue,null,triangle);dc.DrawEllipse(null,new Pen(Brushes.Orange,1.5),b,5,5);
            var label=new FormattedText((i+1).ToString(CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),9,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label,new Point(b.X+5,b.Y-9));
        }
    }
}
