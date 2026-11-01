using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using TekstilCAD.Core;
using Geometry = TekstilCAD.Core.Geometry;

namespace TekstilCAD.Desktop;

/// <summary>Bounded, read-only visual for one already-imported millimetre contour.</summary>
internal sealed class DxfPiecePreview : FrameworkElement
{
    internal const int MaximumPoints=10_000;
    private Point2[] points=[];
    private bool closed;
    private int? selectedVertex;
    private string emptyMessage="Bir parça seçin.";
    internal Guid? DisplayedPieceId { get; private set; }
    internal int DisplayedPointCount => points.Length;
    internal string VisibleSummary { get; private set; }="Parça seçin.";

    internal DxfPiecePreview()
    {
        Focusable=true;ClipToBounds=true;MinHeight=70;
        AutomationProperties.SetName(this,"Seçili DXF parçası geometrisi");
        AutomationProperties.SetHelpText(this,"Seçili parça ekrana eşit oranda sığdırılır. Açık çizgi kesik, uçları üçgen ve daire; kapalı kontur düz çizgidir. Bu görünüm fiziksel ölçüm veya eksiksiz aktarım onayı değildir. Ölçü ve durum metnini ayrıca okuyun.");
    }
    protected override AutomationPeer OnCreateAutomationPeer()=>new PreviewPeer(this);
    private sealed class PreviewPeer(DxfPiecePreview element) : FrameworkElementAutomationPeer(element)
    {
        protected override string GetClassNameCore()=>nameof(DxfPiecePreview);
        protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Custom;
    }
    internal string ShowPiece(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(piece.Points is null || piece.Points.Count<2 || (piece.Closed && piece.Points.Count<3))
            return SetUnavailable("Parça noktaları görsel önizleme için yetersiz; geometri gösterilmedi.");
        if(piece.Points.Count>MaximumPoints)
            return SetUnavailable($"Bu parça {piece.Points.Count} nokta içeriyor; görsel önizleme sınırı {MaximumPoints}. Kısmi geometri gösterilmedi.");
        var snapshot=piece.Points.ToArray();
        if(snapshot.Any(p=>!p.IsFinite || Math.Abs(p.X)>Geometry.MaxCoordinate || Math.Abs(p.Y)>Geometry.MaxCoordinate))
            return SetUnavailable("Parça koordinatları görsel önizleme sınırları dışında; geometri gösterilmedi.");
        var bounds=Geometry.Bounds(snapshot);
        if(bounds.Width==0 && bounds.Height==0)return SetUnavailable("Parça noktaları aynı konumda; geometri gösterilmedi.");
        points=snapshot;closed=piece.Closed;selectedVertex=null;DisplayedPieceId=piece.Id;emptyMessage="";
        VisibleSummary=$"{(closed?"Kapalı":"Açık")} · {MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm";
        SetAccessibleSummary($"Seçili DXF parçası geometrisi. {VisibleSummary}. {snapshot.Length} nokta. Kimlik: {piece.Id:D}");InvalidateVisual();
        return $"{(closed?"Kapalı kontur":"Açık çizgi")} · {snapshot.Length} nokta · Kimlik: {piece.Id:D}\n"+
            $"Genişlik: {MeasurementInput.Format(bounds.Width)} mm · Yükseklik: {MeasurementInput.Format(bounds.Height)} mm\n"+
            $"Sınırlar: min X {MeasurementInput.Format(bounds.MinX)} mm · min Y {MeasurementInput.Format(bounds.MinY)} mm · max X {MeasurementInput.Format(bounds.MaxX)} mm · max Y {MeasurementInput.Format(bounds.MaxY)} mm\n"+
            (!closed ? $"Başlangıç: X {MeasurementInput.Format(snapshot[0].X)} mm · Y {MeasurementInput.Format(snapshot[0].Y)} mm\nBitiş: X {MeasurementInput.Format(snapshot[^1].X)} mm · Y {MeasurementInput.Format(snapshot[^1].Y)} mm\n" : "")+
            "Görünüm otomatik sığdırılır; gerçek koordinat yerleşimini göstermez ve fiziksel ölçüm veya eksiksiz aktarım doğrulaması değildir.";
    }
    internal void SelectVertex(int? index)
    {
        selectedVertex=index is int i && i>=0 && i<points.Length ? i : null;
        SetAccessibleSummary(selectedVertex is int selected ? $"Seçili DXF parçası. {VisibleSummary}. Köşe {selected}, X {points[selected].X:R}, Y {points[selected].Y:R} mm." : "Seçili DXF parçası. "+VisibleSummary);
        InvalidateVisual();
    }
    internal void Clear() { points=[];selectedVertex=null;DisplayedPieceId=null;closed=false;emptyMessage="Bir parça seçin.";VisibleSummary="Parça seçin.";SetAccessibleSummary("DXF parçası seçilmedi.");InvalidateVisual(); }
    private string SetUnavailable(string message) { points=[];selectedVertex=null;DisplayedPieceId=null;closed=false;emptyMessage=message;VisibleSummary="Görsel önizleme yok; ayrıntıları inceleyin.";SetAccessibleSummary("DXF görsel önizleme yok. "+message);InvalidateVisual();return message; }
    private void SetAccessibleSummary(string value)
    {
        var previous=AutomationProperties.GetName(this);
        if(previous==value)return;
        AutomationProperties.SetName(this,value);
        if(UIElementAutomationPeer.CreatePeerForElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty,previous,value);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(StudioTheme.Raised,null,new Rect(RenderSize));
        if(points.Length==0)
        {
            var text=new FormattedText(emptyMessage,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,StudioTheme.Muted,VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth=Math.Max(1,ActualWidth-16) };
            dc.DrawText(text,new Point(8,8));return;
        }
        if(ActualWidth<24 || ActualHeight<24)return;
        var bounds=Geometry.Bounds(points);var width=Math.Max(bounds.Width,1e-9);var height=Math.Max(bounds.Height,1e-9);
        var factor=Math.Min((ActualWidth-20)/width,(ActualHeight-20)/height);
        var ox=(ActualWidth-width*factor)/2;var oy=(ActualHeight-height*factor)/2;
        // The preview must match the CAD canvas, where model Y increases downwards.
        Point Map(Point2 p)=>new(ox+(p.X-bounds.MinX)*factor,oy+(p.Y-bounds.MinY)*factor);
        var path=new StreamGeometry();using(var context=path.Open())
        {
            context.BeginFigure(Map(points[0]),false,closed);
            for(var i=1;i<points.Length;i++)context.LineTo(Map(points[i]),true,false);
        }
        path.Freeze();
        var pen=new Pen(Brushes.DeepSkyBlue,1.8) { DashStyle=closed?DashStyles.Solid:DashStyles.Dash };
        dc.DrawGeometry(null,pen,path);
        if(selectedVertex is int selected && selected<points.Length)dc.DrawEllipse(null,new Pen(Brushes.Gold,2),Map(points[selected]),6,6);
        if(!closed)
        {
            var start=Map(points[0]);var end=Map(points[^1]);
            var triangle=new StreamGeometry();using(var context=triangle.Open()) { context.BeginFigure(new Point(start.X,start.Y-5),true,true);context.LineTo(new Point(start.X-5,start.Y+4),true,false);context.LineTo(new Point(start.X+5,start.Y+4),true,false); }
            triangle.Freeze();dc.DrawGeometry(Brushes.DeepSkyBlue,null,triangle);dc.DrawEllipse(null,new Pen(Brushes.DeepSkyBlue,1.5),end,5,5);
        }
    }
}
