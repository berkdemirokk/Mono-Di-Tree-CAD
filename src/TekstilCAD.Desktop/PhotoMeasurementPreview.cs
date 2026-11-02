using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Read-only photograph and selected measurement endpoints.</summary>
internal sealed class PhotoMeasurementPreview : FrameworkElement
{
    private readonly BitmapSource image;
    private readonly Point2[] points;
    private readonly int sourceWidth,sourceHeight;
    internal int? FirstIndex { get; private set; }
    internal int? SecondIndex { get; private set; }

    internal PhotoMeasurementPreview(BitmapSource image,IReadOnlyList<Point2> points,int sourceWidth,int sourceHeight)
    {
        ArgumentNullException.ThrowIfNull(image);ArgumentNullException.ThrowIfNull(points);
        if(sourceWidth<=0 || sourceHeight<=0)throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        var snapshot=image.IsFrozen ? image : image.CloneCurrentValue();
        if(!snapshot.CanFreeze)throw new ArgumentException("Fotoğraf önizlemesi sabitlenemedi.",nameof(image));
        if(!snapshot.IsFrozen)snapshot.Freeze();
        this.image=snapshot;this.points=points.ToArray();this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;
        Height=150;MinWidth=100;ClipToBounds=true;
        AutomationProperties.SetName(this,"Seçilen ölçü noktalarının fotoğraf üzerindeki görünümü");
        AutomationProperties.SetHelpText(this,"Bir ve iki işaretleri listelerde seçilen uçları gösterir. Görünüm yalnız inceleme içindir; noktaları değiştirmez.");
    }
    internal void Select(int? first,int? second)
    {
        FirstIndex=Valid(first) ? first : null;SecondIndex=Valid(second) ? second : null;InvalidateVisual();
    }
    private bool Valid(int? index)=>index is >=0 && index<points.Length;
    private Rect ImageBounds(Size viewport)
    {
        var margin=Math.Min(11,Math.Min(viewport.Width,viewport.Height)/4);
        var scale=Math.Min((viewport.Width-2*margin)/sourceWidth,(viewport.Height-2*margin)/sourceHeight);
        var width=sourceWidth*scale;var height=sourceHeight*scale;
        return new Rect((viewport.Width-width)/2,(viewport.Height-height)/2,width,height);
    }
    internal Point MapPixel(Point2 p,Size viewport)
    {
        var bounds=ImageBounds(viewport);
        return new Point(bounds.Left+(p.X+.5)*bounds.Width/image.PixelWidth,
            bounds.Top+(p.Y+.5)*bounds.Height/image.PixelHeight);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(Brushes.WhiteSmoke,new Pen(Brushes.Gray,1),new Rect(RenderSize));
        if(RenderSize.Width<=0 || RenderSize.Height<=0)return;
        dc.DrawImage(image,ImageBounds(RenderSize));
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
