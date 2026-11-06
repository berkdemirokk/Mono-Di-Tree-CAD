using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using TekstilCAD.FileFormats;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TekstilCAD.Core;

namespace TekstilCAD.Desktop;

internal enum PhotoSourcePurpose { Unspecified, GarmentReference, FlatPattern }

/// <summary>Manual photo tracing; the result is an unapproved open draft, not a pattern.</summary>
internal sealed class PhotoTraceWindow : Window
{
    private readonly MainWindow workspace;
    private readonly MainWindow.PhotoTraceStamp stamp;
    private readonly Func<bool>? sourceCurrent;
    private readonly BitmapSource image;
    private readonly int sourceWidth,sourceHeight;
    private readonly string? sourceSha256;
    private readonly int sourceByteLength;
    private readonly int sourceOrientation;
    private readonly TextBox xInput=new() { Width=60,MaxLength=24 },yInput=new() { Width=60,MaxLength=24 };
    internal (int Width,int Height) SourceDimensions => (sourceWidth,sourceHeight);
    internal long DraftRevision { get; private set; }
    internal bool HasDraft => PlaneSettings is not null || CanvasForVerification.Points.Count>0 || LengthForVerification.Text.Length>0 || xInput.Text.Length>0 || yInput.Text.Length>0;
    internal PhotoTraceCanvas CanvasForVerification { get; } = new();
    internal TextBox LengthForVerification { get; } = new() { Width=85,MaxLength=32 };
    internal TextBlock StatusForVerification { get; } = new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3) };
    internal CheckBox ReviewClosureForVerification { get; } = new() { Content="Kapatmayı incele",Foreground=DesignRequestWindow.Ink,Margin=new Thickness(4,3,0,2) };
    internal CheckBox ColorPathForVerification { get; } = new() { Content="Renkle ara",Foreground=DesignRequestWindow.Ink,Margin=new Thickness(4,3,0,2) };
    internal CheckBox PhotoOnlyForVerification { get; } = new() { Content="Çizimi gizle",Foreground=DesignRequestWindow.Ink,Margin=new Thickness(4,3,0,2) };
    internal CheckBox WidePathForVerification { get; } = new() { Content="Geniş ara",Foreground=DesignRequestWindow.Ink,Margin=new Thickness(4,3,0,2) };
    internal ComboBox SourcePurposeForVerification { get; } = new() { MinWidth=165,Height=32,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(6,0,6,0),Margin=new Thickness(6,0,0,4) };
    internal PhotoSourcePurpose SourcePurpose { get; private set; }
    internal Button ApplyForVerification { get; private set; } = null!;
    internal Button RegionForVerification { get; private set; } = null!;
    internal FrameworkElement ReferenceInputsForVerification { get; private set; } = null!;
    internal Func<string,bool>? ConfirmForVerification { get; set; }
    internal Action<Guid>? OpenClosureForVerification { get; set; }
    internal Func<BitmapSource,Point,Point,CancellationToken,IReadOnlyList<Point>?>? ComputePathForVerification { get; set; }
    internal Action<CancellationToken>? BeforeTopologyReviewForVerification { get; set; }
    internal (double Width,double Height)? PlaneSettings { get; private set; }
    private readonly Label referenceLabel=new() { Foreground=DesignRequestWindow.Ink };
    private bool allowClose,closed;
    private CancellationTokenSource? pathCancellation;
    private CancellationTokenSource? sessionCancellation;

    internal PhotoTraceWindow(MainWindow workspace,BitmapSource bitmap,MainWindow.PhotoTraceStamp stamp,int sourceWidth=0,int sourceHeight=0,string? sourceSha256=null,int sourceByteLength=0,int sourceOrientation=1,Func<bool>? sourceCurrent=null,Window? windowOwner=null)
    {
        this.workspace=workspace;this.stamp=stamp;this.sourceCurrent=sourceCurrent;image=bitmap;this.sourceWidth=sourceWidth==0 ? bitmap.PixelWidth : sourceWidth;this.sourceHeight=sourceHeight==0 ? bitmap.PixelHeight : sourceHeight;this.sourceSha256=sourceSha256;this.sourceByteLength=sourceByteLength;this.sourceOrientation=sourceOrientation;Owner=windowOwner ?? workspace;Title="Fotoğrafta elle taslak çiz";Width=820;Height=760;MinWidth=560;MinHeight=480;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("DesignRequestTheme.xaml",UriKind.Relative) });Background=DesignRequestWindow.BackgroundColor;Foreground=DesignRequestWindow.Ink;
        var root=new DockPanel { Margin=new Thickness(12) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,5,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        Button Action(string title,Action act) { var b=new Button { Content=title,Padding=new Thickness(9,5,9,5),Margin=new Thickness(0,0,6,4) };b.Click+=(_,_)=>act();actions.Children.Add(b);return b; }
        var undoPoint=Action("Son noktayı kaldır",()=>CanvasForVerification.UndoPoint());var clearPoints=Action("Noktaları sil",()=>CanvasForVerification.ClearPoints());
        var apply=Action("CAD'e taslak ekle",TryApply);apply.Background=DesignRequestWindow.Accent;apply.Foreground=DesignRequestWindow.Surface;
        ApplyForVerification=apply;
        AutomationProperties.SetHelpText(apply,"Seçilen ve açıkça kabul edilen noktalarla açık çizgiyi CAD'e ekler; kalıp ve üretim uygunluğu doğrulanmaz.");
        var traceMenu=new ContextMenu();
        var saveTrace=new MenuItem { Header="Çizgiyi kaydet…" };saveTrace.Click+=(_,_)=>_ = SaveTraceDialogAsync();traceMenu.Items.Add(saveTrace);
        var loadTrace=new MenuItem { Header="Çizgiyi aç…" };loadTrace.Click+=(_,_)=>_ = LoadTraceDialogAsync();traceMenu.Items.Add(loadTrace);
        var cancelTrace=new MenuItem { Header="Dosya işlemini iptal et" };cancelTrace.Click+=(_,_)=> { sessionCancellation?.Cancel();StatusForVerification.Text="İptal istendi; tamamlanmış kayıt geri alınmaz."; };traceMenu.Items.Add(cancelTrace);
        var traceFiles=Action("⋯",()=> { traceMenu.IsOpen=true; });actions.Children.Remove(traceFiles);traceFiles.Width=25;traceFiles.Padding=new Thickness(0);traceFiles.Margin=new Thickness(1,0,0,4);traceMenu.PlacementTarget=traceFiles;AutomationProperties.SetName(traceFiles,"Çizgi dosyası");
        AutomationProperties.SetHelpText(traceFiles,"Referans ve çizgi noktalarını kaydedin veya aynı fotoğrafa ait taslağı açın. Fotoğraf ve bölge maskesi dosyaya dahil değildir.");
        Action("Kapat",Close);
        var region=Action("Bölge…",OpenRegionSuggestion);RegionForVerification=region;
        AutomationProperties.SetHelpText(region,"Dikdörtgen içinden deneysel renk bölgesi önerir. Giysi fotoğrafında yalnız inceleme yapılır; düz kalıp görselinde sınır ayrıca önizlenebilir. Üretim doğruluğu göstermez.");
        var sourceRow=new WrapPanel { Margin=new Thickness(0,0,0,4) };
        sourceRow.Children.Add(new Label { Content="Görsel türü",Target=SourcePurposeForVerification,Foreground=DesignRequestWindow.Ink,Padding=new Thickness(0,4,0,4) });
        SourcePurposeForVerification.ItemsSource=new[] { "Giysi/model referansı", "Düz serilmiş kalıp görseli" };
        AutomationProperties.SetName(SourcePurposeForVerification,"Görsel türü seçimi");
        AutomationProperties.SetHelpText(SourcePurposeForVerification,"Görsel otomatik sınıflandırılmaz. Giysi/model yalnız görsel referanstır; düz serilmiş kalıp seçilirse ölçek ve çizim araçları açılır.");
        sourceRow.Children.Add(SourcePurposeForVerification);sourceRow.Children.Add(ColorPathForVerification);sourceRow.Children.Add(WidePathForVerification);sourceRow.Children.Add(PhotoOnlyForVerification);sourceRow.Children.Add(traceFiles);DockPanel.SetDock(sourceRow,Dock.Top);root.Children.Add(sourceRow);
        var input=new WrapPanel();DockPanel.SetDock(input,Dock.Top);root.Children.Add(input);
        ReferenceInputsForVerification=input;
        referenceLabel.Content="İki uç arası bilinen mesafe (mm)";referenceLabel.Target=LengthForVerification;input.Children.Add(referenceLabel);AutomationProperties.SetName(LengthForVerification,"Bilinen referans mesafesi milimetre");input.Children.Add(LengthForVerification);
        input.Children.Add(new Label { Content="Piksel X",Target=xInput,Foreground=DesignRequestWindow.Ink });AutomationProperties.SetName(xInput,"Seçilecek piksel X");input.Children.Add(xInput);
        input.Children.Add(new Label { Content="Y",Target=yInput,Foreground=DesignRequestWindow.Ink });AutomationProperties.SetName(yInput,"Seçilecek piksel Y");input.Children.Add(yInput);
        var addPoint=new Button { Content="Nokta ekle",Padding=new Thickness(8,4,8,4),Margin=new Thickness(6,0,0,4) };input.Children.Add(addPoint);
        addPoint.Click+=(_,_)=> { if(!MeasurementInput.TryParseMillimeters(xInput.Text,out var x) || !MeasurementInput.TryParseMillimeters(yInput.Text,out var y) || !CanvasForVerification.TryAddPixelPoint(new Point(x,y)))StatusForVerification.Text="Piksel koordinatı geçersiz veya nokta eklenemedi."; };
        var editPoint=new Button { Content="Düzelt…",Padding=new Thickness(8,4,8,4),Margin=new Thickness(6,0,0,4) };input.Children.Add(editPoint);
        AutomationProperties.SetName(editPoint,"Çizgi noktasını düzelt");AutomationProperties.SetHelpText(editPoint,"İnceleme imlecine en yakın çizgi noktası ilk seçilir; fotoğrafa odaklanıldığında sarı halkayla gösterilir. Piksel koordinatları veya inceleme imleciyle taşıyabilirsiniz; referanslar değişmez.");
        editPoint.Click+=(_,_)=> { var dialog=CreatePointEditor();if(dialog is not null)dialog.ShowDialog(); };
        var checkMeasure=new Button { Content="Ölçü karşılaştır…",Padding=new Thickness(8,4,8,4),Margin=new Thickness(6,0,0,4) };input.Children.Add(checkMeasure);
        AutomationProperties.SetHelpText(checkMeasure,"Taslak noktaları arasındaki fotoğraf mesafesini ayrı bilinen bir ölçüyle karşılaştırır; proje değişmez ve sonuç kaydedilmez.");
        checkMeasure.Click+=(_,_)=>CreateMeasurementCheck()?.ShowDialog();
        var planeSetup=new Button { Content="Düzlem ayarı…",Padding=new Thickness(8,4,8,4),Margin=new Thickness(6,0,0,4) };input.Children.Add(planeSetup);
        AutomationProperties.SetHelpText(planeSetup,"Noktalar temizken dört köşeli düzlem referansını veya tek mesafe modunu seçer.");
        planeSetup.Click+=(_,_)=>OpenPlaneSetup();
        var allPaths=new Button { Content="Tüm çizgiyi öner",Padding=new Thickness(8,4,8,4),Margin=new Thickness(6,0,0,4) };input.Children.Add(allPaths);
        AutomationProperties.SetHelpText(allPaths,"En fazla 17 çizgi noktasının ardışık aralıklarını birlikte arar. Kapatmayı incele seçiliyken son noktadan ilk noktaya da yol önerir; kapalı parça ayrı onay gerektirir. Doku veya gölgeyi izleyebilir.");
        allPaths.Click+=(_,_)=>_ = StartPathSuggestionAsync(allSegments:true);
        var viewActions=new WrapPanel { Margin=new Thickness(0,2,0,2) };DockPanel.SetDock(viewActions,Dock.Top);root.Children.Add(viewActions);
        Button ViewAction(string text,string help,Action action) { var button=new Button { Content=text,Padding=new Thickness(8,3,8,3),Margin=new Thickness(0,0,6,2) };AutomationProperties.SetHelpText(button,help);button.Click+=(_,_)=> { action();UpdateStatus(); };viewActions.Children.Add(button);return button; }
        ViewAction("Yakınlaştır","Son seçilen, sağ tıklanan veya klavye imlecindeki noktaya yakınlaştırır.",()=>CanvasForVerification.ZoomAtCursor(2));
        ViewAction("Uzaklaştır","Son seçilen veya klavye imlecindeki noktadan uzaklaştırır.",()=>CanvasForVerification.ZoomAtCursor(0.5));
        ViewAction("Görüntüyü sığdır","Fotoğrafın tamamını pencereye sığdırır.",CanvasForVerification.ResetZoom);
        var suggest=new Button { Content="Kenar öner",Padding=new Thickness(8,3,8,3),Margin=new Thickness(0,0,6,2) };viewActions.Children.Add(suggest);
        AutomationProperties.SetHelpText(suggest,"Son çizgi noktasına yakın görüntü kontrastını arar; mavi halka bir öneridir ve nokta değişmez. Kumaş kenarı olduğu doğrulanmaz.");
        suggest.Click+=(_,_)=> { StatusForVerification.Text=CanvasForVerification.SuggestLastEdge() && CanvasForVerification.EdgeSuggestion is Point p
            ? $"Mavi halka önerisi: X {MeasurementInput.Format(p.X)}, Y {MeasurementInput.Format(p.Y)}. Görüntü kontrastı kumaş kenarı olmayabilir; uygula seçilene kadar nokta değişmez."
            : "Son çizgi noktasının yakınında yeterli kontrast bulunamadı; nokta değişmedi."; };
        var applySuggestion=new Button { Content="Öneriyi uygula",Padding=new Thickness(8,3,8,3),Margin=new Thickness(0,0,6,2) };viewActions.Children.Add(applySuggestion);
        AutomationProperties.SetHelpText(applySuggestion,"Mavi halka önerisini son çizgi noktasına uygular; referans uçlarını değiştirmez.");
        applySuggestion.Click+=(_,_)=> { if(!CanvasForVerification.ApplyEdgeSuggestion())StatusForVerification.Text="Uygulanabilir yeni kenar önerisi yok; nokta değişmedi."; };
        var pathActions=new WrapPanel { Margin=new Thickness(0,2,0,2) };DockPanel.SetDock(pathActions,Dock.Top);root.Children.Add(pathActions);
        Button PathAction(string text,string help,Action action) { var b=new Button { Content=text,Padding=new Thickness(8,3,8,3),Margin=new Thickness(0,0,6,2) };AutomationProperties.SetHelpText(b,help);b.Click+=(_,_)=>action();pathActions.Children.Add(b);return b; }
        PathAction("Yol öner","Son iki çizgi noktası arasında kontrast yolunu arar; kesikli mavi çizgi yalnız önizlemedir.",()=>_ = StartPathSuggestionAsync());
        PathAction("Yolu uygula","Kesikli öneriyi seçilen aralığa uygular; önceden seçilen uçları korur.",()=> { if(pathCancellation is not null) { StatusForVerification.Text="Yol araması ve piksel denetiminin bitmesini bekleyin.";return; } if(!CanvasForVerification.ApplyPathSuggestion())StatusForVerification.Text="Uygulanabilir yol önerisi yok."; });
        PathAction("Geri al","Son uygulanan yolu, kenar önerisini veya nokta düzenlemesini tek adımda geri alır; başka nokta değişikliği yapılmamış olmalıdır.",()=> { if(!CanvasForVerification.UndoPath())StatusForVerification.Text="Geri alınacak son yol veya nokta düzenlemesi yok."; });
        PathAction("Denetle","Geçerli çizgiyi veya varsa yol önerisini piksel geometrisinde denetler; kapanış seçiliyse son kenarı da kontrol eder.",()=>_ = StartTraceTopologyReviewAsync());
        PathAction("İptal et","Süren yol araması veya piksel denetimine iptal isteği gönderir.",CancelPathSuggestion);
        AutomationProperties.SetHelpText(ReviewClosureForVerification,"Seçiliyken toplu yol önerisi kapanış yolunu da arar; taslak eklendikten sonra ayrı kapanış incelemesi açılır. Kapalı parça otomatik eklenmez; fotoğraf geçmişi açık kaynakta kalır.");pathActions.Children.Add(ReviewClosureForVerification);
        AutomationProperties.SetHelpText(PhotoOnlyForVerification,"Fotoğrafı çizgi, nokta, öneri ve uyarı işaretleri olmadan gösterir; bu görünümde çizim ve uygulama araçları kapalıdır. Taslak ve öneri korunur.");
        AutomationProperties.SetName(ColorPathForVerification,"Renk kontrastı ile yol");AutomationProperties.SetHelpText(ColorPathForVerification,"Seçiliyken yol önerisi gri parlaklık yerine renk kanallarının kontrastını kullanır. Renk, desen veya gölge giysi sınırı olmayabilir; öneriyi kontrol edin. Kenar önerisi düğmesini değiştirmez.");
        ColorPathForVerification.ToolTip="Yol için renk kontrastı kullanır; desen veya gölge izlenebilir. Sonucu inceleyin.";
        AutomationProperties.SetName(WidePathForVerification,"Geniş arama alanıyla yol öner");AutomationProperties.SetHelpText(WidePathForVerification,"Uçların çevresindeki arama payını 16 yerine 32 piksel yapar. İş sınırı aşılırsa standart alana döner ve durum bildirir; farklı bir kenarı izleyebilir.");
        WidePathForVerification.ToolTip="32 piksel pay kullanır; iş sınırında standart alana döner. Farklı bir kenarı izleyebilir.";
        AutomationProperties.SetName(StatusForVerification,"Fotoğraf taslağı durumu");AutomationProperties.SetLiveSetting(StatusForVerification,AutomationLiveSetting.Polite);
        var statusViewport=new ScrollViewer { Content=StatusForVerification,MaxHeight=36,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        AutomationProperties.SetName(statusViewport,"Fotoğraf taslağı durum metni");AutomationProperties.SetHelpText(statusViewport,"Uzun durum metnini yön tuşlarıyla kaydırın.");
        DockPanel.SetDock(statusViewport,Dock.Top);root.Children.Add(statusViewport);
        CanvasForVerification.Image=image;CanvasForVerification.SetSourceAspect(this.sourceWidth,this.sourceHeight);
        CanvasForVerification.PointsChanged+=(_,_)=> { DraftRevision++;UpdateStatus(); };
        LengthForVerification.TextChanged+=(_,_)=>DraftRevision++;xInput.TextChanged+=(_,_)=>DraftRevision++;yInput.TextChanged+=(_,_)=>DraftRevision++;
        void ClosureChoiceChanged() { DraftRevision++;CanvasForVerification.ClearPathSuggestion();allPaths.Content=ReviewClosureForVerification.IsChecked==true ? "Çizgi + kapanış öner" : "Tüm çizgiyi öner"; }
        ReviewClosureForVerification.Checked+=(_,_)=>ClosureChoiceChanged();ReviewClosureForVerification.Unchecked+=(_,_)=>ClosureChoiceChanged();
        void ColorChoiceChanged()
        {
            DraftRevision++;pathCancellation?.Cancel();
            if(CanvasForVerification.PathSuggestion is not null)CanvasForVerification.ClearPathSuggestion();
            if(CanvasForVerification.HasTopologyWarning) { UpdateStatus();StatusForVerification.Text+=" Yol kontrastı değişti; çizgi uyarısı korunuyor."; }
            else StatusForVerification.Text="Yol kontrastı değişti; önceki yol önerisi kaldırıldı. Yeni öneriyi inceleyin.";
        }
        ColorPathForVerification.Checked+=(_,_)=>ColorChoiceChanged();ColorPathForVerification.Unchecked+=(_,_)=>ColorChoiceChanged();
        void WideChoiceChanged()
        {
            DraftRevision++;pathCancellation?.Cancel();
            if(CanvasForVerification.PathSuggestion is not null)CanvasForVerification.ClearPathSuggestion();
            if(CanvasForVerification.HasTopologyWarning) { UpdateStatus();StatusForVerification.Text+=" Yol alanı değişti; çizgi uyarısı korunuyor."; }
            else StatusForVerification.Text="Yol arama alanı değişti; önceki yol önerisi kaldırıldı. Yeni öneriyi inceleyin.";
        }
        WidePathForVerification.Checked+=(_,_)=>WideChoiceChanged();WidePathForVerification.Unchecked+=(_,_)=>WideChoiceChanged();
        void UpdatePhotoOnly()
        {
            CanvasForVerification.PhotoOnly=PhotoOnlyForVerification.IsChecked==true;
            CanvasForVerification.IsEnabled=SourcePurpose==PhotoSourcePurpose.FlatPattern && !CanvasForVerification.PhotoOnly;
            var editing=!CanvasForVerification.PhotoOnly;
            input.IsEnabled=editing;pathActions.IsEnabled=editing;suggest.IsEnabled=editing;applySuggestion.IsEnabled=editing;
            undoPoint.IsEnabled=editing;clearPoints.IsEnabled=editing;apply.IsEnabled=editing;region.IsEnabled=SourcePurpose==PhotoSourcePurpose.GarmentReference || editing;
        }
        PhotoOnlyForVerification.Checked+=(_,_)=>UpdatePhotoOnly();PhotoOnlyForVerification.Unchecked+=(_,_)=>UpdatePhotoOnly();
        void UpdatePurpose()
        {
            var flat=SourcePurpose==PhotoSourcePurpose.FlatPattern;
            Title=flat ? "Fotoğrafta elle taslak çiz" : "Referans fotoğrafı";
            var visible=flat ? Visibility.Visible : Visibility.Collapsed;
            input.Visibility=visible;pathActions.Visibility=visible;ColorPathForVerification.Visibility=visible;WidePathForVerification.Visibility=visible;PhotoOnlyForVerification.Visibility=visible;suggest.Visibility=visible;applySuggestion.Visibility=visible;
            undoPoint.Visibility=visible;clearPoints.Visibility=visible;apply.Visibility=visible;traceFiles.Visibility=visible;region.Visibility=SourcePurpose==PhotoSourcePurpose.Unspecified ? Visibility.Collapsed : Visibility.Visible;
            region.Content=flat ? "Bölge…" : "Giysi bölgesini incele…";UpdatePhotoOnly();
            UpdateStatus();
        }
        SourcePurposeForVerification.SelectionChanged+=(_,_)=>
        {
            var selected=SourcePurposeForVerification.SelectedIndex switch { 0=>PhotoSourcePurpose.GarmentReference,1=>PhotoSourcePurpose.FlatPattern,_=>PhotoSourcePurpose.Unspecified };
            if(selected==SourcePurpose)return;
            SourcePurpose=selected;DraftRevision++;pathCancellation?.Cancel();UpdatePurpose();
        };
        UpdatePurpose();root.Children.Add(CanvasForVerification);
        Closing+=OnClosing;
        workspace.RegisterPhotoTrace(this);Closed+=(_,_)=> { closed=true;pathCancellation?.Cancel();sessionCancellation?.Cancel();workspace.UnregisterPhotoTrace(this); };
    }
    private bool CanUseTraceSession()
    {
        if(closed || SourcePurpose!=PhotoSourcePurpose.FlatPattern || sessionCancellation is not null || pathCancellation is not null || sourceSha256 is null || sourceByteLength<=0)
        { if(!closed)StatusForVerification.Text="Düz kalıp görseli seçili olmalı; kaynak fotoğraf kimliği bulunmalı ve süren işlem bitmeli.";return false; }
        return true;
    }
    private PhotoTraceSession.Data CaptureTraceSession()=>new(1,sourceSha256!,sourceByteLength,sourceOrientation,sourceWidth,sourceHeight,image.PixelWidth,image.PixelHeight,
        PlaneSettings?.Width,PlaneSettings?.Height,LengthForVerification.Text,xInput.Text,yInput.Text,ReviewClosureForVerification.IsChecked==true,ColorPathForVerification.IsChecked==true,WidePathForVerification.IsChecked==true,
        CanvasForVerification.Points.Select(p=>new PhotoTraceSession.Pixel(p.X,p.Y)).ToArray());
    private async Task SaveTraceDialogAsync()
    {
        if(!CanUseTraceSession())return;var revision=DraftRevision;
        var dialog=new SaveFileDialog { Filter="Fotoğraf çizgisi (*.tdtrace)|*.tdtrace",DefaultExt=".tdtrace",AddExtension=true,OverwritePrompt=false };
        if(dialog.ShowDialog(this)!=true)return;
        if(!CanUseTraceSession() || revision!=DraftRevision) { StatusForVerification.Text="Taslak değişti; dosya kaydedilmedi.";return; }
        await TrySaveTraceSessionAsync(dialog.FileName);
    }
    private async Task LoadTraceDialogAsync()
    {
        if(!CanUseTraceSession())return;var revision=DraftRevision;
        var dialog=new OpenFileDialog { Filter="Fotoğraf çizgisi (*.tdtrace)|*.tdtrace",CheckFileExists=true };
        if(dialog.ShowDialog(this)!=true)return;
        if(!CanUseTraceSession() || revision!=DraftRevision) { StatusForVerification.Text="Taslak değişti; dosya açılmadı.";return; }
        await TryLoadTraceSessionAsync(dialog.FileName);
    }
    internal async Task<bool> TrySaveTraceSessionAsync(string path)
    {
        if(!CanUseTraceSession())return false;var data=CaptureTraceSession();var revision=DraftRevision;var cts=new CancellationTokenSource();sessionCancellation=cts;
        try {
            var full=PhotoTraceSession.SavePath(path);StatusForVerification.Text="Çizgi dosyası kaydediliyor…";
            await Task.Run(()=> { var text=PhotoTraceSession.Encode(data);ProjectStorage.AtomicWriteIfUnchanged(full,text,new ProjectFileVersion(full,null),cts.Token); },cts.Token);
            if(!closed)StatusForVerification.Text=revision==DraftRevision ? "Çizgi yeni dosyaya kaydedildi; fotoğraf ve bölge maskesi dahil değil." : "Önceki çizgi durumu kaydedildi; sonraki değişiklikler dosyada yok.";
            return true;
        } catch(OperationCanceledException) { if(!closed)StatusForVerification.Text="Kayıt iptal edildi; tamamlanmış kayıt geri alınmaz.";return false; }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException or JsonException)
        { if(!closed)StatusForVerification.Text="Çizgi kaydedilemedi: "+ex.Message;return false; }
        finally { if(ReferenceEquals(sessionCancellation,cts))sessionCancellation=null;cts.Dispose(); }
    }
    internal async Task<bool> TryLoadTraceSessionAsync(string path)
    {
        if(!CanUseTraceSession())return false;var revision=DraftRevision;
        if(HasDraft) { var accepted=ConfirmForVerification?.Invoke("Çizgi dosyası mevcut taslağın yerini alacak. Devam edilsin mi?") ?? (MessageBox.Show(this,"Çizgi dosyası mevcut taslağın yerini alacak. Devam edilsin mi?","Çizgiyi aç",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes);
            if(!accepted || revision!=DraftRevision || !CanUseTraceSession()) { if(!closed)StatusForVerification.Text="Çizgi açılmadı; mevcut taslak korundu.";return false; } }
        var cts=new CancellationTokenSource();sessionCancellation=cts;
        try {
            StatusForVerification.Text="Çizgi dosyası açılıyor; mevcut taslak korunuyor…";
            var data=await Task.Run(()=>PhotoTraceSession.Decode(path,cts.Token),cts.Token);cts.Token.ThrowIfCancellationRequested();
            if(closed || revision!=DraftRevision || SourcePurpose!=PhotoSourcePurpose.FlatPattern) { if(!closed)StatusForVerification.Text="Taslak değişti; dosya uygulanmadı.";return false; }
            if(!string.Equals(data.Sha256,sourceSha256,StringComparison.OrdinalIgnoreCase) || data.ByteLength!=sourceByteLength || data.Orientation!=sourceOrientation || data.SourceWidth!=sourceWidth || data.SourceHeight!=sourceHeight || data.PreviewWidth!=image.PixelWidth || data.PreviewHeight!=image.PixelHeight)
                throw new InvalidDataException("Dosya bu fotoğrafa veya önizlemeye ait değil.");
            CanvasForVerification.ClearPoints();SetPlaneSettings(data.PlaneWidth is double w ? (w,data.PlaneHeight!.Value) : null);
            LengthForVerification.Text=data.Length;xInput.Text=data.XInput;yInput.Text=data.YInput;
            ReviewClosureForVerification.IsChecked=data.Closure;ColorPathForVerification.IsChecked=data.Color;WidePathForVerification.IsChecked=data.Wide;
            foreach(var point in data.Points)if(!CanvasForVerification.TryAddPixelPoint(new Point(point.X,point.Y)))throw new InvalidOperationException("Doğrulanmış nokta yüklenemedi.");
            StatusForVerification.Text="Çizgi aynı fotoğrafa yüklendi; fotoğraf ve bölge maskesi dosyada yok. Taslak ve ölçüler doğrulanmadı."+(data.Version==1 ? " Eski dosyanın içerik özeti yok." : " İçerik özeti eşleşti; dijital imza değildir.");return true;
        } catch(OperationCanceledException) { if(!closed)StatusForVerification.Text="Açma iptal edildi; mevcut taslak korundu.";return false; }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException or JsonException)
        { if(!closed)StatusForVerification.Text="Çizgi açılamadı: "+ex.Message;return false; }
        finally { if(ReferenceEquals(sessionCancellation,cts))sessionCancellation=null;cts.Dispose(); }
    }
    internal PhotoTracePointEditWindow? CreatePointEditor()
    {
        if(SourcePurpose!=PhotoSourcePurpose.FlatPattern) { StatusForVerification.Text="Nokta düzenlemek için düz serilmiş kalıp görselini seçin.";return null; }
        if(CanvasForVerification.Points.Count<=CanvasForVerification.ReferencePointCount) { StatusForVerification.Text="Düzeltmek için önce bir çizgi noktası seçin.";return null; }
        return new PhotoTracePointEditWindow(this);
    }
    internal PhotoRegionWindow? CreateRegionWindow()
    {
        if(SourcePurpose==PhotoSourcePurpose.Unspecified) { StatusForVerification.Text="Önce görsel türünü seçin.";return null; }
        if(pathCancellation is not null) { StatusForVerification.Text="Önce süren yol araması veya denetimi bitirin.";return null; }
        if(SourcePurpose==PhotoSourcePurpose.FlatPattern && CanvasForVerification.Points.Count!=CanvasForVerification.ReferencePointCount)
        { StatusForVerification.Text="Önce referansları seçin; bölge önerisi için çizgi noktaları boş olmalı.";return null; }
        if(image.PixelWidth<3 || image.PixelHeight<3)
        { StatusForVerification.Text="Bölge önerisi için görsel en az 3 × 3 piksel olmalı.";return null; }
        return new PhotoRegionWindow(this,image,reviewOnly:SourcePurpose==PhotoSourcePurpose.GarmentReference);
    }
    private void OpenRegionSuggestion()
    {
        var revision=DraftRevision;var purpose=SourcePurpose;var dialog=CreateRegionWindow();if(dialog is null)return;
        if(purpose==PhotoSourcePurpose.GarmentReference)
        {
            dialog.ShowDialog();
            if(!closed && revision==DraftRevision && SourcePurpose==purpose)StatusForVerification.Text="Giysi bölgesi yalnız incelendi; ölçü, kalıp veya CAD değişikliği yapılmadı.";
            return;
        }
        if(dialog.ShowDialog()!=true || dialog.SelectedContour is not { } contour)return;
        if(closed || revision!=DraftRevision || !CanvasForVerification.SetRegionContourSuggestion(contour))
        { StatusForVerification.Text="Taslak değişti veya sınır geçersiz; öneri kullanılmadı.";return; }
        var approximationNote=dialog.SelectedContourMaximumDeviation is double deviation ? $" Sadeleştirilmiş: kaynak noktalarının karşılık gelen kenara en büyük uzaklığı yaklaşık {deviation:0.###} önizleme pikseli; kayan nokta hesabı yaklaşık, kesin üst sınır değil. İnce ayrıntılar kaybolabilir, fiziksel hata ölçülmedi." : "";
        StatusForVerification.Text="Mavi sınır yalnız öneridir. Diğer bölgeler ve boşluklar aktarılmaz; inceleyip Yolu uygula seçin. Taslak açık kalır."+approximationNote;
        _=StartTraceTopologyReviewAsync(approximationNote);
    }
    private void OpenPlaneSetup()
    {
        if(SourcePurpose!=PhotoSourcePurpose.FlatPattern) { StatusForVerification.Text="Düzlem ayarı için düz serilmiş kalıp görselini seçin.";return; }
        if(CanvasForVerification.Points.Count!=0) { StatusForVerification.Text="Önce noktaları temizleyin; referans türü mevcut noktalara yeniden yorumlanmaz.";return; }
        var revision=DraftRevision;var dialog=new PhotoPlaneSetupWindow(this,PlaneSettings?.Width,PlaneSettings?.Height);
        if(dialog.ShowDialog()!=true)return;
        if(DraftRevision!=revision || CanvasForVerification.Points.Count!=0) { StatusForVerification.Text="Taslak değişti; referans ayarı uygulanmadı.";return; }
        SetPlaneSettings(dialog.UsePlane ? (dialog.WidthMm,dialog.HeightMm) : null);
    }
    internal void SetPlaneSettings((double Width,double Height)? value)
    {
        if(SourcePurpose!=PhotoSourcePurpose.FlatPattern)throw new InvalidOperationException("Düzlem ayarı için düz serilmiş kalıp görselini seçin.");
        if(CanvasForVerification.Points.Count!=0)throw new InvalidOperationException("Referans türü için önce noktaları temizleyin.");
        if(value is null && PlaneSettings is null) { UpdateStatus();return; }
        if(value is { } p && (!double.IsFinite(p.Width) || !double.IsFinite(p.Height) || p.Width<=0 || p.Height<=0 || p.Width>Geometry.MaxCoordinate || p.Height>Geometry.MaxCoordinate))
            throw new ArgumentOutOfRangeException(nameof(value));
        CanvasForVerification.SetReferencePointCount(value is null ? 2 : 4);PlaneSettings=value;
        LengthForVerification.IsReadOnly=value is not null;LengthForVerification.Text=value is { } dimensions ? MeasurementInput.Format(dimensions.Width) : "";
        referenceLabel.Content=value is null ? "İki uç arası bilinen mesafe (mm)" : "Düzlem referans eni (mm)";
        AutomationProperties.SetName(LengthForVerification,value is null ? "Bilinen referans mesafesi milimetre" : "Düzlem referans eni milimetre, ayarlarda değiştirilir");
        DraftRevision++;UpdateStatus();
    }
    internal PhotoTraceCheckWindow? CreateMeasurementCheck()
    {
        if(SourcePurpose!=PhotoSourcePurpose.FlatPattern) { StatusForVerification.Text="Ölçekli ölçüm için düz serilmiş kalıp görselini seçin.";return null; }
        try
        {
            var points=CanvasForVerification.Points.ToArray();
            var referenceCount=CanvasForVerification.ReferencePointCount;
            if(points.Length<referenceCount+1) { StatusForVerification.Text="Ölçü karşılaştırmak için referans noktalarını ve en az bir başka nokta seçin.";return null; }
            if(!MeasurementInput.TryParseMillimeters(LengthForVerification.Text,out var mm)) { StatusForVerification.Text="Önce bilinen referans mesafesini girin.";return null; }
            var a=new Point2(points[0].X,points[0].Y);var b=new Point2(points[1].X,points[1].Y);
            PhotoPlaneCalibration? plane=null;
            if(PlaneSettings is { } settings)
            {
                if(referenceCount!=4 || BitConverter.DoubleToInt64Bits(mm)!=BitConverter.DoubleToInt64Bits(settings.Width))throw new InvalidOperationException("Düzlem eni ayarla uyuşmuyor.");
                plane=new PhotoPlaneCalibration { Corners=points.Take(4).Select(q=>new Point2(q.X,q.Y)).ToList(),WidthMm=settings.Width,HeightMm=settings.Height };
                _=PhotoPlaneRectification.Convert(image.PixelWidth,image.PixelHeight,plane.Corners,plane.WidthMm,plane.HeightMm,[a,b]);
            }
            else _=PhotoTraceCalibration.Convert(image.PixelWidth,image.PixelHeight,sourceWidth,sourceHeight,a,b,mm,[a,b]);
            return new PhotoTraceCheckWindow(this,image,sourceWidth,sourceHeight,mm,points,plane,sourceSha256,sourceByteLength,sourceOrientation);
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException)
        { StatusForVerification.Text="Ölçü karşılaştırması açılamadı: "+ex.Message;return null; }
    }
    internal void CancelPathSuggestion()=>pathCancellation?.Cancel();
    private Task<(bool Valid,Point[]? Edges)> AnalyzePixelTopologyAsync(Point[] snapshot,bool closure,CancellationToken token)
    {
        var pixelPoints=snapshot.Select(p=>new Point2(p.X,p.Y)).ToArray();
        return Task.Run(()=>
        {
            BeforeTopologyReviewForVerification?.Invoke(token);
            try { LargeOpenPathValidator.Validate(pixelPoints,token);if(closure)LargeContourValidator.Validate(pixelPoints,token);return (Valid:true,Edges:(Point[]?)null); }
            catch(ArgumentException) { return (Valid:false,Edges:PhotoPathCrossingDiagnostic.Find(snapshot,closure,token)); }
        },token);
    }
    private async Task ReviewProposedTopologyAsync(long revision,CancellationToken token)
    {
        var snapshot=CanvasForVerification.ProposedTraceSnapshot();if(snapshot is null)return;
        var closure=ReviewClosureForVerification.IsChecked==true || CanvasForVerification.ProposedPathHasClosingEdge;
        var result=await AnalyzePixelTopologyAsync(snapshot,closure,token);
        token.ThrowIfCancellationRequested();if(closed || DraftRevision!=revision || (ReviewClosureForVerification.IsChecked==true || CanvasForVerification.ProposedPathHasClosingEdge)!=closure)return;
        var current=CanvasForVerification.ProposedTraceSnapshot();if(current is null || !current.SequenceEqual(snapshot))return;
        CanvasForVerification.SetTopologyWarning(snapshot,result.Edges,!result.Valid);
        StatusForVerification.Text+=result.Valid
            ? " Piksel geometrisi: sorun bulunmadı. Fotoğraf kenarı ve üretim doğrulanmadı."
            : result.Edges is not null ? " Piksel geometrisi uyarısı: turuncu kenarlar kesişiyor veya temas ediyor; inceleyin."
            : " Piksel geometrisi uyarısı: çakışma, sıfır kenar veya kapanış sorunu olabilir; öneriyi düzeltin.";
    }
    internal async Task StartTraceTopologyReviewAsync(string approximationNote="")
    {
        if(SourcePurpose!=PhotoSourcePurpose.FlatPattern) { StatusForVerification.Text="Çizim denetimi için düz serilmiş kalıp görselini seçin.";return; }
        if(closed)return;
        if(pathCancellation is not null) { StatusForVerification.Text="Yol araması veya denetim zaten sürüyor.";return; }
        var proposal=CanvasForVerification.ProposedTraceSnapshot();var usesProposal=proposal is not null;
        var snapshot=proposal ?? CanvasForVerification.Points.Skip(CanvasForVerification.ReferencePointCount).ToArray();
        if(snapshot.Length<2) { StatusForVerification.Text="Denetlemek için en az iki çizgi noktası seçin.";return; }
        var closure=ReviewClosureForVerification.IsChecked==true || CanvasForVerification.ProposedPathHasClosingEdge;var revision=DraftRevision;var cts=new CancellationTokenSource();pathCancellation=cts;
        StatusForVerification.Text=(usesProposal ? "Yol önerisi piksel geometrisinde denetleniyor." : "Çizgi piksel geometrisinde denetleniyor.")+approximationNote;
        try
        {
            var result=await AnalyzePixelTopologyAsync(snapshot,closure,cts.Token);
            cts.Token.ThrowIfCancellationRequested();if(closed || DraftRevision!=revision || (ReviewClosureForVerification.IsChecked==true || CanvasForVerification.ProposedPathHasClosingEdge)!=closure)return;
            var currentProposal=CanvasForVerification.ProposedTraceSnapshot();
            if((currentProposal is not null)!=usesProposal || !(currentProposal ?? CanvasForVerification.Points.Skip(CanvasForVerification.ReferencePointCount).ToArray()).SequenceEqual(snapshot))return;
            if(usesProposal)CanvasForVerification.SetTopologyWarning(snapshot,result.Edges,!result.Valid);else CanvasForVerification.SetCurrentTopologyWarning(snapshot,result.Edges,!result.Valid);
            var prefix=usesProposal ? "Yol önerisi" : "Çizgi";
            StatusForVerification.Text=result.Valid ? $"{prefix}: piksel geometrisinde sorun bulunmadı. Fotoğraf kenarı ve üretim doğrulanmadı."
                : result.Edges is not null ? $"{prefix}: piksel geometrisi uyarısı; turuncu kenarlar kesişiyor veya temas ediyor."
                : $"{prefix}: piksel geometrisi uyarısı; çakışma, sıfır kenar veya kapanış sorunu olabilir.";
            if(closure)StatusForVerification.Text+=" Kapanış kenarı önizlemedir; taslak açık kalır.";
            StatusForVerification.Text+=approximationNote;
        }
        catch(OperationCanceledException) { if(!closed && DraftRevision==revision)StatusForVerification.Text="Piksel denetimi iptal edildi; çizgi değişmedi."; }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException)
        { if(!closed && DraftRevision==revision)StatusForVerification.Text="Piksel denetimi yapılamadı: "+ex.Message; }
        finally { if(ReferenceEquals(pathCancellation,cts))pathCancellation=null;cts.Dispose(); }
    }
    internal async Task StartReplacementPathAsync(int first,int last,long expectedRevision)
    {
        if(SourcePurpose!=PhotoSourcePurpose.FlatPattern) { StatusForVerification.Text="Yol önerisi için düz serilmiş kalıp görselini seçin.";return; }
        if(closed)return;
        if(pathCancellation is not null) { StatusForVerification.Text="Yol araması zaten sürüyor.";return; }
        var points=CanvasForVerification.Points;
        if(DraftRevision!=expectedRevision || first<CanvasForVerification.ReferencePointCount || first>=points.Count || last<=first || last>=points.Count ||
            points[first]==points[last] || CanvasForVerification.Image is not BitmapSource source)
        { StatusForVerification.Text="Taslak veya seçili aralık değişti; yol aranmadı.";return; }
        var start=points[first];var end=points[last];var revision=DraftRevision;var color=ColorPathForVerification.IsChecked==true;var wide=WidePathForVerification.IsChecked==true;var cts=new CancellationTokenSource();pathCancellation=cts;CanvasForVerification.ClearPathSuggestion();
        StatusForVerification.Text="Seçili aralık için yol aranıyor; iptal edebilirsiniz.";
        try
        {
            var compute=ComputePathForVerification;
            var search=await Task.Run(()=>compute is null ? FindPath(source,start,end,color,wide,cts.Token) : new PathSearchResult(compute(source,start,end,cts.Token),false),cts.Token);
            cts.Token.ThrowIfCancellationRequested();if(closed)return;
            if(DraftRevision!=revision) { StatusForVerification.Text="Taslak arama sırasında değişti; öneri kullanılmadı.";return; }
            var previewed=search.Points is not null && CanvasForVerification.SetReplacementPath(first,last,search.Points);
            StatusForVerification.Text=previewed
                ? "Mavi yol seçili aralık için öneridir; uygularsanız aradaki eski noktalar değişir. Doku ve gölgeyi inceleyin."
                : "Uygun yeni yol bulunamadı veya nokta sınırı aşıldı; taslak değişmedi.";
            if(search.UsedStandard)StatusForVerification.Text="Geniş alan iş sınırını aştı; standart 16 piksel alan kullanıldı. "+StatusForVerification.Text;
            if(previewed)await ReviewProposedTopologyAsync(revision,cts.Token);
        }
        catch(OperationCanceledException) { if(!closed && DraftRevision==revision) { CanvasForVerification.ClearPathSuggestion();StatusForVerification.Text="Yol araması iptal edildi; taslak değişmedi."; } }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.IOException or OverflowException)
        { if(!closed)StatusForVerification.Text="Yol önerisi hazırlanamadı: "+ex.Message; }
        finally { if(ReferenceEquals(pathCancellation,cts))pathCancellation=null;cts.Dispose(); }
    }
    internal async Task StartPathSuggestionAsync(bool allSegments=false)
    {
        if(SourcePurpose!=PhotoSourcePurpose.FlatPattern) { StatusForVerification.Text="Yol önerisi için düz serilmiş kalıp görselini seçin.";return; }
        if(pathCancellation is not null) { StatusForVerification.Text="Yol araması zaten sürüyor.";return; }
        var points=CanvasForVerification.Points;
        if(points.Count<CanvasForVerification.ReferencePointCount+2 || CanvasForVerification.Image is not BitmapSource source) { StatusForVerification.Text="Önce referans noktalarını ve en az iki çizgi noktası seçin.";return; }
        var anchors=allSegments ? points.Skip(CanvasForVerification.ReferencePointCount).ToArray() : new[] { points[^2],points[^1] };
        if(anchors.Length>17) { CanvasForVerification.ClearPathSuggestion();StatusForVerification.Text="Tüm çizgi için en fazla 17 çizgi noktası seçin; mevcut noktalar korundu.";return; }
        var includeClosing=allSegments && ReviewClosureForVerification.IsChecked==true;
        if(includeClosing && anchors.Length<3) { CanvasForVerification.ClearPathSuggestion();StatusForVerification.Text="Kapanış yolu için en az üç çizgi noktası seçin.";return; }
        var revision=DraftRevision;var color=ColorPathForVerification.IsChecked==true;var wide=WidePathForVerification.IsChecked==true;var cts=new CancellationTokenSource();pathCancellation=cts;CanvasForVerification.ClearPathSuggestion();
        StatusForVerification.Text=includeClosing ? "Çizgi ve kapanış yolları aranıyor; iptal edebilirsiniz." : allSegments ? "Çizgi noktaları arasındaki yollar aranıyor; iptal edebilirsiniz." : "Son iki nokta arasında yol aranıyor; iptal edebilirsiniz.";
        try
        {
            var compute=ComputePathForVerification;
            var candidates=await Task.Run(()=>
            {
                var paths=new List<IReadOnlyList<Point>>();var usedStandard=false;
                var count=anchors.Length-1+(includeClosing ? 1 : 0);
                for(int i=0;i<count;i++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var start=anchors[i];var end=i+1==anchors.Length ? anchors[0] : anchors[i+1];
                    var candidate=compute is null ? FindPath(source,start,end,color,wide,cts.Token) : new PathSearchResult(compute(source,start,end,cts.Token),false);
                    usedStandard|=candidate.UsedStandard;
                    if(candidate.Points is null)return (Paths:(List<IReadOnlyList<Point>>?)null,UsedStandard:usedStandard);paths.Add(candidate.Points);
                }
                return (Paths:(List<IReadOnlyList<Point>>?)paths,UsedStandard:usedStandard);
            },cts.Token);
            cts.Token.ThrowIfCancellationRequested();if(closed)return;
            if(DraftRevision!=revision) { StatusForVerification.Text="Taslak arama sırasında değişti; öneri kullanılmadı.";return; }
            var previewed=candidates.Paths is not null && (allSegments ? CanvasForVerification.SetAllPathSuggestions(anchors,candidates.Paths,includeClosing) : CanvasForVerification.SetPathSuggestion(anchors[0],anchors[1],candidates.Paths[0]));
            StatusForVerification.Text=previewed
                ? (includeClosing ? "Mavi yol kapanışı da önerir; taslak açık kalır. Son kenarı ayrıca inceleyin." : allSegments ? "Mavi açık yol önerisi: ilk ve son birleşmez; doku ve gölgeyi kontrol edin." : "Kesikli mavi yol yalnız öneridir; doku veya gölgeyi izleyebilir. İnceleyip isterseniz uygulayın.")
                : "Uygun yol bulunamadı veya nokta sınırı aşıldı; taslak değişmedi.";
            if(candidates.UsedStandard)StatusForVerification.Text="En az bir aralıkta geniş alan iş sınırını aştı; standart 16 piksel alan kullanıldı. "+StatusForVerification.Text;
            if(previewed)await ReviewProposedTopologyAsync(revision,cts.Token);
        }
        catch(OperationCanceledException) { if(!closed && DraftRevision==revision) { CanvasForVerification.ClearPathSuggestion();StatusForVerification.Text="Yol araması iptal edildi; taslak değişmedi."; } }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.IOException or OverflowException)
        { if(!closed)StatusForVerification.Text="Yol önerisi hazırlanamadı: "+ex.Message; }
        finally { if(ReferenceEquals(pathCancellation,cts))pathCancellation=null;cts.Dispose(); }
    }
    private readonly record struct PathSearchResult(IReadOnlyList<Point>? Points,bool UsedStandard);
    private static PathSearchResult FindPath(BitmapSource source,Point start,Point end,bool color,bool wide,CancellationToken token)
    {
        if(wide) { var ok=PhotoWideEdgePath.TryFind(source,start,end,color,out var path,out var usedStandard,token);return new(ok ? path : null,usedStandard); }
        return color ? new(PhotoColorEdgePath.TryFind(source,start,end,out var colored,token) ? colored : null,false)
                     : new(PhotoEdgePath.TryFind(source,start,end,out var gray,token) ? gray : null,false);
    }
    private void UpdateStatus()
    {
        if(SourcePurpose==PhotoSourcePurpose.Unspecified) { StatusForVerification.Text="Önce görsel türünü kendiniz seçin; otomatik sınıflandırma yapılmaz.";return; }
        if(SourcePurpose==PhotoSourcePurpose.GarmentReference) { StatusForVerification.Text="Giysi/model yalnız görsel referanstır; fotoğraftan gerçek kalıp veya ölçek çıkarılmaz.";return; }
        var n=CanvasForVerification.Points.Count;
        var references=CanvasForVerification.ReferencePointCount;
        StatusForVerification.Text=$"Önizleme: {image.PixelWidth} × {image.PixelHeight} piksel; yakınlaştırma {MeasurementInput.Format(CanvasForVerification.Zoom)}×. "+(n<references ? $"{(references==4 ? "Düzlem köşesi" : "Referans ucu")} seçin: {n}/{references}. "+(references==4 ? "Dört köşeyi sırayla sol tıklayın." : "Sol tıkla iki uç seçin; düzlem ayarında dört köşe kullanılabilir.") : $"Referans hazır; çizgi noktası: {n-references}. İlk referans CAD'de 0,0 olur; Y aşağı artar.");
        if(CanvasForVerification.DenseTraceView)StatusForVerification.Text+=" Yoğun çizgide ara nokta numaraları gizlenir; Düzelt ile tüm noktaları seçebilirsiniz.";
        if(CanvasForVerification.HasTopologyWarning)
        {
            var target=CanvasForVerification.PathSuggestion is null ? "Çizgi" : "Yol önerisi";
            StatusForVerification.Text+=CanvasForVerification.HasTopologyWarningEdges
                ? $" {target}: Turuncu kenarlar kesişiyor veya temas ediyor; düzeltin."
                : $" {target}: piksel geometrisi uyarısı; çakışma, sıfır kenar veya kapanış sorunu olabilir.";
            if(ReviewClosureForVerification.IsChecked==true)StatusForVerification.Text+=" Kapanış kenarı önizlemedir; taslak açık kalır.";
        }
    }
    internal void TryApply()
    {
        if(sourceCurrent is not null && !sourceCurrent()) { StatusForVerification.Text="Kaynak istek veya fotoğraf değişti ya da kapandı; taslak projeye eklenmedi.";return; }
        if(SourcePurpose!=PhotoSourcePurpose.FlatPattern) { StatusForVerification.Text="CAD'e ölçekli taslak eklemek için düz serilmiş kalıp görselini seçin; giysi/model referansı kalıp değildir.";return; }
        try
        {
            var p=CanvasForVerification.Points;
            var referenceCount=CanvasForVerification.ReferencePointCount;
            if(p.Count<referenceCount+2) { StatusForVerification.Text="Referans noktalarını ve en az iki çizgi noktası seçin.";return; }
            var reviewClosure=ReviewClosureForVerification.IsChecked==true;
            if(reviewClosure && p.Count<referenceCount+3) { StatusForVerification.Text="Kapanışı incelemek için en az üç çizgi noktası seçin.";return; }
            if(string.IsNullOrEmpty(sourceSha256) || sourceByteLength<=0) { StatusForVerification.Text="Fotoğraf kaynak kimliği bulunamadı; taslak eklenmedi.";return; }
            if(!MeasurementInput.TryParseMillimeters(LengthForVerification.Text,out var mm) || mm<=0) { StatusForVerification.Text="Bilinen mesafeyi pozitif milimetre olarak girin.";return; }
            var referenceStart=new Point2(p[0].X,p[0].Y);var referenceEnd=new Point2(p[1].X,p[1].Y);
            var pixels=p.Skip(referenceCount).Select(q=>new Point2(q.X,q.Y)).ToList();
            PhotoPlaneCalibration? plane=null;IReadOnlyList<Point2> trace;
            if(PlaneSettings is { } settings)
            {
                if(referenceCount!=4 || BitConverter.DoubleToInt64Bits(mm)!=BitConverter.DoubleToInt64Bits(settings.Width))throw new InvalidOperationException("Düzlem eni ayarla uyuşmuyor; yeniden seçin.");
                plane=new PhotoPlaneCalibration { Corners=p.Take(4).Select(q=>new Point2(q.X,q.Y)).ToList(),WidthMm=settings.Width,HeightMm=settings.Height };
                trace=PhotoPlaneRectification.Convert(image.PixelWidth,image.PixelHeight,plane.Corners,plane.WidthMm,plane.HeightMm,pixels);
            }
            else
            {
                if(referenceCount!=2)throw new InvalidOperationException("Referans türü uyuşmuyor; yeniden seçin.");
                trace=PhotoTraceCalibration.Convert(image.PixelWidth,image.PixelHeight,sourceWidth,sourceHeight,referenceStart,referenceEnd,mm,pixels);
            }
            var history=new PhotoTraceProvenance { AlgorithmVersion=plane is null ? 1 : 2,SourceSha256=sourceSha256,SourceByteLength=sourceByteLength,SourceOrientation=sourceOrientation,SourceWidth=sourceWidth,SourceHeight=sourceHeight,PreviewWidth=image.PixelWidth,PreviewHeight=image.PixelHeight,ReferenceStart=referenceStart,ReferenceEnd=referenceEnd,ReferenceLengthMm=mm,PlaneCalibration=plane,TracePixels=pixels,GeneratedPoints=trace.ToList() };
            LargeOpenPathValidator.Validate(trace);
            if(workspace.PhotoTraceProblem(stamp) is { } stale) { StatusForVerification.Text=stale;return; }
            var distance=Math.Sqrt(Math.Pow(p[1].X-p[0].X,2)+Math.Pow(p[1].Y-p[0].Y,2));
            var pendingSuggestion=CanvasForVerification.PathSuggestion is not null || CanvasForVerification.EdgeSuggestion is not null
                ? "\n\nUygulanmamış mavi öneri çizgiye dahil edilmeyecek." : "";
            var closureNotice=reviewClosure ? "\n\nArdından kapanış incelemesi açılacak; kalan son kenar düz olarak kapatılır. Kapalı parça ayrıca onaylanır; fotoğraf geçmişi açık kaynakta kalır." : "";
            var calibrationNotice=plane is null ? $"Referans uçları önizlemede {MeasurementInput.Format(distance)} piksel aralığında. Kısa bir referans, nokta seçme hatasını büyütebilir; mümkünse daha uzun bir referans seçin." :
                $"Düzlem referansı {MeasurementInput.Format(plane.WidthMm)} × {MeasurementInput.Format(plane.HeightMm)} mm. Dört köşe ve çizgi aynı düzlemde olmalı; seçilen köşeler doğru sırada ve ölçüler gerçek olmalıdır. Lens bozulması ve kumaş dökümü düzeltilmez.";
            var version=plane is null ? 8 : 9;
            var question=$"{calibrationNotice}\n\nSeçilen ve kabul edilen noktalardan oluşan açık çizgi CAD'e taslak olarak eklenecek. Gerçek beden, görünmeyen parçalar ve üretim uygunluğu doğrulanmadı. Görsel türü seçimi proje geçmişine kaydedilmez. Fotoğraf özeti, boyutlar, işaretler ve referans proje biçimi {version} ile kaydedilir; fotoğrafın kendisi eklenmez. Önceki sürümler dosyayı açamaz.{pendingSuggestion}{closureNotice} Devam edilsin mi?";
            var approvedRevision=DraftRevision;
            var confirmed=ConfirmForVerification?.Invoke(question) ?? (MessageBox.Show(this,question,"Taslak çizgi",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes);
            if(!confirmed) { StatusForVerification.Text="İşlem iptal edildi; proje değiştirilmedi.";return; }
            if(sourceCurrent is not null && !sourceCurrent()) { StatusForVerification.Text="Kaynak istek veya fotoğraf değişti ya da kapandı; taslak projeye eklenmedi.";return; }
            if(DraftRevision!=approvedRevision) { StatusForVerification.Text="Fotoğraf noktaları veya değerleri değişti; taslak eklenmedi.";return; }
            if(!workspace.ApplyPhotoTrace(stamp,trace,history,out var addedId)) { StatusForVerification.Text=workspace.PhotoTraceProblem(stamp) ?? "Taslak eklenemedi.";return; }
            var closureOwner=Owner;allowClose=true;Close();
            if(reviewClosure && addedId is Guid id) { if(OpenClosureForVerification is { } open)open(id);else workspace.OpenPhotoTraceClosure(id,closureOwner); }
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or System.IO.InvalidDataException or OverflowException)
        { StatusForVerification.Text="Taslak eklenemedi: "+ex.Message; }
    }
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(!allowClose && HasDraft)
        {
            var revision=DraftRevision;
            var accepted=MessageBox.Show(this,"İşaretlenmiş noktalar ve girilen değerler kaybolacak. Kapatılsın mı?","Taslak çizgi",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
            if(!accepted || revision!=DraftRevision) { e.Cancel=true;if(accepted)StatusForVerification.Text="Taslak değişti; pencere kapatılmadı."; }
        }
    }
}
