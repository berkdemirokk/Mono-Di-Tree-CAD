using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Read-only photograph and selected measurement endpoints.</summary>
internal sealed class PhotoMeasurementPreview : FrameworkElement
{
    private readonly BitmapSource image;
    private readonly List<Point2> points;
    private readonly int sourceWidth,sourceHeight;
    private double zoom=1,centerX=.5,centerY=.5;
    internal double Zoom=>zoom;
    internal int? FirstIndex { get; private set; }
    internal int? SecondIndex { get; private set; }
    internal event Action<int,bool>? PointChosen;
    internal event Action<Point2>? ControlPointChosen;
    internal event Action<string>? PickMessage;
    internal bool AddControlPointMode { get; set; }

    internal PhotoMeasurementPreview(BitmapSource image,IReadOnlyList<Point2> points,int sourceWidth,int sourceHeight)
    {
        ArgumentNullException.ThrowIfNull(image);ArgumentNullException.ThrowIfNull(points);
        if(sourceWidth<=0 || sourceHeight<=0)throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        var snapshot=image.IsFrozen ? image : image.CloneCurrentValue();
        if(!snapshot.CanFreeze)throw new ArgumentException("Fotoğraf önizlemesi sabitlenemedi.",nameof(image));
        if(!snapshot.IsFrozen)snapshot.Freeze();
        this.image=snapshot;this.points=points.ToList();this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;
        Height=120;MinWidth=100;ClipToBounds=true;
        AutomationProperties.SetName(this,"Seçilen ölçü noktalarının fotoğraf üzerindeki görünümü");
        AutomationProperties.SetHelpText(this,"Sol tık birinci, sağ tık ikinci ucu seçer; fare tekerleği yakınlaştırır. Yakın noktalar ayırt edilemiyorsa listeleri kullanın. Görünüm noktaları değiştirmez.");
    }
    internal void Select(int? first,int? second)
    {
        FirstIndex=Valid(first) ? first : null;SecondIndex=Valid(second) ? second : null;InvalidateVisual();
    }
    private bool Valid(int? index)=>index is >=0 && index<points.Count;
    internal void AddControlPoint(Point2 point) { points.Add(point);InvalidateVisual(); }
    private Rect ImageBounds(Size viewport)
    {
        var margin=Math.Min(11,Math.Min(viewport.Width,viewport.Height)/4);
        var scale=Math.Min((viewport.Width-2*margin)/sourceWidth,(viewport.Height-2*margin)/sourceHeight);
        var width=sourceWidth*scale*zoom;var height=sourceHeight*scale*zoom;
        return new Rect(viewport.Width/2-centerX*width,viewport.Height/2-centerY*height,width,height);
    }
    internal Point MapPixel(Point2 p,Size viewport)
    {
        var bounds=ImageBounds(viewport);
        return new Point(bounds.Left+(p.X+.5)*bounds.Width/image.PixelWidth,
            bounds.Top+(p.Y+.5)*bounds.Height/image.PixelHeight);
    }
    internal bool PickAt(Point position,Size viewport,bool second)
    {
        if(!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(viewport.Width) ||
            !double.IsFinite(viewport.Height) || viewport.Width<=0 || viewport.Height<=0 ||
            position.X<0 || position.Y<0 || position.X>=viewport.Width || position.Y>=viewport.Height)
            return false;
        var nearest=double.PositiveInfinity;var next=double.PositiveInfinity;var index=-1;
        for(int i=0;i<points.Count;i++)
        {
            var point=MapPixel(points[i],viewport);
            if(point.X<0 || point.Y<0 || point.X>=viewport.Width || point.Y>=viewport.Height)continue;
            var dx=point.X-position.X;var dy=point.Y-position.Y;
            var distance=Math.Sqrt(dx*dx+dy*dy);
            if(distance<nearest) { next=nearest;nearest=distance;index=i; }
            else if(distance<next)next=distance;
        }
        if(index<0 || nearest>9) { PickMessage?.Invoke("Yakında nokta yok; bir işarete tıklayın veya listeden seçin.");return false; }
        if(next-nearest<3) { PickMessage?.Invoke("Yakın noktalar ayırt edilemiyor; ilgili ucu listeden seçin.");return false; }
        PointChosen?.Invoke(index,second);PickMessage?.Invoke($"{(second ? "2" : "1")}. uç seçildi; çizim değişmedi.");return true;
    }
    internal bool PickControlPoint(Point position,Size viewport)
    {
        if(!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) ||
            viewport.Width<=0 || viewport.Height<=0 || position.X<0 || position.Y<0 || position.X>=viewport.Width || position.Y>=viewport.Height)return false;
        var bounds=ImageBounds(viewport);
        if(bounds.Width<=0 || bounds.Height<=0)return false;
        var point=new Point2((position.X-bounds.Left)/bounds.Width*image.PixelWidth-.5,(position.Y-bounds.Top)/bounds.Height*image.PixelHeight-.5);
        if(!point.IsFinite || point.X<0 || point.Y<0 || point.X>image.PixelWidth-1 || point.Y>image.PixelHeight-1)
        { PickMessage?.Invoke("Kontrol noktası için fotoğrafın içinde bir piksel seçin.");return false; }
        ControlPointChosen?.Invoke(point);return true;
    }
    internal bool ZoomAt(Point position,Size viewport,double factor)
    {
        if(!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(viewport.Width) ||
            !double.IsFinite(viewport.Height) || viewport.Width<=0 || viewport.Height<=0 ||
            position.X<0 || position.Y<0 || position.X>=viewport.Width || position.Y>=viewport.Height ||
            !double.IsFinite(factor) || factor<=0)return false;
        var bounds=ImageBounds(viewport);var next=Math.Clamp(zoom*factor,1,8);
        if(next==1) { centerX=.5;centerY=.5; }
        else { centerX=Math.Clamp((position.X-bounds.Left)/bounds.Width,0,1);centerY=Math.Clamp((position.Y-bounds.Top)/bounds.Height,0,1); }
        zoom=next;InvalidateVisual();PickMessage?.Invoke($"Fotoğraf görünümü {zoom:G}×; ölçü ve çizim değişmedi.");return true;
    }
    internal void ZoomSelected(double factor)
    {
        if(RenderSize.Width<=0 || RenderSize.Height<=0 || !double.IsFinite(factor) || factor<=0)return;
        var index=FirstIndex ?? SecondIndex;
        if(index is not int i) { ZoomAt(new Point(RenderSize.Width/2,RenderSize.Height/2),RenderSize,factor);return; }
        zoom=Math.Clamp(zoom*factor,1,8);
        if(zoom==1) { centerX=.5;centerY=.5; }
        else { centerX=Math.Clamp((points[i].X+.5)/image.PixelWidth,0,1);centerY=Math.Clamp((points[i].Y+.5)/image.PixelHeight,0,1); }
        InvalidateVisual();PickMessage?.Invoke($"Fotoğraf görünümü {zoom:G}×; ölçü ve çizim değişmedi.");
    }
    internal void ResetZoom() { zoom=1;centerX=.5;centerY=.5;InvalidateVisual();PickMessage?.Invoke("Fotoğrafın tamamı gösteriliyor; ölçü ve çizim değişmedi."); }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    { base.OnMouseLeftButtonDown(e);if(AddControlPointMode)PickControlPoint(e.GetPosition(this),RenderSize);else PickAt(e.GetPosition(this),RenderSize,false);e.Handled=true; }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    { base.OnMouseRightButtonDown(e);PickAt(e.GetPosition(this),RenderSize,true);e.Handled=true; }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    { base.OnMouseWheel(e);if(e.Delta!=0)ZoomAt(e.GetPosition(this),RenderSize,e.Delta>0 ? 2 : .5);e.Handled=true; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(Brushes.WhiteSmoke,new Pen(Brushes.Gray,1),new Rect(RenderSize));
        if(RenderSize.Width<=0 || RenderSize.Height<=0)return;
        dc.DrawImage(image,ImageBounds(RenderSize));
        foreach(var point in points)
            dc.DrawEllipse(Brushes.Black,new Pen(Brushes.White,1),MapPixel(point,RenderSize),3,3);
        Point? a=FirstIndex is int first ? MapPixel(points[first],RenderSize) : null;
        Point? b=SecondIndex is int second ? MapPixel(points[second],RenderSize) : null;
        if(a is Point start && b is Point end)
        {
            dc.DrawLine(new Pen(Brushes.Black,4),start,end);
            dc.DrawLine(new Pen(Brushes.White,2),start,end);
        }
        void Mark(Point point,string text,Brush fill)
        {
            dc.DrawEllipse(fill,new Pen(Brushes.White,2),point,10,10);
            var label=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),11,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label,new Point(point.X-label.Width/2,point.Y-label.Height/2));
        }
        if(a is Point one && b is Point two && one==two)Mark(one,"1/2",Brushes.DarkSlateBlue);
        else { if(a is Point oneOnly)Mark(oneOnly,"1",Brushes.DarkBlue);if(b is Point twoOnly)Mark(twoOnly,"2",Brushes.DarkRed); }
    }
}
