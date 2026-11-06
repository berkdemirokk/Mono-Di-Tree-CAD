using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TekstilCAD.Desktop;

/// <summary>Detached visual context for one existing trace point; never edits the draft.</summary>
internal sealed class PhotoTracePointContextWindow : Window
{
    internal PointContextPreview PreviewForVerification { get; }
    internal TextBlock ExplanationForVerification { get; }
    internal CheckBox ClosingForVerification { get; } = new() { Content="Kapanışı göster",Margin=new Thickness(0,0,0,4) };
    internal PhotoTracePointContextWindow(Window owner,BitmapSource image,int sourceWidth,int sourceHeight,Point[] snapshot,int referenceCount,int selected)
    {
        if(selected<referenceCount || selected>=snapshot.Length)throw new ArgumentOutOfRangeException(nameof(selected));
        var contextPoints=(Point[])snapshot.Clone();
        Owner=owner;Title="Nokta bağlantılarını incele";Width=620;Height=540;MinWidth=420;MinHeight=380;WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        PreviewForVerification=new PointContextPreview(image,sourceWidth,sourceHeight,contextPoints,referenceCount,selected);
        var root=new DockPanel { Margin=new Thickness(12) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,6,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        Button Action(string title,string help,Action action) { var b=new Button { Content=title,Padding=new Thickness(9,5,9,5),Margin=new Thickness(0,0,6,4) };AutomationProperties.SetHelpText(b,help);b.Click+=(_,_)=>action();actions.Children.Add(b);return b; }
        Action("Yakınlaştır","Seçili noktanın çevresini büyütür; fotoğraf veya çizgi değişmez.",()=>PreviewForVerification.SetZoom(PreviewForVerification.Zoom*2));
        Action("Uzaklaştır","Görüntüyü uzaklaştırır; fotoğraf veya çizgi değişmez.",()=>PreviewForVerification.SetZoom(PreviewForVerification.Zoom/2));
        Action("Sığdır","Fotoğrafın tamamını gösterir.",()=>PreviewForVerification.SetZoom(1));Action("Kapat","İncelemeyi kapatır.",Close);
        var previous=selected>referenceCount ? $"Nokta {selected} → {selected+1}" : "Önceki kenar yok";
        var next=selected+1<contextPoints.Length ? $"Nokta {selected+1} → {selected+2}" : "Sonraki kenar yok";
        ExplanationForVerification=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) };
        AutomationProperties.SetLiveSetting(ExplanationForVerification,AutomationLiveSetting.Polite);
        var description=new StackPanel();description.Children.Add(ExplanationForVerification);description.Children.Add(ClosingForVerification);DockPanel.SetDock(description,Dock.Top);root.Children.Add(description);
        ClosingForVerification.IsEnabled=contextPoints.Length-referenceCount>=3;
        AutomationProperties.SetHelpText(ClosingForVerification,"İlk ve son çizgi noktası arasındaki varsayımsal kapanışı mor kesikli gösterir; çizgiyi kapatmaz, kalıp veya geometri onayı vermez.");
        void UpdateClosing()
        {
            var enabled=ClosingForVerification.IsChecked==true && ClosingForVerification.IsEnabled;PreviewForVerification.SetClosingPreview(enabled);
            var closing=!ClosingForVerification.IsEnabled ? "Kapanış önizlemesi için en az üç çizgi noktası gerekir." : !enabled ? "Kapanış kenarı gösterilmiyor." :
                contextPoints[referenceCount]==contextPoints[^1] ? "İlk ve son nokta aynı koordinatta; kapanış kenarı sıfır uzunluklu ve geçersiz." :
                $"Mor kesikli varsayımsal kapanış: Nokta {contextPoints.Length} → {referenceCount+1}.";
            ExplanationForVerification.Text=$"Seçili nokta {selected+1}. Mavi önceki: {previous}. Turuncu sonraki: {next}. {closing} Çizgi açık kalır. Açılış anının kopyasıdır; kendiliğinden güncellenmez. Doğruluk onayı değildir.";
            AutomationProperties.SetName(ExplanationForVerification,ExplanationForVerification.Text);
            if(UIElementAutomationPeer.CreatePeerForElement(ExplanationForVerification) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        ClosingForVerification.Checked+=(_,_)=>UpdateClosing();ClosingForVerification.Unchecked+=(_,_)=>UpdateClosing();UpdateClosing();
        root.Children.Add(PreviewForVerification);
    }
}

internal sealed class PointContextPreview : FrameworkElement
{
    private readonly BitmapSource image;private readonly Point[] points;private readonly int sourceWidth,sourceHeight,referenceCount,selected;
    internal double Zoom { get; private set; }=1;
    internal bool ClosingPreview { get; private set; }
    internal int? PreviousIndex=>selected>referenceCount ? selected-1 : null;
    internal int? NextIndex=>selected+1<points.Length ? selected+1 : null;
    internal int SelectedIndex=>selected;
    internal PointContextPreview(BitmapSource image,int sourceWidth,int sourceHeight,Point[] snapshot,int referenceCount,int selected)
    {
        if(!image.IsFrozen || sourceWidth<=0 || sourceHeight<=0 || referenceCount<0 || selected<referenceCount || selected>=snapshot.Length)throw new ArgumentException("Nokta bağlamı geçersiz.");
        this.image=image;this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;points=(Point[])snapshot.Clone();this.referenceCount=referenceCount;this.selected=selected;
        ClipToBounds=true;MinHeight=120;AutomationProperties.SetName(this,"Seçili nokta ve komşu kenarlarının fotoğraf önizlemesi");
        AutomationProperties.SetHelpText(this,"Mavi önceki, turuncu sonraki kenardır; isteğe bağlı mor kesikli kenar varsayımsal kapanıştır. Yazılı açıklamada nokta numaraları verilir.");
    }
    internal void SetZoom(double value) { if(!double.IsFinite(value))return;Zoom=Math.Clamp(value,1,8);InvalidateVisual(); }
    internal void SetClosingPreview(bool value) { ClosingPreview=value && points.Length-referenceCount>=3;InvalidateVisual(); }
    private Rect ImageRect(Size viewport)
    {
        if(viewport.Width<=0 || viewport.Height<=0)return Rect.Empty;
        var scale=Math.Min(viewport.Width/sourceWidth,viewport.Height/sourceHeight)*Zoom;var width=sourceWidth*scale;var height=sourceHeight*scale;
        var x=Zoom==1 ? (viewport.Width-width)/2 : viewport.Width/2-(points[selected].X+0.5)/image.PixelWidth*width;
        var y=Zoom==1 ? (viewport.Height-height)/2 : viewport.Height/2-(points[selected].Y+0.5)/image.PixelHeight*height;
        return new Rect(x,y,width,height);
    }
    internal Point MapPointForVerification(Point point,Size viewport)
    {
        var rect=ImageRect(viewport);return rect.IsEmpty ? default : new Point(rect.X+(point.X+0.5)/image.PixelWidth*rect.Width,rect.Y+(point.Y+0.5)/image.PixelHeight*rect.Height);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(Brushes.White,new Pen(StudioTheme.Border,1),new Rect(RenderSize));var rect=ImageRect(RenderSize);if(rect.IsEmpty)return;
        dc.DrawImage(image,rect);var center=MapPointForVerification(points[selected],RenderSize);var dip=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        void DrawNeighbor(int index,Brush color)
        {
            var p=MapPointForVerification(points[index],RenderSize);dc.DrawLine(new Pen(color,4),p,center);dc.DrawEllipse(color,new Pen(Brushes.White,1),p,6,6);
            var text=new FormattedText((index+1).ToString(CultureInfo.InvariantCulture),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.White,dip);
            var origin=new Point(p.X+7,p.Y-14);dc.DrawRectangle(color,null,new Rect(origin,new Size(text.Width+5,text.Height)));dc.DrawText(text,new Point(origin.X+2,origin.Y));
        }
        if(PreviousIndex is int previous)DrawNeighbor(previous,Brushes.DodgerBlue);if(NextIndex is int next)DrawNeighbor(next,Brushes.DarkOrange);
        if(ClosingPreview)
        {
            var first=MapPointForVerification(points[referenceCount],RenderSize);var last=MapPointForVerification(points[^1],RenderSize);
            var closurePen=new Pen(Brushes.MediumPurple,4) { DashStyle=DashStyles.Dash };
            if(points[referenceCount]==points[^1])dc.DrawEllipse(null,closurePen,first,13,13);else dc.DrawLine(closurePen,last,first);
        }
        dc.DrawEllipse(Brushes.Black,new Pen(Brushes.White,3),center,8,8);
    }
}
