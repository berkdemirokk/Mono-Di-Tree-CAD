using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using TekstilCAD.Core;
using Geometry = TekstilCAD.Core.Geometry;

namespace TekstilCAD.Desktop;

/// <summary>
/// Edits a detached snapshot only. A true dialog result returns Result (null
/// means remove); the owner must recheck its source and commit transactionally.
/// </summary>
internal sealed class GrainlineEditorWindow : Window
{
    private readonly Func<string?> checkCurrent;
    private readonly GrainlinePreview preview;
    internal TextBox StartX { get; } = NewInput();
    internal TextBox StartY { get; } = NewInput();
    internal TextBox EndX { get; } = NewInput();
    internal TextBox EndY { get; } = NewInput();
    internal CheckBox Reviewed { get; } = new() { Content="Bu kontur ile çizginin ilişkisini gözden geçirdim",Margin=new Thickness(0,8,0,4) };
    internal TextBlock Feedback { get; } = new() { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,8,0,0) };
    internal Button SaveButton { get; }
    internal Button RemoveButton { get; }
    internal Grainline? Result { get; private set; }

    internal GrainlineEditorWindow(Window owner,PatternPiece snapshot,Func<string?> checkCurrent)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(snapshot);
        this.checkCurrent=checkCurrent ?? throw new ArgumentNullException(nameof(checkCurrent));
        var source=snapshot.Clone();
        Owner=owner;Title="İplik yönü · Mono Di Tree CAD";Width=590;Height=700;MinWidth=430;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(16) };Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,10,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var cancel=new Button { Content="Vazgeç",IsCancel=true,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,4) };
        cancel.Click+=(_,_)=>DialogResult=false;actions.Children.Add(cancel);
        RemoveButton=new Button { Content="İplik yönünü kaldır",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,4),IsEnabled=source.Grainline is not null };
        RemoveButton.Click+=(_,_)=>RequestRemove();actions.Children.Add(RemoveButton);
        SaveButton=new Button { Content="İplik yönünü kaydet",IsDefault=true,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,0,4) };
        SaveButton.Click+=(_,_)=>RequestSave();actions.Children.Add(SaveButton);

        var body=new StackPanel();
        var scroll=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        AutomationProperties.SetName(scroll,"İplik yönü düzenleme alanı");root.Children.Add(scroll);
        body.Children.Add(new TextBlock { Text="İplik yönü",FontSize=20,FontWeight=FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text="Parça: "+SafeName(source.Name),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0) });
        var identity=new TextBox { Text=source.Id.ToString("D"),IsReadOnly=true,Margin=new Thickness(0,4,0,8) };
        AutomationProperties.SetName(identity,"İplik yönü düzenlenen parçanın tam kimliği");body.Children.Add(identity);
        body.Children.Add(new TextBlock { Text="Çizginin iki ucunu mutlak X/Y koordinatlarıyla milimetre olarak girin. Pozitif Y ana çalışma alanındaki gibi aşağı gösterilir.",TextWrapping=TextWrapping.Wrap });
        AddInput(body,"Başlangıç X (mm)",StartX);AddInput(body,"Başlangıç Y (mm)",StartY);
        AddInput(body,"Bitiş X (mm)",EndX);AddInput(body,"Bitiş Y (mm)",EndY);
        AutomationProperties.SetName(Reviewed,"İplik yönü ile kontur ilişkisi gözden geçirildi");
        AutomationProperties.SetHelpText(Reviewed,"Bu yalnızca kullanıcı inceleme durumudur; üretim uygunluğu veya makine uyumluluğu belgesi değildir.");
        body.Children.Add(Reviewed);
        body.Children.Add(new TextBlock { Text="Bu çizgi tek başına kumaş yönünün, pastal yerleşiminin veya fiziksel makine uyumluluğunun doğrulandığı anlamına gelmez.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,6,0,0) });
        preview=new GrainlinePreview(source);body.Children.Add(preview);
        AutomationProperties.SetName(Feedback,"İplik yönü düzenleme durumu");
        AutomationProperties.SetLiveSetting(Feedback,AutomationLiveSetting.Polite);body.Children.Add(Feedback);

        if(source.Grainline is { } existing)
        {
            StartX.Text=MeasurementInput.Format(existing.Start.X);StartY.Text=MeasurementInput.Format(existing.Start.Y);
            EndX.Text=MeasurementInput.Format(existing.End.X);EndY.Text=MeasurementInput.Format(existing.End.Y);
            Reviewed.IsChecked=!existing.NeedsReview;
        }
        else Reviewed.IsChecked=false;
        // A prior review applies to the exact values the user reviewed. Even a
        // temporary edit that is later undone requires an explicit new review.
        // Handlers are attached after loading the existing values above.
        foreach(var input in new[] { StartX,StartY,EndX,EndY })input.TextChanged+=(_,_)=>
        {
            Reviewed.IsChecked=false;
            RefreshPreview();
            SetFeedback("Koordinatlar değişti. Konturla ilişkiyi yeniden inceleyin; uygunsa inceleme kutusunu tekrar işaretleyin.");
        };
        Reviewed.Checked+=(_,_)=>RefreshPreview();Reviewed.Unchecked+=(_,_)=>RefreshPreview();
        Loaded+=(_,_)=>StartX.Focus();RefreshPreview();
    }

    private static TextBox NewInput()=>new() { MinWidth=140,MaxLength=128,Padding=new Thickness(7),Margin=new Thickness(0,3,0,5) };
    private static void AddInput(Panel panel,string text,TextBox input)
    {
        panel.Children.Add(new Label { Content=text,Target=input,Padding=new Thickness(0),Margin=new Thickness(0,6,0,0) });
        AutomationProperties.SetName(input,text);panel.Children.Add(input);
    }
    private static string SafeName(string? name)
    {
        if(string.IsNullOrEmpty(name))return "(adsız)";
        var length=Math.Min(name.Length,80);
        if(length<name.Length && length>0 && char.IsHighSurrogate(name[length-1]))length--;
        var result=new StringBuilder();
        foreach(var c in name.AsSpan(0,length))
        {
            var category=char.GetUnicodeCategory(c);
            if(char.IsControl(c) || category is UnicodeCategory.Format or UnicodeCategory.Surrogate)
                result.Append("\\u").Append(((int)c).ToString("X4",CultureInfo.InvariantCulture));
            else result.Append(c);
        }
        if(length<name.Length)result.Append('…');return result.ToString();
    }
    private void SetFeedback(string message)
    {
        Feedback.Text=message;AutomationProperties.SetName(Feedback,message);
        if(UIElementAutomationPeer.CreatePeerForElement(Feedback) is { } peer)peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    internal bool TryBuildCandidate(out Grainline? candidate,out string error)
    {
        candidate=null;error="";
        if(!MeasurementInput.TryParseMillimeters(StartX.Text,Geometry.MaxCoordinate,out var sx) ||
           !MeasurementInput.TryParseMillimeters(StartY.Text,Geometry.MaxCoordinate,out var sy) ||
           !MeasurementInput.TryParseMillimeters(EndX.Text,Geometry.MaxCoordinate,out var ex) ||
           !MeasurementInput.TryParseMillimeters(EndY.Text,Geometry.MaxCoordinate,out var ey))
        { error="Dört koordinat için çalışma sınırları içinde geçerli milimetre değerleri girin.";return false; }
        var start=new Point2(sx,sy);var end=new Point2(ex,ey);
        if(start==end) { error="İplik yönünün başlangıç ve bitiş noktaları farklı olmalıdır.";return false; }
        candidate=new Grainline { Start=start,End=end,NeedsReview=Reviewed.IsChecked!=true };return true;
    }
    private void RefreshPreview()
    {
        if(TryBuildCandidate(out var candidate,out _))preview.SetLine(candidate);
        else preview.SetLine(null);
        Feedback.Text="";
        AutomationProperties.SetName(Feedback,"İplik yönü düzenleme durumu");
    }
    private string? SourceProblem()
    {
        try { return checkCurrent(); }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        { return "Kaynak durumu doğrulanamadı. Çalışma alanını kontrol edin: "+ex.Message; }
    }
    internal void RequestSave()
    {
        if(!TryBuildCandidate(out var candidate,out var error)) { SetFeedback(error);return; }
        if(SourceProblem() is { } problem) { SetFeedback(problem);return; }
        Result=candidate;DialogResult=true;
    }
    internal void RequestRemove()
    {
        if(SourceProblem() is { } problem) { SetFeedback(problem);return; }
        Result=null;DialogResult=true;
    }

    /// <summary>Snapshot-only illustration; not a physical scale or topology validator.</summary>
    private sealed class GrainlinePreview : FrameworkElement
    {
        private readonly Point2[] points;
        private readonly bool closed;
        private Grainline? line;
        internal GrainlinePreview(PatternPiece piece)
        {
            points=piece.Points.ToArray();closed=piece.Closed;Height=180;MinHeight=150;Margin=new Thickness(0,10,0,0);ClipToBounds=true;
            AutomationProperties.SetName(this,"Kontur ve iplik yönü önizlemesi");
            AutomationProperties.SetHelpText(this,"Kontur ve girilen iplik yönü çizgisi aynı görünümde gösterilir; fiziksel ölçü değildir. Koordinatlar üstteki alanlardadır.");
        }
        internal void SetLine(Grainline? value) { line=value?.Clone();InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);var width=ActualWidth;var height=ActualHeight;if(width<=0 || height<=0)return;
            dc.DrawRectangle(StudioTheme.Raised,null,new Rect(0,0,width,height));
            var all=line is null ? points : points.Concat([line.Start,line.End]).ToArray();
            if(all.Length==0 || all.Any(p=>!p.IsFinite))return;
            var minX=all.Min(p=>p.X);var maxX=all.Max(p=>p.X);var minY=all.Min(p=>p.Y);var maxY=all.Max(p=>p.Y);
            var span=Math.Max(maxX-minX,maxY-minY);if(!double.IsFinite(span))return;if(span==0)span=1;
            const double pad=16;var scale=Math.Max(0,Math.Min(width-2*pad,height-2*pad));if(scale<=0)return;
            var x=(width-scale*(maxX-minX)/span)/2;var y=(height-scale*(maxY-minY)/span)/2;
            Point Screen(Point2 p)=>new(x+(p.X-minX)/span*scale,y+(p.Y-minY)/span*scale);
            if(points.Length>1)
            {
                var shape=new StreamGeometry();using(var ctx=shape.Open())
                { ctx.BeginFigure(Screen(points[0]),false,closed);for(var i=1;i<points.Length;i++)ctx.LineTo(Screen(points[i]),true,false); }
                shape.Freeze();dc.DrawGeometry(null,new Pen(StudioTheme.Muted,1.5),shape);
            }
            if(line is not null)
            {
                var a=Screen(line.Start);var b=Screen(line.End);dc.DrawLine(new Pen(StudioTheme.Accent,2.5),a,b);
                dc.DrawEllipse(StudioTheme.Accent,null,a,3,3);dc.DrawEllipse(StudioTheme.Warning,null,b,3,3);
            }
        }
    }
}
