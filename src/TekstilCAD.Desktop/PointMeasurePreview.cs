using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Input;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Overview of a snapshot with conservative, unambiguous point picking.</summary>
internal sealed class PointMeasurePreview : FrameworkElement
{
    private static readonly Brush BackgroundBrush=FrozenBrush(0x15,0x21,0x30);
    private static readonly Brush OutlineBrush=FrozenBrush(0x9d,0xaf,0xc2);
    private static readonly Brush StartBrush=FrozenBrush(0x39,0xd6,0xc6);
    private static readonly Brush EndBrush=FrozenBrush(0xff,0xc4,0x68);
    private static readonly Brush TextBrush=FrozenBrush(0xff,0xff,0xff);
    private readonly IReadOnlyList<Point2> points;
    private readonly StreamGeometry? outline;
    private readonly double minX, minY, span, xExtent, yExtent;
    private const int GridSize=128;
    private readonly Dictionary<(int X,int Y),List<int>> hitGrid=new();
    internal event Action<int>? PointRequested;
    internal int StartIndex { get; private set; } = -1;
    internal int EndIndex { get; private set; } = -1;
    internal bool IndividualVerticesVisible => points.Count<=500;

    internal PointMeasurePreview(PatternPiece snapshot)
    {
        points=snapshot.Points.ToArray();
        Height=180; MinHeight=150; Margin=new Thickness(0,8,0,0); ClipToBounds=true;
        AutomationProperties.SetName(this,"Kalıp konturu ve ölçüm noktaları önizlemesi");
        AutomationProperties.SetHelpText(this,"Salt okunur önizleme. A halka, B baklava işaretiyle gösterilir; nokta seçimi yukarıdaki listelerden yapılır.");
        if(points.Count==0 || points.Any(p=>!p.IsFinite)) return;
        minX=points.Min(p=>p.X); minY=points.Min(p=>p.Y);
        var maxX=points.Max(p=>p.X); var maxY=points.Max(p=>p.Y);
        span=Math.Max(maxX-minX,maxY-minY);
        if(!double.IsFinite(span))return;
        if(span==0)span=1;
        xExtent=(maxX-minX)/span; yExtent=(maxY-minY)/span;
        for(var i=0;i<points.Count;i++)
        {
            var p=Normalized(points[i]); var key=(Cell(p.X),Cell(p.Y));
            if(!hitGrid.TryGetValue(key,out var bucket))hitGrid[key]=bucket=new List<int>();
            bucket.Add(i);
        }
        var geometry=new StreamGeometry();
        using(var context=geometry.Open())
        {
            context.BeginFigure(Normalized(points[0]),false,snapshot.Closed);
            for(var i=1;i<points.Count;i++)context.LineTo(Normalized(points[i]),true,false);
        }
        geometry.Freeze(); outline=geometry;
    }

