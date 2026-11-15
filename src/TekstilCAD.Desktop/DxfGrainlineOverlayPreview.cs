using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

/// <summary>Shows two selected DXF geometries in one world-coordinate view; conveys no textile classification.</summary>
internal sealed class DxfGrainlineOverlayPreview : FrameworkElement
{
    private Point2[] contour=[];
    private Point2[] marker=[];
    private string message="Bir kontur ve açık çizgi seçin.";
    internal Guid? ContourId { get; private set; }
    internal Guid? MarkerId { get; private set; }
    internal DxfGrainlineOverlayPreview()
    {
        Focusable=true;ClipToBounds=true;MinHeight=90;
        AutomationProperties.SetName(this,"DXF kontur ve çizgi ortak konum önizlemesi");
        AutomationProperties.SetHelpText(this,"İki geometri aynı koordinat ve ölçekle gösterilir. Kontur koyu gri, seçili çizgi kırmızıdır. Bu görünüm işlev veya üretim doğrulaması değildir.");
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new OverlayPeer(this);
    private sealed class OverlayPeer(DxfGrainlineOverlayPreview element):FrameworkElementAutomationPeer(element)
    {
        protected override string GetClassNameCore()=>nameof(DxfGrainlineOverlayPreview);
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Custom;
    }
    internal void ShowPair(PatternPiece? selectedContour,PatternPiece? selectedMarker)
    {
        contour=[];marker=[];ContourId=null;MarkerId=null;
        if(selectedContour is null || selectedMarker is null)message="Bir kontur ve açık çizgi seçin.";
        else if(selectedContour.Points.Count<3 || selectedContour.Points.Count>DxfPiecePreview.MaximumPoints || selectedMarker.Points.Count!=2 ||
            selectedContour.Points.Any(p=>!p.IsFinite || Math.Abs(p.X)>TekstilCAD.Core.Geometry.MaxCoordinate || Math.Abs(p.Y)>TekstilCAD.Core.Geometry.MaxCoordinate) ||
            selectedMarker.Points.Any(p=>!p.IsFinite || Math.Abs(p.X)>TekstilCAD.Core.Geometry.MaxCoordinate || Math.Abs(p.Y)>TekstilCAD.Core.Geometry.MaxCoordinate))
            message="Birlikte görünüm bu geometriyi gösteremiyor; kısmi geometri çizilmedi.";
        else
        {
            contour=selectedContour.Points.ToArray();marker=selectedMarker.Points.ToArray();ContourId=selectedContour.Id;MarkerId=selectedMarker.Id;message="";
        }
        var summary=ContourId is Guid a && MarkerId is Guid b
            ? $"Ortak görünüm: kontur {a:D}, açık çizgi {b:D}. Çizgi başlangıcı X {MeasurementInput.Format(marker[0].X)}, Y {MeasurementInput.Format(marker[0].Y)}; bitişi X {MeasurementInput.Format(marker[1].X)}, Y {MeasurementInput.Format(marker[1].Y)} mm. Konum semantik doğrulaması değildir."
            : message;
        var old=AutomationProperties.GetName(this);AutomationProperties.SetName(this,summary);
        if(UIElementAutomationPeer.CreatePeerForElement(this) is { } peer)peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty,old,summary);
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(StudioTheme.Raised,null,new Rect(RenderSize));
        if(contour.Length==0 || marker.Length==0)
        {
            if(ActualWidth<20 || ActualHeight<20)return;
            var text=new FormattedText(message,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,StudioTheme.Muted,VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth=Math.Max(1,ActualWidth-16) };
            dc.DrawText(text,new Point(8,8));return;
        }
        if(ActualWidth<20 || ActualHeight<20)return;
        var all=contour.Concat(marker);double minX=all.Min(p=>p.X),maxX=all.Max(p=>p.X),minY=all.Min(p=>p.Y),maxY=all.Max(p=>p.Y);
        double scale=Math.Min((ActualWidth-20)/Math.Max(maxX-minX,1e-12),(ActualHeight-20)/Math.Max(maxY-minY,1e-12));
        Point Map(Point2 p)=>new(10+(p.X-minX)*scale,10+(p.Y-minY)*scale);
        var outline=new Pen(Brushes.DimGray,1.5);var line=new Pen(Brushes.Crimson,3);
        for(int i=0;i<contour.Length-1;i++)dc.DrawLine(outline,Map(contour[i]),Map(contour[i+1]));
        if(contour[^1]!=contour[0])dc.DrawLine(outline,Map(contour[^1]),Map(contour[0]));
        dc.DrawLine(line,Map(marker[0]),Map(marker[1]));
        dc.DrawEllipse(Brushes.RoyalBlue,null,Map(marker[0]),4,4);dc.DrawEllipse(Brushes.DarkGreen,null,Map(marker[1]),4,4);
    }
}
