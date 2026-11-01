using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Desktop;

/// <summary>Read-only comparison of explicitly selected notch triples.</summary>
internal sealed class SelectedNotchComparisonWindow : Window
{
    private readonly MainWindow workspace;
    private readonly ProjectDocument originalDocument,originalContent;
    private readonly PatternPiece source;
    private readonly DispatcherTimer staleTimer=new() { Interval=TimeSpan.FromMilliseconds(500) };
    private readonly ComboBox target=new() { MinWidth=260,HorizontalAlignment=HorizontalAlignment.Left,DisplayMemberPath="Label",IsTextSearchEnabled=true };
    private readonly TextBox[] indices=new TextBox[6];
    private readonly TextBox widthTolerance=new() { Text="0",MinWidth=100,HorizontalAlignment=HorizontalAlignment.Left };
    private readonly TextBox depthTolerance=new() { Text="0",MinWidth=100,HorizontalAlignment=HorizontalAlignment.Left };
    private readonly TextBlock result=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0),Focusable=true };
    internal Action<PointPickerWindow>? PointPickerOpenedForVerification { get; set; }
    private sealed record Choice(string Label,PatternPiece Piece,string SearchLabel);

    internal SelectedNotchComparisonWindow(MainWindow workspace,Window owner,PatternPiece source)
    {
        this.workspace=workspace;this.source=source.Clone();originalDocument=workspace.Document;originalContent=workspace.Document.Clone();
        staleTimer.Tick+=(_,_)=>InvalidateIfStale();
        Closed+=(_,_)=>staleTimer.Stop();
        Owner=owner;Title="Seçili çentik ölçülerini karşılaştır";Width=700;Height=740;MinWidth=430;MinHeight=400;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel { Margin=new Thickness(18) };
        var warning=new TextBlock { TextWrapping=TextWrapping.Wrap,Focusable=true,Margin=new Thickness(0,0,0,12),
            Text="Yalnız sizin iki konturda eşlediğiniz üç nokta karşılaştırılır. Dikiş payının doğru olduğu, diğer çentiklerin korunduğu veya üretime uygunluk doğrulanmaz. Kontur yönleri tersse aynı ağız ucunu aynı sırada seçin. Proje değiştirilmez." };
        AutomationProperties.SetName(warning,warning.Text);panel.Children.Add(warning);
        panel.Children.Add(new TextBlock { Text=$"Kaynak parça kimliği: {source.Id:D}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        panel.Children.Add(new TextBlock { Text="Karşılaştırılacak parça",Margin=new Thickness(0,4,0,4) });
        AutomationProperties.SetName(target,"Karşılaştırılacak parça ve kimliği");
        TextSearch.SetTextPath(target,nameof(Choice.SearchLabel));
        target.ItemsSource=workspace.Document.Pieces.Select(p=> {
            var safeName=CadCanvas.CanvasPieceLabel(p.Name);
            return new Choice($"{p.Id:D} · {safeName}",p.Clone(),safeName);
        }).ToArray();
        target.SelectedIndex=target.Items.Count>0?0:-1;target.SelectionChanged+=(_,_)=>InvalidateResult();panel.Children.Add(target);
        string[] labels=["Kaynak ağız başlangıcı indeksi (0 tabanlı)","Kaynak dip noktası indeksi (0 tabanlı)","Kaynak ağız bitişi indeksi (0 tabanlı)",
            "Karşılaştırma ağız başlangıcı indeksi (0 tabanlı)","Karşılaştırma dip noktası indeksi (0 tabanlı)","Karşılaştırma ağız bitişi indeksi (0 tabanlı)"];
        for(int i=0;i<indices.Length;i++)
        {
            panel.Children.Add(new TextBlock { Text=labels[i],Margin=new Thickness(0,7,0,3) });
            var field=i;
            var row=new WrapPanel();
            indices[i]=new TextBox { MinWidth=100,HorizontalAlignment=HorizontalAlignment.Left };
            AutomationProperties.SetName(indices[i],labels[i]);indices[i].TextChanged+=(_,_)=>InvalidateResult();row.Children.Add(indices[i]);
            var pick=new Button { Content="Konturdan seç…",Margin=new Thickness(8,0,0,0) };
            AutomationProperties.SetName(pick,labels[i]+" için konturdan seç");
            pick.Click+=(_,_)=>ChoosePoint(field);row.Children.Add(pick);panel.Children.Add(row);
        }
        panel.Children.Add(new TextBlock { Text="Ağız genişliğinde izin verilen mutlak değişim (mm)",Margin=new Thickness(0,9,0,3) });
        AutomationProperties.SetName(widthTolerance,"Ağız genişliği değişim toleransı, mm");widthTolerance.TextChanged+=(_,_)=>InvalidateResult();panel.Children.Add(widthTolerance);
        panel.Children.Add(new TextBlock { Text="Dip derinliğinde izin verilen mutlak değişim (mm)",Margin=new Thickness(0,9,0,3) });
        AutomationProperties.SetName(depthTolerance,"Dip derinliği değişim toleransı, mm");depthTolerance.TextChanged+=(_,_)=>InvalidateResult();panel.Children.Add(depthTolerance);
        var compare=new Button { Content="Seçili ölçüleri karşılaştır",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0) };
        compare.Click+=(_,_)=>Compare();panel.Children.Add(compare);
        AutomationProperties.SetName(result,"Seçili çentik ölçümü sonucu");AutomationProperties.SetLiveSetting(result,AutomationLiveSetting.Polite);
        panel.Children.Add(result);
        var close=new Button { Content="Kapat",IsCancel=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) };
        close.Click+=(_,_)=>Close();panel.Children.Add(close);
        Content=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    }
    private void ChoosePoint(int field)
    {
        try
        {
            if(!IsCurrent())throw new InvalidOperationException("Proje veya bekleyen çalışma değişti; pencereyi kapatıp yeniden açın.");
            if(target.SelectedItem is not Choice selectedTarget)throw new ArgumentException("Karşılaştırılacak parçayı seçin.");
            var id=field<3 ? source.Id : selectedTarget.Piece.Id;
            var live=workspace.Document.Pieces.FirstOrDefault(p=>p.Id==id)
                ?? throw new InvalidOperationException("Seçilen parça artık bulunamıyor.");
            var previousText=indices[field].Text;
            var initial=int.TryParse(previousText,NumberStyles.None,CultureInfo.InvariantCulture,out var n) ? n : -1;
            Guid? CurrentSource()=>ReferenceEquals(target.SelectedItem,selectedTarget) && IsCurrent() ? id : null;
            var picker=new PointPickerWindow(workspace,this,live,CurrentSource,initial);
            if(PointPickerOpenedForVerification is { } probe)picker.Loaded+=(_,_)=>probe(picker);
            if(picker.ShowDialog()!=true || picker.SelectedPointIndex is not int index)return;
            if(!IsCurrent() || !ReferenceEquals(target.SelectedItem,selectedTarget) || indices[field].Text!=previousText)
                throw new InvalidOperationException("Proje, parça seçimi veya nokta girişi değişti; seçim uygulanmadı.");
            indices[field].Text=index.ToString(CultureInfo.InvariantCulture);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException)
        {
            staleTimer.Stop();
            result.Text="Nokta seçilemedi: "+ex.Message;
            AutomationProperties.SetName(result,result.Text);
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(result) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }
    private void InvalidateResult()
    {
        staleTimer.Stop();
        if(result.Text.Length==0)return;
        result.Text="Giriş değişti; yeniden karşılaştırın.";
        AutomationProperties.SetName(result,result.Text);
        if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(result) is { } peer)
            peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private bool IsCurrent() => !workspace.HasPendingCanvasOrPropertyWork &&
        ReferenceEquals(workspace.Document,originalDocument) && DocumentSession.ContentEquals(workspace.Document,originalContent);
    private void InvalidateIfStale()
    {
        if(IsCurrent())return;
        staleTimer.Stop();
        result.Text="Proje veya bekleyen çalışma değişti; eski sonuç geçersiz. Pencereyi kapatıp yeniden açın.";
        AutomationProperties.SetName(result,result.Text);
        if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(result) is { } peer)
            peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private void Compare()
    {
        try
        {
            staleTimer.Stop();
            if(!IsCurrent())
                throw new InvalidOperationException("Proje veya bekleyen çalışma değişti; pencereyi kapatıp güncel parçalarla yeniden açın.");
            if(target.SelectedItem is not Choice choice)throw new ArgumentException("Karşılaştırılacak parçayı seçin.");
            var values=new int[6];
            for(int i=0;i<values.Length;i++)
                if(!int.TryParse(indices[i].Text,NumberStyles.None,CultureInfo.InvariantCulture,out values[i]))
                    throw new ArgumentException("Altı nokta indeksini sıfırdan başlayan tam sayı olarak girin.");
            if(!MeasurementInput.TryParseMillimeters(widthTolerance.Text,out var w) || !MeasurementInput.TryParseMillimeters(depthTolerance.Text,out var d))
                throw new ArgumentException("İki toleransı milimetre cinsinden girin.");
            var measurement=SelectedNotchComparison.Compare(source.Points,choice.Piece.Points,values[0],values[1],values[2],
                values[3],values[4],values[5],w,d);
            result.Text=(measurement.WithinTolerance?"Seçili iki ölçü tolerans içinde. ":"Seçili ölçüler toleransı aşıyor. ")+
                $"Kaynak ağız: {MeasurementInput.Format(measurement.SourceWidth)} mm, karşılaştırma ağız: {MeasurementInput.Format(measurement.CandidateWidth)} mm. "+
                $"Kaynak derinlik: {MeasurementInput.Format(measurement.SourceDepth)} mm, karşılaştırma derinlik: {MeasurementInput.Format(measurement.CandidateDepth)} mm. "+
                $"Kaynak: {source.Id:D}; karşılaştırma: {choice.Piece.Id:D}. Genişlik toleransı: {MeasurementInput.Format(w)} mm; derinlik toleransı: {MeasurementInput.Format(d)} mm. "+
                "Gösterilen ölçüler yaklaşıktır. Yalnız bu üç nokta karşılaştırıldı; dikiş payı, diğer çentikler ve üretim uygunluğu doğrulanmadı.";
            staleTimer.Start();
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException)
        { result.Text="Karşılaştırma yapılamadı: "+ex.Message; }
        AutomationProperties.SetName(result,result.Text);
        if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(result) is { } peer)
            peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
}
