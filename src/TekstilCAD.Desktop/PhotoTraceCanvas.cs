using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

/// <summary>
/// Manual point selection on a detached, already-oriented preview. Coordinates are
/// continuous pixel-centre coordinates (0..width-1, 0..height-1), with Y downward.
/// No image analysis, physical scale, or garment construction is performed here.
/// </summary>
internal sealed class PhotoTraceCanvas : FrameworkElement
{
    internal const int MaximumPoints=512;
    private readonly List<Point> points=[];
    private BitmapSource? image;
    private int sourceWidth,sourceHeight;
    private Point cursor;

    internal PhotoTraceCanvas()
    {
        Focusable=true;MinHeight=160;ClipToBounds=true;
        AutomationProperties.SetName(this,"Referans fotoğrafta elle nokta seçimi");
        AutomationProperties.SetHelpText(this,"Fotoğraf üzerinde tıklayın. Klavyeyle ok tuşları imleci taşır, Enter veya boşluk nokta ekler, Backspace son noktayı kaldırır. Çizim otomatik kalıp değildir.");
    }

    internal BitmapSource? Image
    {
        get=>image;
        set
        {
            if(ReferenceEquals(image,value))return;
            if(value is null)image=null;
            else
            {
                // Never retain mutable external bitmap ownership. Frozen sources
                // are safe to share; otherwise clone and freeze a snapshot.
                var snapshot=value.IsFrozen ? value : value.CloneCurrentValue();
                if(!snapshot.CanFreeze)throw new ArgumentException("Görsel güvenli şekilde sabitlenemedi.",nameof(value));
                if(!snapshot.IsFrozen)snapshot.Freeze();
                if(snapshot.PixelWidth<1 || snapshot.PixelHeight<1)throw new ArgumentException("Görsel boyutu geçersiz.",nameof(value));
                image=snapshot;
            }
            sourceWidth=image?.PixelWidth ?? 0;sourceHeight=image?.PixelHeight ?? 0;
            points.Clear();cursor=image is null ? default : new Point((image.PixelWidth-1)/2d,(image.PixelHeight-1)/2d);
            InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    internal void SetSourceAspect(int width,int height)
    {
        if(image is null || width<image.PixelWidth || height<image.PixelHeight || width>12_000 || height>12_000 || (long)width*height>24_000_000)
            throw new ArgumentOutOfRangeException(nameof(width),"Kaynak görsel boyutu geçersiz.");
        sourceWidth=width;sourceHeight=height;InvalidateVisual();
    }

    internal IReadOnlyList<Point> Points=>points.AsReadOnly();
    internal Point CursorForVerification=>cursor;
    internal event EventHandler? PointsChanged;

    internal bool TryAddPixelPoint(Point point)
    {
        if(image is null || points.Count>=MaximumPoints || !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            point.X<0 || point.Y<0 || point.X>image.PixelWidth-1 || point.Y>image.PixelHeight-1 ||
            (points.Count>0 && points.Count!=2 && points[^1]==point))return false;
        points.Add(point);cursor=point;InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }

    internal bool UndoPoint()
    {
        if(points.Count==0)return false;
        points.RemoveAt(points.Count-1);InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);return true;
    }
    internal void ClearPoints()
    {
        if(points.Count==0)return;
        points.Clear();InvalidateVisual();PointsChanged?.Invoke(this,EventArgs.Empty);
    }

    // Rectangle is the actual uniform-fit image, excluding letterbox regions.
    internal Rect ImageBounds(Size viewport)
    {
        if(image is null || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) || viewport.Width<=0 || viewport.Height<=0)return Rect.Empty;
        var scale=Math.Min(viewport.Width/sourceWidth,viewport.Height/sourceHeight);
        if(!double.IsFinite(scale) || scale<=0)return Rect.Empty;
        var width=sourceWidth*scale;var height=sourceHeight*scale;
        return new Rect((viewport.Width-width)/2,(viewport.Height-height)/2,width,height);
    }
    internal bool TryMapViewToPixel(Point viewPoint,Size viewport,out Point pixel)
    {
        pixel=default;
        if(image is null || !double.IsFinite(viewPoint.X) || !double.IsFinite(viewPoint.Y))return false;
        var bounds=ImageBounds(viewport);
        if(bounds.IsEmpty || viewPoint.X<bounds.Left || viewPoint.Y<bounds.Top || viewPoint.X>=bounds.Right || viewPoint.Y>=bounds.Bottom)return false;
        var scaleX=bounds.Width/image.PixelWidth;var scaleY=bounds.Height/image.PixelHeight;
        pixel=new Point(Math.Clamp((viewPoint.X-bounds.Left)/scaleX-0.5,0,image.PixelWidth-1),
                        Math.Clamp((viewPoint.Y-bounds.Top)/scaleY-0.5,0,image.PixelHeight-1));
        return true;
    }
    internal bool TryMapPixelToView(Point pixel,Size viewport,out Point viewPoint)
    {
        viewPoint=default;
        if(image is null || !double.IsFinite(pixel.X) || !double.IsFinite(pixel.Y) || pixel.X<0 || pixel.Y<0 || pixel.X>image.PixelWidth-1 || pixel.Y>image.PixelHeight-1)return false;
        var bounds=ImageBounds(viewport);if(bounds.IsEmpty)return false;
        var scaleX=bounds.Width/image.PixelWidth;var scaleY=bounds.Height/image.PixelHeight;
        viewPoint=new Point(bounds.Left+(pixel.X+0.5)*scaleX,bounds.Top+(pixel.Y+0.5)*scaleY);return true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);Focus();
        if(TryMapViewToPixel(e.GetPosition(this),RenderSize,out var pixel))
        { cursor=pixel;TryAddPixelPoint(pixel);InvalidateVisual();e.Handled=true; }
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e);InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e);InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);if(image is null)return;
        var step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0 ? 10d : 1d;
        switch(e.Key)
        {
            case Key.Left: cursor.X=Math.Max(0,cursor.X-step);break;
            case Key.Right: cursor.X=Math.Min(image.PixelWidth-1,cursor.X+step);break;
            case Key.Up: cursor.Y=Math.Max(0,cursor.Y-step);break;
            case Key.Down: cursor.Y=Math.Min(image.PixelHeight-1,cursor.Y+step);break;
            case Key.Enter: case Key.Space: TryAddPixelPoint(cursor);break;
            case Key.Back: UndoPoint();break;
            default:return;
        }
        InvalidateVisual();e.Handled=true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White,new Pen(DesignRequestWindow.BorderColor,1),new Rect(RenderSize));
        var bounds=ImageBounds(RenderSize);if(image is null || bounds.IsEmpty)return;
        dc.DrawImage(image,bounds);
        var stroke=new Pen(DesignRequestWindow.Accent,2);Point? previous=null;
        var dip=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for(var i=0;i<points.Count;i++)
        {
            if(!TryMapPixelToView(points[i],RenderSize,out var point))continue;
            if(previous is { } last && i!=2)dc.DrawLine(stroke,last,point);
            dc.DrawEllipse(DesignRequestWindow.Accent,new Pen(Brushes.White,1),point,7,7);
            var label=new FormattedText((i+1).ToString(CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,Brushes.White,dip);
            dc.DrawText(label,new Point(point.X-label.Width/2,point.Y-label.Height/2));previous=point;
        }
        if(IsKeyboardFocused && TryMapPixelToView(cursor,RenderSize,out var current))
        {
            var highlight=new Pen(Brushes.White,2);dc.DrawLine(highlight,new Point(current.X-8,current.Y),new Point(current.X+8,current.Y));
            dc.DrawLine(highlight,new Point(current.X,current.Y-8),new Point(current.X,current.Y+8));
        }
    }
}