    internal void SetSelection(int startIndex,int endIndex)
    {
        StartIndex=startIndex>=0 && startIndex<points.Count ? startIndex : -1;
        EndIndex=endIndex>=0 && endIndex<points.Count ? endIndex : -1;
        var a=StartIndex>=0 ? $"A {StartIndex}. nokta" : "A seçilmedi";
        var b=EndIndex>=0 ? $"B {EndIndex}. nokta" : "B seçilmedi";
        AutomationProperties.SetHelpText(this,$"Önizleme. A halka, B baklava işaretidir. {a}; {b}. Noktayı listelerden veya görsele tıklayarak seçin; yakın noktalar için listeyi kullanın.");
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w=ActualWidth;var h=ActualHeight;
        if(w<=0 || h<=0)return;
        dc.DrawRoundedRectangle(BackgroundBrush,null,new Rect(0,0,w,h),5,5);
        if(outline is null)return;
        const double padding=20;
        var scale=Math.Max(0,Math.Min(w-2*padding,h-2*padding));
        if(scale<=0)return;
        // Match the main canvas: increasing Y is drawn downward.  Only the
        // display transform changes; model coordinates remain untouched.
        var x=(w-scale*xExtent)/2;var y=(h-scale*yExtent)/2;
        Point Screen(Point2 p) => new(x+(p.X-minX)/span*scale,y+(p.Y-minY)/span*scale);
        dc.PushTransform(new MatrixTransform(scale,0,0,scale,x,y));
        dc.DrawGeometry(null,new Pen(OutlineBrush,1.6/scale),outline);
        dc.Pop();
        if(IndividualVerticesVisible)
            foreach(var vertex in points) dc.DrawEllipse(OutlineBrush,null,Screen(vertex),2.5,2.5);
        if(StartIndex>=0)
        {
            var p=Screen(points[StartIndex]);
            dc.DrawEllipse(BackgroundBrush,new Pen(StartBrush,2.5),p,7,7);
            DrawLabel(dc,"A",p.X-14,p.Y-20,w,h);
        }
        if(EndIndex>=0)
        {
            var p=Screen(points[EndIndex]);
            var diamond=new StreamGeometry();
            using(var c=diamond.Open()) { c.BeginFigure(new Point(p.X,p.Y-5),true,true);c.LineTo(new Point(p.X+5,p.Y),true,false);c.LineTo(new Point(p.X,p.Y+5),true,false);c.LineTo(new Point(p.X-5,p.Y),true,false); }
            diamond.Freeze();dc.DrawGeometry(EndBrush,null,diamond);
            DrawLabel(dc,"B",p.X+8,p.Y+5,w,h);
        }
    }

    // A click is accepted only if exactly one vertex is within the hit radius.
    // -1 means no hit, -2 means ambiguous; neither changes the selection.
    internal int HitTestPoint(Point position)
    {
        if(outline is null || !double.IsFinite(position.X) || !double.IsFinite(position.Y))return -1;
        const double padding=20, radius=9;
        var scale=Math.Max(0,Math.Min(ActualWidth-2*padding,ActualHeight-2*padding));
        if(scale<=0)return -1;
        var x=(ActualWidth-scale*xExtent)/2; var y=(ActualHeight-scale*yExtent)/2;
        var nx=(position.X-x)/scale; var ny=(position.Y-y)/scale; var range=radius/scale;
        if(nx+range<0 || nx-range>1 || ny+range<0 || ny-range>1)return -1;
        var found=-1;
        for(var gx=Cell(nx-range);gx<=Cell(nx+range);gx++)
            for(var gy=Cell(ny-range);gy<=Cell(ny+range);gy++)
                if(hitGrid.TryGetValue((gx,gy),out var bucket))
                    foreach(var i in bucket)
                    {
                        var p=Normalized(points[i]); var dx=(p.X-nx)*scale; var dy=(p.Y-ny)*scale;
                        if(dx*dx+dy*dy>radius*radius)continue;
                        if(found>=0)return -2;
                        found=i;
                    }
        return found;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        PointRequested?.Invoke(HitTestPoint(e.GetPosition(this))); e.Handled=true;
    }

    private static int Cell(double value) => (int)Math.Floor(Math.Clamp(value,0,1)*(GridSize-1));

    protected override AutomationPeer OnCreateAutomationPeer() => new PreviewPeer(this);
    private sealed class PreviewPeer(PointMeasurePreview owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(PointMeasurePreview);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
    }
    private Point Normalized(Point2 p)=>new((p.X-minX)/span,(p.Y-minY)/span);
    private void DrawLabel(DrawingContext dc,string label,double x,double y,double width,double height)
    {
        var text=new FormattedText(label,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),12,TextBrush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text,new Point(Math.Clamp(x,2,Math.Max(2,width-text.Width-2)),Math.Clamp(y,2,Math.Max(2,height-text.Height-2))));
    }
    private static Brush FrozenBrush(byte r,byte g,byte b)
    {
        var brush=new SolidColorBrush(Color.FromRgb(r,g,b));brush.Freeze();return brush;
    }
}
