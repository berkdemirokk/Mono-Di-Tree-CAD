using Microsoft.Win32;
using TekstilCAD.FileFormats;
using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoRegionSharpPrototype;

namespace TekstilCAD.Desktop;

/// <summary>Experimental, user-selected photo region; never writes a CAD document.</summary>
internal sealed class PhotoRegionWindow : Window
{
    private readonly BitmapSource image;
    private CancellationTokenSource? running;
    private bool closed;
    private long regionRevision,reviewRevision;
    private bool changingRectangle,rectangleConfirmationActive;
    internal Func<bool>? ConfirmRectangleChangeForVerification { get; set; }
    internal Func<bool>? ConfirmCloseForVerification { get; set; }
    internal Func<Window,string,string,MessageBoxButton,MessageBoxImage,MessageBoxResult,MessageBoxResult>? ShowDiscardDialogForVerification { get; set; }
    private MaskComponents? components;
    private RegionProposal.Snapshot? snapshot;
    private RegionProposal.Snapshot? undoSnapshot;
    private MaskComponents? undoComponents;
    // 0: no user mark, 1: definite foreground, 2: definite background.
    // Arrays are replaced rather than mutated, so undo retains the exact prior state.
    private byte[]? appliedMarks,undoAppliedMarks;
    private byte[]? sourceBgr;
    private readonly Dictionary<int,bool> pendingMarks=[];
    private Dictionary<int,bool?>? pendingUndo;
    internal readonly TextBox left=new() { Width=64,MaxLength=6 },top=new() { Width=64,MaxLength=6 },width=new() { Width=64,MaxLength=6 },height=new() { Width=64,MaxLength=6 };
    private System.Windows.Point? dragStart;
    private List<System.Windows.Point>? activeStroke;
    private bool strokeOverflow;
    private int strokeMode,strokeSize;
    private readonly Image overlay=new() { Stretch=Stretch.Uniform,IsHitTestVisible=false };
    private readonly Image comparisonOverlay=new() { Stretch=Stretch.Uniform,IsHitTestVisible=false };
    private readonly Image boxOverlay=new() { Stretch=Stretch.Uniform,IsHitTestVisible=false };
    private readonly Image marksOverlay=new() { Stretch=Stretch.Uniform,IsHitTestVisible=false };
    internal ComboBox Regions { get; }=new() { MinWidth=240,Margin=new Thickness(0,4,8,4) };
    internal ComboBox MarkMode { get; }=new() { MinWidth=160,Margin=new Thickness(0,4,8,4) };
    internal ComboBox BrushSize { get; }=new() { MinWidth=86,Margin=new Thickness(8,0,0,0) };
    internal TextBox MarkX { get; }=new() { Width=56,MaxLength=6 };
    internal TextBox MarkY { get; }=new() { Width=56,MaxLength=6 };
    internal CheckBox OmitHoles { get; }=new() { Content="İç boşlukların aktarılmayacağını anlıyorum",Margin=new Thickness(0,4,0,4) };
    internal Button SaveSessionForVerification { get; private set; }=null!;
    internal Button LoadSessionForVerification { get; private set; }=null!;
    internal CheckBox AcknowledgeImageEdge { get; }=new() { Content="Görüntü kenarındaki sınırın kırpılmış olabileceğini inceledim",Margin=new Thickness(0,4,0,4),Visibility=Visibility.Collapsed };
    internal CheckBox ComparePrevious { get; }=new() { Content="Öncekiyle farkı göster",Margin=new Thickness(8,4,0,4),IsEnabled=false };
    internal CheckBox ShowAppliedMarks { get; }=new() { Content="Uygulananları göster",Margin=new Thickness(8,4,0,4) };
    internal TextBlock BoundaryWarning { get; }=new() { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Visibility=Visibility.Collapsed };
    internal TextBlock Status { get; }=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,4) };
    internal System.Windows.Point[]? SelectedContour { get; private set; }
    internal bool HasRectangleOverlay => boxOverlay.Source is not null;
    internal byte[]? CopyMaskForVerification()=>snapshot?.CopyBinaryMask();
    internal int PendingMarkCount => pendingMarks.Count;
    internal bool HasMarksOverlay => marksOverlay.Source is not null;
    internal byte AppliedMarkForVerification(int x,int y)=>appliedMarks?[y*image.PixelWidth+x] ?? 0;
    internal bool? PendingMarkAtForVerification(int x,int y)=>pendingMarks.TryGetValue(y*image.PixelWidth+x,out var value) ? value : null;
    internal Action<CancellationToken>? BeforeRefineForVerification { get; set; }
    internal Action<CancellationToken>? BeforeComputeForVerification { get; set; }
    internal Action<CancellationToken>? BeforeSessionSaveForVerification { get; set; }
    internal Action<CancellationToken>? BeforeSessionLoadForVerification { get; set; }
    internal Button CancelSessionForVerification { get; }=new() { Content="Dosya işlemini iptal et",Visibility=Visibility.Collapsed,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,4) };
    private bool sessionOperation;
    private sealed record Choice(int Id,int Pixels,int Left,int Top,int Right,int Bottom)
    {
        public override string ToString()=>$"Bölge {Id} · {Pixels} piksel · ({Left},{Top})–({Right},{Bottom})";
    }

    internal PhotoRegionWindow(Window owner,BitmapSource image,bool reviewOnly=false)
    {
        if(!image.IsFrozen || image.PixelWidth<3 || image.PixelHeight<3 || image.PixelWidth>RegionProposal.MaxDimension ||
           image.PixelHeight>RegionProposal.MaxDimension || (long)image.PixelWidth*image.PixelHeight>RegionProposal.MaxPixels)
            throw new ArgumentException("Fotoğraf önizlemesi sabit ve desteklenen boyutta olmalıdır.",nameof(image));
        this.image=image;Owner=owner;Title="Fotoğrafta bölge önerisi (deneysel)";Width=860;Height=760;MinWidth=620;MinHeight=550;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;StudioTheme.Apply(this);
        var root=new DockPanel { Margin=new Thickness(12) };Content=root;
        var actions=new WrapPanel { Margin=new Thickness(0,5,0,0) };DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var compute=new Button { Content="Bölge ara",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,4) };
        var accept=new Button { Content="Seçili sınırı önizle",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,4) };
        var cancel=new Button { Content="Kapat",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,4) };
        var refine=new Button { Content="İşaretleri uygula",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,4) };
        var clearMarks=new Button { Content="İşaretleri sil",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,4) };
        compute.Click+=async (_,_)=>await ComputeAsync();accept.Click+=(_,_)=> { if(TrySelectContour())DialogResult=true; };cancel.Click+=(_,_)=>Close();
        refine.Click+=async (_,_)=>await RefineAsync();clearMarks.Click+=(_,_)=>ClearMarks();
        actions.Children.Add(compute);if(!reviewOnly)actions.Children.Add(accept);actions.Children.Add(refine);actions.Children.Add(clearMarks);actions.Children.Add(cancel);
        {
            var save=new Button { Content="Bölgeyi kaydet…",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,4) };
            var load=new Button { Content="Bölgeyi aç…",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,4) };
            SaveSessionForVerification=save;LoadSessionForVerification=load;
            AutomationProperties.SetHelpText(save,"Maskeyi ve uygulanmış işaretleri fotoğraf piksel özetiyle yeni dosyaya kaydeder. Fotoğraf veya kalıp içermez; bekleyen işaretler önce uygulanmalı veya silinmelidir.");
            AutomationProperties.SetHelpText(load,"Aynı çözülmüş fotoğrafa ait bölgeyi açar; CAD projesi değişmez.");
            save.Click+=async (_,_)=> { if(running is not null || snapshot is null || pendingMarks.Count>0) { Status.Text="Önce bölge arayın, süren işlemi bitirin ve bekleyen işaretleri uygulayın veya silin.";return; }
                var savedSnapshot=snapshot;var savedApplied=appliedMarks;var savedRevision=regionRevision;
                var savedFields=new[] { left.Text,top.Text,width.Text,height.Text };
                var dialog=new SaveFileDialog { Filter="Fotoğraf bölgesi (*.tdregion)|*.tdregion",DefaultExt=".tdregion",AddExtension=true,OverwritePrompt=false };
                if(dialog.ShowDialog(this)!=true)return;
                if(closed || running is not null || pendingMarks.Count!=0 || regionRevision!=savedRevision || !ReferenceEquals(snapshot,savedSnapshot) || !ReferenceEquals(appliedMarks,savedApplied) ||
                   !savedFields.SequenceEqual(new[] { left.Text,top.Text,width.Text,height.Text })) { Status.Text="Bölge seçim sırasında değişti; dosya kaydedilmedi.";return; }
                await TrySaveSessionAsync(dialog.FileName); };
            load.Click+=async (_,_)=>await PickAndLoadSessionAsync(()=> {
                var dialog=new OpenFileDialog { Filter="Fotoğraf bölgesi (*.tdregion)|*.tdregion",CheckFileExists=true };
                return dialog.ShowDialog(this)==true ? dialog.FileName : null; });
            actions.Children.Add(save);actions.Children.Add(load);
            AutomationProperties.SetName(CancelSessionForVerification,"Bölge dosyası işlemini iptal et");
            CancelSessionForVerification.Click+=(_,_)=>CancelSessionOperation();actions.Children.Add(CancelSessionForVerification);
        }
        var controls=new StackPanel();DockPanel.SetDock(controls,Dock.Top);root.Children.Add(controls);
        controls.Children.Add(new TextBlock { Text=reviewOnly ? "Giysi fotoğrafında bölge arayıp düzeltmeleri inceleyin. Bu yalnız renk tahminidir; ölçü, kalıp veya CAD aktarımı değildir." : "Dikdörtgen sürükleyin, bölge arayın; listeden veya fotoğraftan seçin. Ekle/çıkar fırçasıyla düzeltin; az işaret büyük gösterilir. Sonuç renk tahminidir.",TextWrapping=TextWrapping.Wrap });
        var rectangle=new WrapPanel { Margin=new Thickness(0,4,0,4) };controls.Children.Add(rectangle);
        void Input(string label,TextBox box,int value) { rectangle.Children.Add(new Label { Content=label,Target=box,Foreground=StudioTheme.Text,Padding=new Thickness(3) });box.Text=value.ToString(CultureInfo.InvariantCulture);AutomationProperties.SetName(box,"Bölge dikdörtgeni "+label);rectangle.Children.Add(box); }
        var x=Math.Max(1,image.PixelWidth/20);var y=Math.Max(1,image.PixelHeight/20);
        Input("Sol",left,x);Input("Üst",top,y);Input("En",width,Math.Max(0,image.PixelWidth-2*x));Input("Boy",height,Math.Max(0,image.PixelHeight-2*y));
        var selection=new WrapPanel();selection.Children.Add(new Label { Content="Seçilen bölge",Target=Regions,Foreground=StudioTheme.Text,Padding=new Thickness(3) });selection.Children.Add(Regions);
        MarkMode.ItemsSource=new[] { "Dikdörtgen", "Bölgeye ekle", "Bölgeden çıkar", "Bölge seç" };MarkMode.SelectedIndex=0;selection.Children.Add(MarkMode);controls.Children.Add(selection);
        var markerInput=new WrapPanel();markerInput.Children.Add(new Label { Content="İşaret X",Target=MarkX,Foreground=StudioTheme.Text,Padding=new Thickness(3) });markerInput.Children.Add(MarkX);
        markerInput.Children.Add(new Label { Content="Y",Target=MarkY,Foreground=StudioTheme.Text,Padding=new Thickness(3) });markerInput.Children.Add(MarkY);
        var addMark=new Button { Content="Konumu kullan",Padding=new Thickness(8,3,8,3),Margin=new Thickness(8,0,0,0) };markerInput.Children.Add(addMark);
        markerInput.Children.Add(new Label { Content="Fırça",Target=BrushSize,Foreground=StudioTheme.Text,Padding=new Thickness(8,3,3,3) });
        BrushSize.ItemsSource=new[] { "1 piksel", "5 piksel", "9 piksel" };BrushSize.SelectedIndex=0;markerInput.Children.Add(BrushSize);controls.Children.Add(markerInput);
        var undoMark=new Button { Content="Son işareti geri al",Padding=new Thickness(8,3,8,3),Margin=new Thickness(8,0,0,0) };
        AutomationProperties.SetHelpText(undoMark,"Son bekleyen fırça hareketini geri alır; önceki işaretleri korur. Uygulanmış bölge hesabını geri almaz.");
        undoMark.Click+=(_,_)=>UndoPendingStroke();markerInput.Children.Add(undoMark);
        addMark.Click+=(_,_)=> { if(!int.TryParse(MarkX.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var mx) || !int.TryParse(MarkY.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var my)) { Status.Text="Görüntü içindeki tam sayı koordinatlarını girin.";return; }
            if(MarkMode.SelectedIndex==3)TrySelectComponentAt(mx,my);else TryAddMark(mx,my); };
        AutomationProperties.SetName(MarkX,"Seçim veya düzeltme X piksel");AutomationProperties.SetName(MarkY,"Seçim veya düzeltme Y piksel");AutomationProperties.SetName(MarkMode,"Dikdörtgen, düzeltme veya bölge seçimi");AutomationProperties.SetName(BrushSize,"Düzeltme fırçası çapı piksel");
        var options=new WrapPanel();if(!reviewOnly) { options.Children.Add(OmitHoles);options.Children.Add(AcknowledgeImageEdge); }
        var undoRefinement=new Button { Content="Düzeltmeyi geri al",Padding=new Thickness(8,3,8,3),Margin=new Thickness(8,0,0,2) };
        AutomationProperties.SetHelpText(undoRefinement,"Son başarılı düzeltme veya yeniden aramadan önceki bölgeyi bir kez geri yükler; bekleyen işaretler önce uygulanmalı veya silinmelidir.");
        undoRefinement.Click+=(_,_)=>UndoRefinement();options.Children.Add(undoRefinement);options.Children.Add(ShowAppliedMarks);options.Children.Add(ComparePrevious);controls.Children.Add(options);controls.Children.Add(BoundaryWarning);controls.Children.Add(Status);
        AutomationProperties.SetName(BoundaryWarning,"Bölge sınırı uyarısı");AutomationProperties.SetLiveSetting(BoundaryWarning,AutomationLiveSetting.Polite);
        AcknowledgeImageEdge.Checked+=(_,_)=>reviewRevision++;AcknowledgeImageEdge.Unchecked+=(_,_)=>reviewRevision++;
        AutomationProperties.SetHelpText(ShowAppliedMarks,"Önceki düzeltmelerde uygulanan işaretleri soluk, bekleyen işaretleri parlak gösterir; maskeyi değiştirmez.");
        AutomationProperties.SetHelpText(ComparePrevious,"Tüm maskelerde eklenen pikseller yeşil, çıkarılanlar kırmızıdır. Değişmeyen alanlar gösterilmez; doğruluk ölçümü değildir.");
        ComparePrevious.Checked+=(_,_)=>UpdateComparison();ComparePrevious.Unchecked+=(_,_)=>UpdateComparison();
        ShowAppliedMarks.Checked+=(_,_)=>UpdateMarksOverlay();ShowAppliedMarks.Unchecked+=(_,_)=>UpdateMarksOverlay();
        AutomationProperties.SetName(Regions,"Önerilen bölgeler");AutomationProperties.SetName(Status,"Bölge önerisi durumu");AutomationProperties.SetLiveSetting(Status,AutomationLiveSetting.Polite);
        var view=new Grid { ClipToBounds=true,MinHeight=100,Background=Brushes.Transparent };view.Children.Add(new Image { Source=image,Stretch=Stretch.Uniform });view.Children.Add(overlay);view.Children.Add(comparisonOverlay);view.Children.Add(boxOverlay);view.Children.Add(marksOverlay);root.Children.Add(view);
        AutomationProperties.SetName(view,"Bölge dikdörtgeni seçimi");
        AutomationProperties.SetHelpText(view,"Dikdörtgen modunda sürükleyerek alanı belirleyin; bölge seç modunda bölgeye tıklayın; ekle veya çıkar modunda sürükleyerek düzeltin. Koordinatlar klavyeyle de girilebilir.");
        view.MouseLeftButtonDown+=(_,e)=> { var p=e.GetPosition(view);if(!TryMapViewPixel(p,view.RenderSize,false,out var pixel))return;
            if(MarkMode.SelectedIndex==3) { TrySelectComponentAt((int)pixel.X,(int)pixel.Y);e.Handled=true;return; }
            if(MarkMode.SelectedIndex is 1 or 2) { if(running is not null || snapshot is null) { Status.Text="Önce bölge arayın.";return; } activeStroke=[pixel];strokeMode=MarkMode.SelectedIndex;strokeSize=BrushSize.SelectedIndex;strokeOverflow=false;view.CaptureMouse();e.Handled=true;return; }
            dragStart=p;view.CaptureMouse();e.Handled=true; };
        view.MouseMove+=(_,e)=> { if(activeStroke is null || e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed)return;
            if(TryMapViewPixel(e.GetPosition(view),view.RenderSize,true,out var pixel) && activeStroke[^1]!=pixel)
            { if(activeStroke.Count>=4096)strokeOverflow=true;else activeStroke.Add(pixel); }e.Handled=true; };
        view.MouseLeftButtonUp+=(_,e)=> { if(activeStroke is { } stroke) { activeStroke=null;
                if(TryMapViewPixel(e.GetPosition(view),view.RenderSize,true,out var last) && stroke[^1]!=last) { if(stroke.Count>=4096)strokeOverflow=true;else stroke.Add(last); }
                view.ReleaseMouseCapture();if(strokeOverflow || strokeMode!=MarkMode.SelectedIndex || strokeSize!=BrushSize.SelectedIndex)Status.Text="Fırça hareketi çok uzun veya türü değişti; yeni işaret eklenmedi.";else TryAddStroke(stroke);e.Handled=true;return; }
            if(dragStart is not System.Windows.Point start)return;dragStart=null;view.ReleaseMouseCapture();
            TrySetRectangleFromView(start,e.GetPosition(view),view.RenderSize);e.Handled=true; };
        view.LostMouseCapture+=(_,_)=> { dragStart=null;activeStroke=null; };
        Regions.SelectionChanged+=(_,_)=> { reviewRevision++;ShowSelection(); };
        foreach(var box in new[] { OmitHoles,ShowAppliedMarks,ComparePrevious })
        { box.Checked+=(_,_)=>reviewRevision++;box.Unchecked+=(_,_)=>reviewRevision++; }
        MarkMode.SelectionChanged+=(_,_)=>reviewRevision++;BrushSize.SelectionChanged+=(_,_)=>reviewRevision++;
        foreach(var box in new[] { left,top,width,height })
        {
            box.Tag=box.Text;
            box.TextChanged+=(_,_)=> {
                if(changingRectangle)return;
                var previous=(string)box.Tag;
                if(!ConfirmRectangleChange()) { changingRectangle=true;try { box.Text=previous; } finally { changingRectangle=false; } Status.Text="Dikdörtgen değişikliği iptal edildi; bölge ve işaretler korundu.";return; }
                box.Tag=box.Text;InvalidateResult();
            };
        }
        UpdateBoxOverlay();
        Status.Text="Dikdörtgeni kontrol edin ve Bölge ara seçin. Önceki çizim değişmez.";
        Closing+=(_,e)=> {
            if(DialogResult==true)return;
            if(!ConfirmDiscard(ConfirmCloseForVerification,sessionOperation ? "Dosya işlemi sürüyor; kapatma iptal ister. Tamamlanmış kayıt geri alınmaz. Bölge ve işaretler aktarılmaz. Kapatılsın mı?" : "Bölge penceresi kapatılırsa mevcut öneri ve işaretler aktarılmaz. Kapatılsın mı?","Bölgeyi kapat"))
            { e.Cancel=true;Status.Text="Kapatma iptal edildi; bölge ve işaretler korundu."; }
        };
        Closed+=(_,_)=> { closed=true;running?.Cancel(); };
    }
    private bool ConfirmRectangleChange() => ConfirmDiscard(ConfirmRectangleChangeForVerification,
        "Dikdörtgen değişirse mevcut bölge ve düzeltme işaretleri silinir. Devam edilsin mi?","Bölgeyi değiştir");
    private bool ConfirmDiscard(Func<bool>? confirmForVerification,string message,string title)
    {
        if(closed || rectangleConfirmationActive)return false;
        if(snapshot is null && pendingMarks.Count==0 && running is null)return true;
        var oldSnapshot=snapshot;var oldComponents=components;var oldRunning=running;var oldApplied=appliedMarks;
        var oldUndo=undoSnapshot;var oldUndoComponents=undoComponents;var oldUndoApplied=undoAppliedMarks;
        var oldPendingUndo=pendingUndo;var oldSource=sourceBgr;var oldContour=SelectedContour;
        var oldMarks=pendingMarks.ToArray();var oldSelection=Regions.SelectedItem;var oldOmitHoles=OmitHoles.IsChecked;var oldEdgeAcknowledgement=AcknowledgeImageEdge.IsChecked;
        var oldFields=new[] { left.Text,top.Text,width.Text,height.Text };var oldReviewRevision=reviewRevision;
        rectangleConfirmationActive=true;bool accepted;
        try { accepted=confirmForVerification?.Invoke() ??
            ShowDiscardDialog(message,title)==MessageBoxResult.Yes; }
        finally { rectangleConfirmationActive=false; }
        return accepted && !closed && reviewRevision==oldReviewRevision && ReferenceEquals(snapshot,oldSnapshot) && ReferenceEquals(components,oldComponents) &&
            ReferenceEquals(running,oldRunning) && ReferenceEquals(appliedMarks,oldApplied) && ReferenceEquals(undoSnapshot,oldUndo) &&
            ReferenceEquals(undoComponents,oldUndoComponents) && ReferenceEquals(undoAppliedMarks,oldUndoApplied) &&
            ReferenceEquals(pendingUndo,oldPendingUndo) && ReferenceEquals(sourceBgr,oldSource) && ReferenceEquals(SelectedContour,oldContour) &&
            ReferenceEquals(Regions.SelectedItem,oldSelection) && OmitHoles.IsChecked==oldOmitHoles && AcknowledgeImageEdge.IsChecked==oldEdgeAcknowledgement && pendingMarks.Count==oldMarks.Length &&
            oldMarks.All(mark=>pendingMarks.TryGetValue(mark.Key,out var value) && value==mark.Value) &&
            oldFields.SequenceEqual(new[] { left.Text,top.Text,width.Text,height.Text });
    }
    private MessageBoxResult ShowDiscardDialog(string message,string title)
    {
        // The same arguments reach the native dialog and the verification boundary.
        var show=ShowDiscardDialogForVerification ?? new Func<Window,string,string,MessageBoxButton,MessageBoxImage,MessageBoxResult,MessageBoxResult>(MessageBox.Show);
        return show(this,message,title,MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No);
    }
    private void InvalidateResult()
    {
        regionRevision++;running?.Cancel();components=null;snapshot=null;undoSnapshot=null;undoComponents=null;appliedMarks=null;undoAppliedMarks=null;sourceBgr=null;pendingMarks.Clear();pendingUndo=null;marksOverlay.Source=null;SelectedContour=null;Regions.Items.Clear();overlay.Source=null;OmitHoles.IsChecked=false;ResetComparison();
        UpdateBoxOverlay();
        Status.Text="Dikdörtgen değişti; yeniden bölge arayın.";
    }
    private bool TryMapViewPixel(System.Windows.Point point,Size viewport,bool clamp,out System.Windows.Point pixel)
    {
        pixel=default;
        if(!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) || viewport.Width<=0 || viewport.Height<=0)return false;
        var scale=Math.Min(viewport.Width/image.Width,viewport.Height/image.Height);
        if(!double.IsFinite(scale) || scale<=0)return false;
        var renderWidth=image.Width*scale;var renderHeight=image.Height*scale;
        var x=(viewport.Width-renderWidth)/2;var y=(viewport.Height-renderHeight)/2;
        if(!clamp && (point.X<x || point.Y<y || point.X>=x+renderWidth || point.Y>=y+renderHeight))return false;
        var px=Math.Clamp((point.X-x)/renderWidth,0,1);var py=Math.Clamp((point.Y-y)/renderHeight,0,1);
        pixel=new System.Windows.Point(Math.Min(image.PixelWidth-1,(int)Math.Floor(px*image.PixelWidth)),Math.Min(image.PixelHeight-1,(int)Math.Floor(py*image.PixelHeight)));
        return true;
    }
    internal bool TrySetRectangleFromView(System.Windows.Point start,System.Windows.Point end,Size viewport)
    {
        if(!TryMapViewPixel(start,viewport,false,out var a) || !TryMapViewPixel(end,viewport,true,out var b)) { Status.Text="Fotoğraf içinde bir alan seçin.";return false; }
        var x=(int)Math.Min(a.X,b.X);var y=(int)Math.Min(a.Y,b.Y);
        var w=(int)Math.Abs(a.X-b.X)+1;var h=(int)Math.Abs(a.Y-b.Y)+1;
        if(w<2 || h<2) { Status.Text="Fotoğrafta en az iki piksel genişliğinde ve yüksekliğinde alan seçin.";return false; }
        var values=new[] { x.ToString(CultureInfo.InvariantCulture),y.ToString(CultureInfo.InvariantCulture),w.ToString(CultureInfo.InvariantCulture),h.ToString(CultureInfo.InvariantCulture) };
        var boxes=new[] { left,top,width,height };
        if(boxes.Select((box,i)=>box.Text==values[i]).All(equal=>equal))return true;
        if(!ConfirmRectangleChange()) { Status.Text="Dikdörtgen değişikliği iptal edildi; bölge ve işaretler korundu.";return false; }
        changingRectangle=true;
        try { for(var i=0;i<boxes.Length;i++) { boxes[i].Text=values[i];boxes[i].Tag=values[i]; } }
        finally { changingRectangle=false; }
        InvalidateResult();Status.Text="Dikdörtgen seçildi; sarı çerçeveyi kontrol edip Bölge ara seçin.";return true;
    }
    private void UpdateBoxOverlay()
    {
        boxOverlay.Source=null;
        if(!int.TryParse(left.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var x) || !int.TryParse(top.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var y) ||
           !int.TryParse(width.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var w) || !int.TryParse(height.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var h) ||
           x<0 || y<0 || w<1 || h<1 || (long)x+w>image.PixelWidth || (long)y+h>image.PixelHeight)return;
        var pixels=new byte[checked(image.PixelWidth*image.PixelHeight*4)];
        void Mark(int px,int py) { var p=(py*image.PixelWidth+px)*4;pixels[p]=0;pixels[p+1]=200;pixels[p+2]=255;pixels[p+3]=255; }
        for(var i=x;i<x+w;i++) { Mark(i,y);Mark(i,y+h-1); }
        for(var i=y;i<y+h;i++) { Mark(x,i);Mark(x+w-1,i); }
        var bitmap=BitmapSource.Create(image.PixelWidth,image.PixelHeight,image.DpiX,image.DpiY,PixelFormats.Bgra32,null,pixels,image.PixelWidth*4);bitmap.Freeze();boxOverlay.Source=bitmap;
    }
    internal async Task ComputeAsync()
    {
        if(running is not null) { Status.Text="Önceki aramanın bitmesini bekleyin.";return; }
        if(pendingMarks.Count>0) { Status.Text="Önce bekleyen işaretleri uygulayın veya silin.";return; }
        if(!int.TryParse(left.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var x) || !int.TryParse(top.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var y) ||
           !int.TryParse(width.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var w) || !int.TryParse(height.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var h))
        { Status.Text="Dikdörtgen için tam sayı piksel değerleri girin.";return; }
        if(snapshot is not null && !ConfirmDiscard(ConfirmRectangleChangeForVerification,"Yeni arama başarılı olursa mevcut bölge ve uygulanmış düzeltmeler değiştirilir. Önceki bölge bir kez geri alınabilir; eski geri alma adımı yenilenir. Devam edilsin mi?","Bölgeyi yeniden ara")) { Status.Text="Yeniden arama iptal edildi; mevcut bölge korundu.";return; }
        var revision=regionRevision;var cts=new CancellationTokenSource();running=cts;Status.Text="Bölge aranıyor; önceki bölge işlem bitene kadar korunur.";
        try
        {
            var result=await Task.Run(()=> {
                BeforeComputeForVerification?.Invoke(cts.Token);cts.Token.ThrowIfCancellationRequested();
                var bgr=PhotoRasterCapture.CaptureBgr(image,new Int32Rect(0,0,image.PixelWidth,image.PixelHeight),cts.Token);
                var proposal=RegionProposal.Start(bgr,image.PixelWidth,image.PixelHeight,new OpenCvSharp.Rect(x,y,w,h),cts.Token);
                var found=MaskComponents.Analyze(proposal.CopyBinaryMask(),image.PixelWidth,image.PixelHeight,cts.Token);
                return (Bgr:bgr,Snapshot:proposal,Components:found);
            },cts.Token);
            cts.Token.ThrowIfCancellationRequested();if(closed || revision!=regionRevision)return;
            if(result.Components.Components.Count>512) { Status.Text=$"{result.Components.Components.Count} bölge bulundu; seçim listesi sınırı aşıldı. Önceki bölge korundu.";return; }
            undoSnapshot=snapshot;undoComponents=components;undoAppliedMarks=appliedMarks;appliedMarks=null;pendingUndo=null;marksOverlay.Source=null;
            sourceBgr=result.Bgr;snapshot=result.Snapshot;ShowComponents(result.Components);
            if(undoSnapshot is not null)Status.Text+=" Önceki bölge Düzeltmeyi geri al ile geri yüklenebilir.";
        }
        catch(OperationCanceledException) { if(!closed && revision==regionRevision)Status.Text="Arama iptal edildi; önceki bölge korundu."; }
        catch(TypeInitializationException ex) when(ex.GetBaseException() is DllNotFoundException or BadImageFormatException)
        { if(!closed && revision==regionRevision)Status.Text="Görüntü işleme bileşeni yüklenemedi. Uygulama dosyalarını ve Windows sürümünü kontrol edin; mevcut çizim değişmedi."; }
        catch(Exception ex) when(ex is DllNotFoundException or BadImageFormatException)
        { if(!closed && revision==regionRevision)Status.Text="Görüntü işleme bileşeni yüklenemedi. Uygulama dosyalarını ve Windows sürümünü kontrol edin; mevcut çizim değişmedi."; }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OpenCvSharp.OpenCVException or System.IO.IOException or DllNotFoundException or BadImageFormatException or TypeInitializationException)
        { if(!closed && revision==regionRevision)Status.Text="Bölge önerisi hazırlanamadı; önceki bölge korundu: "+ex.Message; }
        finally { if(ReferenceEquals(running,cts))running=null;cts.Dispose(); }
    }
    internal void CancelSessionOperation()
    {
        if(!sessionOperation || running is null || running.IsCancellationRequested)return;
        running.Cancel();CancelSessionForVerification.IsEnabled=false;
        Status.Text="İptal istendi; süren dosya okuma veya yazma adımı bitince duracak. Tamamlanmış kayıt geri alınmaz.";
    }
    private void BeginSessionOperation(CancellationTokenSource cts)
    {
        running=cts;sessionOperation=true;CancelSessionForVerification.IsEnabled=true;CancelSessionForVerification.Visibility=Visibility.Visible;
    }
    private void EndSessionOperation(CancellationTokenSource cts)
    {
        if(ReferenceEquals(running,cts)) { running=null;sessionOperation=false;if(!closed)CancelSessionForVerification.Visibility=Visibility.Collapsed; }
        cts.Dispose();
    }
    internal async Task<bool> TrySaveSessionAsync(string path)
    {
        if(running is not null || snapshot is null || pendingMarks.Count>0) { Status.Text="Önce bölge arayın, süren işlemi bitirin ve bekleyen işaretleri uygulayın veya silin.";return false; }
        if(!int.TryParse(left.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var x) || !int.TryParse(top.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var y) ||
           !int.TryParse(width.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var w) || !int.TryParse(height.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var h))
        { Status.Text="Bölge kaydedilemedi: Bölge dikdörtgeni geçersiz.";return false; }
        var original=snapshot;var marks=appliedMarks;var revision=regionRevision;var cts=new CancellationTokenSource();BeginSessionOperation(cts);
        Status.Text="Bölge dosyası kaydediliyor…";
        try {
            var full=PhotoRegionSavePath.Complete(path);
            await Task.Run(()=> {
                BeforeSessionSaveForVerification?.Invoke(cts.Token);cts.Token.ThrowIfCancellationRequested();
                var text=PhotoRegionSession.Encode(original,marks,x,y,w,h,cts.Token);
                ProjectStorage.AtomicWriteIfUnchanged(full,text,new ProjectFileVersion(full,null),cts.Token);
            },cts.Token);
            if(!closed && revision==regionRevision)Status.Text="Bölge yeni dosyaya kaydedildi; fotoğraf veya kalıp içermez.";
            return true; // A commit completed even if cancellation arrived after it.
        } catch(OperationCanceledException) { if(!closed && revision==regionRevision)Status.Text="Bölge kaydı iptal edildi; hedef dosya tamamlanmadı.";return false; }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException or JsonException)
        { if(!closed && revision==regionRevision)Status.Text="Bölge kaydedilemedi: "+ex.Message;return false; }
        finally { EndSessionOperation(cts); }
    }
    private sealed record SessionOpenState(RegionProposal.Snapshot? Snapshot,MaskComponents? Components,byte[]? Applied,
        RegionProposal.Snapshot? Undo,MaskComponents? UndoComponents,byte[]? UndoApplied,object? Selection,
        bool? Omit,bool? Show,bool? Compare,KeyValuePair<int,bool>[] Pending,Dictionary<int,bool?>? PendingUndo,
        string[] Fields,long Revision,long ReviewRevision);
    private SessionOpenState CaptureSessionOpenState()=>new(snapshot,components,appliedMarks,undoSnapshot,undoComponents,undoAppliedMarks,
        Regions.SelectedItem,OmitHoles.IsChecked,ShowAppliedMarks.IsChecked,ComparePrevious.IsChecked,pendingMarks.ToArray(),pendingUndo,
        [left.Text,top.Text,width.Text,height.Text],regionRevision,reviewRevision);
    private bool SessionOpenStateMatches(SessionOpenState state,CancellationTokenSource? expected=null)=>!closed && ReferenceEquals(running,expected) && state.Revision==regionRevision && state.ReviewRevision==reviewRevision &&
        ReferenceEquals(snapshot,state.Snapshot) && ReferenceEquals(components,state.Components) && ReferenceEquals(appliedMarks,state.Applied) &&
        ReferenceEquals(undoSnapshot,state.Undo) && ReferenceEquals(undoComponents,state.UndoComponents) && ReferenceEquals(undoAppliedMarks,state.UndoApplied) &&
        ReferenceEquals(Regions.SelectedItem,state.Selection) && OmitHoles.IsChecked==state.Omit && ShowAppliedMarks.IsChecked==state.Show &&
        ComparePrevious.IsChecked==state.Compare && ReferenceEquals(pendingUndo,state.PendingUndo) && pendingMarks.Count==state.Pending.Length &&
        state.Pending.All(p=>pendingMarks.TryGetValue(p.Key,out var value) && value==p.Value) &&
        state.Fields.SequenceEqual(new[] { left.Text,top.Text,width.Text,height.Text });
    internal async Task<bool> PickAndLoadSessionAsync(Func<string?> choose)
    {
        if(closed || running is not null) { if(!closed)Status.Text="Önce süren işlemi bitirin.";return false; }
        var state=CaptureSessionOpenState();var path=choose();
        if(path is null)return false;
        if(!SessionOpenStateMatches(state)) { if(!closed)Status.Text="Dosya seçilirken çalışma değişti; dosya açılmadı.";return false; }
        return await TryLoadSessionAsync(path);
    }
    internal async Task<bool> TryLoadSessionAsync(string path)
    {
        if(running is not null) { Status.Text="Önce süren işlemi bitirin.";return false; }
        var beforeConfirmation=CaptureSessionOpenState();
        if(!ConfirmDiscard(ConfirmRectangleChangeForVerification,"Dosya açılırsa mevcut bölge ve işaretler değiştirilir. Devam edilsin mi?","Bölgeyi aç")) { Status.Text="Bölge açma iptal edildi; mevcut çalışma korundu.";return false; }
        if(!SessionOpenStateMatches(beforeConfirmation)) { if(!closed)Status.Text="Onay sırasında çalışma değişti; dosya açılmadı.";return false; }
        var revision=regionRevision;
        var cts=new CancellationTokenSource();BeginSessionOperation(cts);Status.Text="Bölge dosyası açılıyor; mevcut çalışma korunuyor…";
        try {
            var result=await Task.Run(()=> {
                BeforeSessionLoadForVerification?.Invoke(cts.Token);cts.Token.ThrowIfCancellationRequested();
                var bgr=PhotoRasterCapture.CaptureBgr(image,new Int32Rect(0,0,image.PixelWidth,image.PixelHeight),cts.Token);
                var data=PhotoRegionSession.Decode(path,bgr,image.PixelWidth,image.PixelHeight,cts.Token);
                var restored=new RegionProposal.Snapshot(data.Width,data.Height,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bgr)),data.Labels);
                var found=MaskComponents.Analyze(restored.CopyBinaryMask(),data.Width,data.Height,cts.Token);
                if(found.Components.Count>512)throw new InvalidDataException("Bölge sayısı seçim sınırını aşıyor.");
                return (Bgr:bgr,Data:data,Snapshot:restored,Components:found);
            },cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if(!SessionOpenStateMatches(beforeConfirmation,cts))
            { if(!closed && revision==regionRevision)Status.Text="Çalışma değişti; dosya uygulanmadı.";return false; }
            var data=result.Data;var boxes=new[] { left,top,width,height };var values=new[] { data.X,data.Y,data.RectangleWidth,data.RectangleHeight };
            changingRectangle=true;try { for(var i=0;i<boxes.Length;i++) { boxes[i].Text=values[i].ToString(CultureInfo.InvariantCulture);boxes[i].Tag=boxes[i].Text; } } finally { changingRectangle=false; }
            regionRevision++;snapshot=result.Snapshot;sourceBgr=result.Bgr;appliedMarks=data.AppliedMarks;undoSnapshot=null;undoComponents=null;undoAppliedMarks=null;pendingMarks.Clear();pendingUndo=null;
            UpdateMarksOverlay();UpdateBoxOverlay();ShowComponents(result.Components);Status.Text="Bölge aynı fotoğraf pikselleriyle açıldı; bölgeyi yeniden seçin. Kalıp veya ölçü doğrulanmadı."+(data.Version==1 ? " Eski dosyada içerik özeti yok; dosya değişiklikleri doğrulanamadı." : " İçerik özeti eşleşti; bu dijital imza değildir.");return true;
        } catch(OperationCanceledException) { if(!closed && revision==regionRevision)Status.Text="Bölge açma iptal edildi; mevcut çalışma korundu.";return false; }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Security.SecurityException or JsonException)
        { if(!closed && revision==regionRevision)Status.Text="Bölge açılamadı; mevcut çalışma korundu: "+ex.Message;return false; }
        finally { EndSessionOperation(cts); }
    }
    private void ShowComponents(MaskComponents result)
    {
        ResetComparison();components=result;SelectedContour=null;Regions.Items.Clear();overlay.Source=null;OmitHoles.IsChecked=false;
        foreach(var c in result.Components)Regions.Items.Add(new Choice(c.Id,c.Pixels,c.Left,c.Top,c.Right,c.Bottom));
        Status.Text=$"{result.Components.Count} bölge bulundu. Birini seçip sınırı ve varsa boşlukları inceleyin.";
    }
    internal bool TrySelectComponentAt(int x,int y)
    {
        if(running is not null || components is null || x<0 || y<0 || x>=image.PixelWidth || y>=image.PixelHeight)
        { Status.Text="Önce bölge arayın ve görüntü içinde bir konum seçin.";return false; }
        var id=components.ComponentAt(x,y);
        if(id==0) { Status.Text="Bu konum arka plan; mevcut bölge seçimi korunuyor.";return false; }
        var choice=Regions.Items.Cast<Choice>().FirstOrDefault(item=>item.Id==id);
        if(choice is null) { Status.Text="Bu bölge seçim listesinde bulunamadı.";return false; }
        if(Equals(Regions.SelectedItem,choice)) { Status.Text=ComparePrevious.IsChecked==true ? "Tüm maske farkı açık: yeşil eklenen, kırmızı çıkarılan piksellerdir." : "Bu bölge zaten seçili; mavi alanı inceleyin.";return true; }
        Regions.SelectedItem=choice;return true;
    }
    internal bool TryAddMark(int x,int y)=>TryAddStroke([new System.Windows.Point(x,y)]);
    internal bool TryAddStroke(IReadOnlyList<System.Windows.Point> stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if(running is not null || snapshot is null || MarkMode.SelectedIndex is not (1 or 2) || BrushSize.SelectedIndex is <0 or >2 || stroke.Count is <1 or >4096)
        { Status.Text="Önce bölge arayın ve eklenecek veya çıkarılacak işaret türünü seçin.";return false; }
        foreach(var p in stroke)if(!double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X!=Math.Truncate(p.X) || p.Y!=Math.Truncate(p.Y) || p.X<0 || p.Y<0 || p.X>=image.PixelWidth || p.Y>=image.PixelHeight)
        { Status.Text="Fırça işareti görüntü sınırları dışında.";return false; }
        var radius=BrushSize.SelectedIndex switch { 0=>0,1=>2,_=>4 };var newPixels=new HashSet<int>();var newCount=0;var work=0;
        bool AddDisk(int x,int y)
        {
            for(var yy=Math.Max(0,y-radius);yy<=Math.Min(image.PixelHeight-1,y+radius);yy++)for(var xx=Math.Max(0,x-radius);xx<=Math.Min(image.PixelWidth-1,x+radius);xx++)
            {
                if(++work>1_000_000)return false;
                var dx=xx-x;var dy=yy-y;if(dx*dx+dy*dy>radius*radius)continue;
                var index=yy*image.PixelWidth+xx;
                if(newPixels.Add(index) && !pendingMarks.ContainsKey(index) && ++newCount+pendingMarks.Count>4096)return false;
            }
            return true;
        }
        for(var i=0;i<stroke.Count;i++)
        {
            var x=(int)(i==0 ? stroke[0].X : stroke[i-1].X);var y=(int)(i==0 ? stroke[0].Y : stroke[i-1].Y);
            var ex=(int)stroke[i].X;var ey=(int)stroke[i].Y;var dx=Math.Abs(ex-x);var sx=x<ex ? 1 : -1;var dy=-Math.Abs(ey-y);var sy=y<ey ? 1 : -1;var error=dx+dy;
            while(true)
            {
                if(!AddDisk(x,y)) { Status.Text="Fırça sınırı aşıldı; bu hareketten işaret eklenmedi.";return false; }
                if(x==ex && y==ey)break;
                var twice=2*error;if(twice>=dy) { error+=dy;x+=sx; }if(twice<=dx) { error+=dx;y+=sy; }
            }
        }
        var undo=new Dictionary<int,bool?>(newPixels.Count);
        foreach(var index in newPixels) { undo[index]=pendingMarks.TryGetValue(index,out var old) ? old : null;pendingMarks[index]=MarkMode.SelectedIndex==1; }
        reviewRevision++;pendingUndo=undo;
        SelectedContour=null;UpdateMarksOverlay();
        Status.Text=$"{pendingMarks.Count} işaret bekliyor; İşaretleri uygula ile bölgeyi yeniden hesaplayın veya işaretleri silin.";return true;
    }
    internal void ClearMarks()
    {
        if(running is not null) { Status.Text="İşlem sürüyor; bitmesini bekleyin.";return; }
        reviewRevision++;pendingMarks.Clear();pendingUndo=null;UpdateMarksOverlay();Status.Text="Bekleyen işaretler silindi; mevcut bölge korunuyor.";
    }
    internal bool UndoPendingStroke()
    {
        if(running is not null || pendingUndo is null) { Status.Text="Geri alınacak bekleyen fırça hareketi yok.";return false; }
        foreach(var entry in pendingUndo)
            if(entry.Value is bool old)pendingMarks[entry.Key]=old;else pendingMarks.Remove(entry.Key);
        reviewRevision++;pendingUndo=null;SelectedContour=null;UpdateMarksOverlay();
        Status.Text=$"Son fırça hareketi geri alındı; {pendingMarks.Count} işaret bekliyor.";return true;
    }
    private void UpdateMarksOverlay()
    {
        var showApplied=ShowAppliedMarks.IsChecked==true && appliedMarks is not null;
        if(pendingMarks.Count==0 && !showApplied) { marksOverlay.Source=null;return; }
        var pixels=new byte[checked(image.PixelWidth*image.PixelHeight*4)];
        if(showApplied)
        {
            var appliedCount=0;var small=new List<int>(3);
            for(var i=0;i<appliedMarks!.Length;i++)if(appliedMarks[i]!=0) { appliedCount++;if(small.Count<3)small.Add(i); }
            if(appliedCount==0 && pendingMarks.Count==0) { marksOverlay.Source=null;return; }
            if(appliedCount is >0 and <=3)foreach(var index in small)
            {
                var x=index%image.PixelWidth;var y=index/image.PixelWidth;var value=appliedMarks[index];
                for(var yy=Math.Max(0,y-3);yy<=Math.Min(image.PixelHeight-1,y+3);yy++)for(var xx=Math.Max(0,x-3);xx<=Math.Min(image.PixelWidth-1,x+3);xx++)
                {
                    var p=(yy*image.PixelWidth+xx)*4;var border=Math.Abs(xx-x)==3 || Math.Abs(yy-y)==3;
                    pixels[p]=(byte)(border ? 255 : 0);pixels[p+1]=(byte)(border || value==1 ? 255 : 0);pixels[p+2]=(byte)(border || value==2 ? 255 : 0);pixels[p+3]=200;
                }
            }
            for(var i=0;i<appliedMarks!.Length;i++)
            {
                var value=appliedMarks[i];if(value==0)continue;var p=i*4;
                pixels[p]=0;pixels[p+1]=(byte)(value==1 ? 220 : 0);pixels[p+2]=(byte)(value==2 ? 255 : 0);pixels[p+3]=(byte)(appliedCount<=3 ? 200 : 125);
            }
        }
        if(pendingMarks.Count<=3)foreach(var mark in pendingMarks)
        {
            var x=mark.Key%image.PixelWidth;var y=mark.Key/image.PixelWidth;
            for(var yy=Math.Max(0,y-2);yy<=Math.Min(image.PixelHeight-1,y+2);yy++)for(var xx=Math.Max(0,x-2);xx<=Math.Min(image.PixelWidth-1,x+2);xx++)
            { var p=(yy*image.PixelWidth+xx)*4;pixels[p]=0;pixels[p+1]=(byte)(mark.Value ? 220 : 0);pixels[p+2]=(byte)(mark.Value ? 0 : 255);pixels[p+3]=255; }
        }
        foreach(var mark in pendingMarks)
        { var p=mark.Key*4;pixels[p]=0;pixels[p+1]=(byte)(mark.Value ? 220 : 0);pixels[p+2]=(byte)(mark.Value ? 0 : 255);pixels[p+3]=255; }
        var bitmap=BitmapSource.Create(image.PixelWidth,image.PixelHeight,image.DpiX,image.DpiY,PixelFormats.Bgra32,null,pixels,image.PixelWidth*4);bitmap.Freeze();marksOverlay.Source=bitmap;
    }
    internal async Task RefineAsync()
    {
        if(running is not null || snapshot is null || sourceBgr is null || pendingMarks.Count==0)
        { Status.Text="Önce bölge arayın ve en az bir düzeltme işareti ekleyin.";return; }
        var oldSnapshot=snapshot;var oldComponents=components;var bgr=sourceBgr;
        var marks=pendingMarks.Select(p=>new RegionProposal.Mark(p.Key%image.PixelWidth,p.Key/image.PixelWidth,p.Value)).ToArray();
        var cts=new CancellationTokenSource();running=cts;Status.Text="Düzeltmeler hesaplanıyor; eski bölge işlem bitene kadar korunur.";
        try
        {
            var result=await Task.Run(()=> {
                BeforeRefineForVerification?.Invoke(cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                var next=RegionProposal.Refine(bgr,image.PixelWidth,image.PixelHeight,oldSnapshot,marks,cts.Token);
                return (Snapshot:next,Components:MaskComponents.Analyze(next.CopyBinaryMask(),image.PixelWidth,image.PixelHeight,cts.Token));
            },cts.Token);
            cts.Token.ThrowIfCancellationRequested();if(closed)return;
            if(result.Components.Components.Count>512) { Status.Text="Düzeltme çok fazla bölge üretti; önceki bölge ve işaretler korundu.";return; }
            if(oldComponents is null)throw new InvalidOperationException("Önceki bölge durumu bulunamadı.");
            var nextApplied=appliedMarks is null ? new byte[checked(image.PixelWidth*image.PixelHeight)] : (byte[])appliedMarks.Clone();
            foreach(var mark in pendingMarks)nextApplied[mark.Key]=(byte)(mark.Value ? 1 : 2);
            undoSnapshot=oldSnapshot;undoComponents=oldComponents;undoAppliedMarks=appliedMarks;
            snapshot=result.Snapshot;appliedMarks=nextApplied;pendingMarks.Clear();pendingUndo=null;UpdateMarksOverlay();ShowComponents(result.Components);
            Status.Text+=" Düzeltme uygulandı; bölgeyi yeniden seçin.";
        }
        catch(OperationCanceledException) { if(!closed && ReferenceEquals(snapshot,oldSnapshot))Status.Text="Düzeltme iptal edildi; eski bölge korunur."; }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OpenCvSharp.OpenCVException or System.IO.IOException or DllNotFoundException or BadImageFormatException or TypeInitializationException)
        { if(!closed && ReferenceEquals(snapshot,oldSnapshot))Status.Text="Düzeltme uygulanamadı; eski bölge korunur: "+ex.Message; }
        finally { if(ReferenceEquals(running,cts))running=null;cts.Dispose(); }
    }
    internal bool UndoRefinement()
    {
        if(running is not null || pendingMarks.Count>0)
        { Status.Text="Önce süren işlemi bitirin ve bekleyen işaretleri uygulayın veya silin.";return false; }
        if(undoSnapshot is null || undoComponents is null)
        { Status.Text="Geri alınacak uygulanmış düzeltme yok.";return false; }
        snapshot=undoSnapshot;var restored=undoComponents;appliedMarks=undoAppliedMarks;undoSnapshot=null;undoComponents=null;undoAppliedMarks=null;
        UpdateMarksOverlay();ShowComponents(restored);Status.Text="Önceki bölge geri yüklendi; bölgeyi yeniden seçin.";return true;
    }
    private void ShowSelection()
    {
        SelectedContour=null;overlay.Source=null;OmitHoles.IsChecked=false;AcknowledgeImageEdge.IsChecked=false;AcknowledgeImageEdge.Visibility=Visibility.Collapsed;BoundaryWarning.Visibility=Visibility.Collapsed;BoundaryWarning.Text="";
        if(components is null || Regions.SelectedItem is not Choice choice)return;
        var touchesImageEdge=TouchesImageEdge(choice);var touchesSearchEdge=TouchesSearchRectangle(choice);
        if(touchesImageEdge || touchesSearchEdge) { AcknowledgeImageEdge.Content=touchesImageEdge && touchesSearchEdge ? "Görüntü ve arama sınırında kırpılma olasılığını inceledim" : touchesImageEdge ? "Görüntü kenarındaki sınırın kırpılmış olabileceğini inceledim" : "Arama dikdörtgenindeki sınırın kırpılmış olabileceğini inceledim";AcknowledgeImageEdge.Visibility=Visibility.Visible; }
        BoundaryWarning.Text=(touchesImageEdge ? "Bölge görüntü kenarına değiyor; gerçek sınır kırpılmış olabilir. " : "")+(touchesSearchEdge ? "Bölge arama dikdörtgenine değiyor; sınır dışında kalmış olabilir." : "");
        BoundaryWarning.Visibility=touchesImageEdge || touchesSearchEdge ? Visibility.Visible : Visibility.Collapsed;
        var selected=components.CopySelection([choice.Id]);var pixels=new byte[checked(selected.Length*4)];
        for(var i=0;i<selected.Length;i++)if(selected[i]!=0) { var p=i*4;pixels[p]=210;pixels[p+1]=190;pixels[p+2]=0;pixels[p+3]=100; }
        var mask=BitmapSource.Create(image.PixelWidth,image.PixelHeight,image.DpiX,image.DpiY,PixelFormats.Bgra32,null,pixels,image.PixelWidth*4);mask.Freeze();overlay.Source=mask;
        Status.Text=(ComparePrevious.IsChecked==true ? "Tüm maske farkı: yeşil eklenen, kırmızı çıkarılan piksel; değişmeyen alan görünmez. Bu bir doğruluk ölçümü değildir." : "Mavi alan seçilen bölgedir; diğer bölgeler aktarılmaz. Sınırın fotoğrafla uyumunu kontrol edin.");
    }
    private void ResetComparison()
    {
        ComparePrevious.IsChecked=false;
        ComparePrevious.IsEnabled=snapshot is not null && undoSnapshot is not null;
        comparisonOverlay.Source=null;overlay.Visibility=Visibility.Visible;
    }
    private void UpdateComparison()
    {
        comparisonOverlay.Source=null;overlay.Visibility=Visibility.Visible;
        if(ComparePrevious.IsChecked!=true || snapshot is null || undoSnapshot is null)
        {
            Status.Text=Regions.SelectedItem is Choice ? "Karşılaştırma kapatıldı; mavi alan seçilen bölgedir." : "Karşılaştırma kapatıldı; incelemek için bir bölge seçin.";
            return;
        }
        var current=snapshot.CopyBinaryMask();var previous=undoSnapshot.CopyBinaryMask();
        var pixels=new byte[checked(current.Length*4)];
        for(var i=0;i<current.Length;i++)
        {
            if(current[i]==previous[i])continue;
            var p=i*4;pixels[p+1]=(byte)(current[i]!=0 ? 220 : 0);pixels[p+2]=(byte)(current[i]==0 ? 255 : 0);pixels[p+3]=180;
        }
        var bitmap=BitmapSource.Create(image.PixelWidth,image.PixelHeight,image.DpiX,image.DpiY,PixelFormats.Bgra32,null,pixels,image.PixelWidth*4);bitmap.Freeze();
        comparisonOverlay.Source=bitmap;overlay.Visibility=Visibility.Hidden;
        Status.Text="Tüm maske farkı: yeşil eklenen, kırmızı çıkarılan piksel; değişmeyen alan görünmez. Bu bir doğruluk ölçümü değildir.";
    }
    internal byte[]? ComparisonPixelsForVerification()
    {
        if(comparisonOverlay.Source is not BitmapSource bitmap)return null;
        var pixels=new byte[checked(bitmap.PixelWidth*bitmap.PixelHeight*4)];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);return pixels;
    }
    private bool TouchesImageEdge(Choice choice)=>choice.Left==0 || choice.Top==0 || choice.Right==image.PixelWidth-1 || choice.Bottom==image.PixelHeight-1;
    private bool TouchesSearchRectangle(Choice choice)
    {
        if(components is null || !int.TryParse(left.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var x) ||
           !int.TryParse(top.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var y) ||
           !int.TryParse(width.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var w) ||
           !int.TryParse(height.Text,NumberStyles.None,CultureInfo.InvariantCulture,out var h) ||
           x<0 || y<0 || w<1 || h<1 || (long)x+w>image.PixelWidth || (long)y+h>image.PixelHeight)return false;
        var right=x+w-1;var bottom=y+h-1;
        for(var px=x;px<=right;px++)if(components.ComponentAt(px,y)==choice.Id || components.ComponentAt(px,bottom)==choice.Id)return true;
        for(var py=y+1;py<bottom;py++)if(components.ComponentAt(x,py)==choice.Id || components.ComponentAt(right,py)==choice.Id)return true;
        return false;
    }
    internal bool TrySelectContour()
    {
        if(pendingMarks.Count>0) { Status.Text="Önce bekleyen işaretleri uygulayın veya silin.";return false; }
        if(running is not null || components is null || Regions.SelectedItem is not Choice choice) { Status.Text="Önce arama tamamlanınca bir bölge seçin.";return false; }
        try
        {
            if((TouchesImageEdge(choice) || TouchesSearchRectangle(choice)) && AcknowledgeImageEdge.IsChecked!=true) { Status.Text="Bölge görüntü veya arama dikdörtgeni sınırına değiyor; gerçek sınır kırpılmış olabilir. İnceledikten sonra seçimi onaylayın.";return false; }
            var contours=MaskContours.Extract(components.CopySelection([choice.Id]),image.PixelWidth,image.PixelHeight);
            var outer=contours.Loops.Where(l=>!l.IsHole).ToArray();var holes=contours.Loops.Count(l=>l.IsHole);
            if(outer.Length!=1) { Status.Text="Seçilen bölgenin birden fazla dış sınırı var; otomatik seçim yapılmadı.";return false; }
            if(holes>0 && OmitHoles.IsChecked!=true) { Status.Text=$"Bölgede {holes} iç boşluk var. Bunlar çizgiye aktarılmayacak; devam için seçimi onaylayın.";return false; }
            var reduced=ContourCollinearReduction.Reduce(outer[0].CopyPoints());
            if(reduced.Length<3 || reduced.Length>PhotoTraceCanvas.MaximumPoints-4) { Status.Text="Sınırın nokta sayısı desteklenen aralıkta değil; mevcut çizim korunuyor.";return false; }
            SelectedContour=reduced.Select(p=>new System.Windows.Point(p.X,p.Y)).ToArray();return true;
        }
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OpenCvSharp.OpenCVException or System.IO.IOException or DllNotFoundException or BadImageFormatException or TypeInitializationException)
        { Status.Text="Sınır çıkarılamadı: "+ex.Message;return false; }
    }
}
