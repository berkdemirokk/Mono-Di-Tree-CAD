using System.Globalization;
using System.IO;
using System.Text;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TekstilCAD.Core;
using Geometry = TekstilCAD.Core.Geometry;

namespace TekstilCAD.Desktop;

public class MainWindow : Window
{
    private readonly DocumentSession session = new(Sample());
    // DocumentSession clones on every commit; this token changes only when the
    // whole project is replaced, even if a loaded project reuses a piece ID.
    private Guid projectEpoch = Guid.NewGuid();
    private long projectOpenGeneration;
    private bool projectOpenRunning;
    private Button? projectOpenCancel;
    internal Action? BeforeProjectReadForVerification { get; set; }
    internal Func<bool>? ConfirmOpenDrawingForVerification { get; set; }
    internal Func<(bool Accepted, bool Saved)>? ConfirmOpenDiscardForVerification { get; set; }
    internal Action<Exception>? ProjectOpenErrorForVerification { get; set; }
    public ProjectDocument Document => session.Document;
    public Guid? SelectedId { get => session.SelectedId; private set => session.SelectedId = value; }
    public PatternPiece? Selected => Document.Pieces.FirstOrDefault(p => p.Id == SelectedId);
    public string ActiveTool { get; private set; } = "Seç";
    public bool Snap => snap.IsChecked == true;
    private readonly CadCanvas canvas;
    private readonly ListBox pieces = new();
    private readonly TextBox pieceSearch = new() { MaxLength=1024, Margin=new Thickness(3,2,3,3), Padding=new Thickness(7,5,7,5), MinWidth=0 };
    private readonly TextBlock pieceSearchStatus = new() { TextWrapping=TextWrapping.Wrap, Foreground=StudioTheme.Muted, Margin=new Thickness(4,2,4,6) };
    private string pieceSearchQuery = "";
    private readonly List<(PatternPiece Piece, Guid Id, string Name)> displayedPieces = [];
    private readonly TextBox selectedPieceIdentity = new() { IsReadOnly=true, TextWrapping=TextWrapping.Wrap, AcceptsReturn=true, MaxHeight=64, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Margin=new Thickness(3,5,3,0) };
    private SimulationQueue? machineQueue;
    private Button? machineQueueCommand;
    private readonly TextBox machineQueueMessage = new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=64,Width=220,Margin=new Thickness(3,2,3,3),Visibility=Visibility.Collapsed };
    internal Func<SimulationQueue> MachineQueueFactory { get; set; } = () => new SimulationQueue(AppPaths.MachineSpoolDirectory);
    internal bool MachineQueueLoading { get; private set; }
    private readonly TextBlock status = new(), metrics = new(), projectTitle = new();
    private readonly TextBlock grainlineStatus = new() { TextWrapping=TextWrapping.Wrap, Foreground=StudioTheme.Muted, Margin=new Thickness(3,5,3,8) };
    private readonly TextBlock historyCapacityNotice = new() { Visibility=Visibility.Collapsed, Foreground=StudioTheme.Warning, FontSize=11, TextWrapping=TextWrapping.Wrap, Focusable=true, Margin=new Thickness(10,0,10,5) };
    private long historyCapacityBaseline, historyCapacityAnnounced;
    private readonly TextBlock canvasError = new() { TextWrapping=TextWrapping.Wrap, Foreground=StudioTheme.Danger };
    private readonly ScrollViewer canvasErrorViewer = new() { MaxHeight=54, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Focusable=true };
    private readonly DockPanel canvasErrorPanel = new() { Visibility=Visibility.Collapsed, Margin=new Thickness(18,2,18,2) };
    private readonly ScrollViewer statusViewer = new() { MaxHeight=44, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Focusable=true, VerticalAlignment=VerticalAlignment.Center };
    private readonly TextBlock saveState = new(), canvasTool = new(), pieceCount = new();
    private readonly TextBlock exportNotice = new() { Visibility=Visibility.Collapsed, FontSize=11, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,4,0,0), Focusable=true };
    private readonly TextBlock nestingHistoryNotice = new() { Visibility=Visibility.Collapsed, FontSize=11, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,4,0,0), Focusable=true };
    private readonly Button nestingHistoryDetails = new() { Content="Pastal geçmişini incele…", Visibility=Visibility.Collapsed, HorizontalAlignment=HorizontalAlignment.Left, Padding=new Thickness(4,2,4,2), Margin=new Thickness(0,2,0,0) };
    private ProjectDocument? nestingHistoryDocument;
    private long nestingHistoryGeneration;
    private readonly Button exportCancel = new() { Content="HPGL aktarımını iptal et", Visibility=Visibility.Collapsed, HorizontalAlignment=HorizontalAlignment.Left, Padding=new Thickness(4,2,4,2), Margin=new Thickness(0,2,0,0) };
    private readonly TextBlock pieceLimits = new() { Visibility=Visibility.Collapsed, Foreground=StudioTheme.Warning, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(4,4,4,10), Focusable=true };
    private readonly TextBlock gradingGeometryNotice = new() { Visibility=Visibility.Collapsed, Foreground=StudioTheme.Warning, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(4,4,4,10), Focusable=true };
    private readonly TextBox propertyDraftConflict = new() { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=100,Margin=new Thickness(3,4,3,8),Visibility=Visibility.Collapsed };
    private readonly TextBlock recoveryNotice = new() { Visibility=Visibility.Collapsed, Foreground=StudioTheme.Warning, FontSize=11, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,4,0,0) };
    private readonly Button recoveryDetails = new() { Content="Kurtarma uyarısı ayrıntıları", Visibility=Visibility.Collapsed, HorizontalAlignment=HorizontalAlignment.Left, Padding=new Thickness(4,2,4,2), Margin=new Thickness(0,2,0,0) };
    private readonly TextBlock recoveryScanNotice = new() { Visibility=Visibility.Collapsed, Foreground=StudioTheme.Warning, FontSize=11, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,4,0,0) };
    private readonly Button recoveryScanDetails = new() { Content="Tarama hatası ayrıntıları", Visibility=Visibility.Collapsed, HorizontalAlignment=HorizontalAlignment.Left, Padding=new Thickness(4,2,4,2), Margin=new Thickness(0,2,0,0) };
    private readonly Button recoveryOpen = new() { Content="Kurtarma kayıtlarını incele", Visibility=Visibility.Collapsed, HorizontalAlignment=HorizontalAlignment.Left, Padding=new Thickness(4,2,4,2), Margin=new Thickness(0,2,0,0) };
    private RecoveryScanResult? recoveryScan;
    private RecoveryScanCursor? recoveryCursor;
    private bool recoveryHasMore;
    private const int RecoveryPageHistoryLimit = 20;
    private readonly List<RecoveryScanPage> recoveryPages = [];
    private int recoveryPageIndex = -1, recoveryFirstPageNumber = 1;
    private readonly CancellationTokenSource recoveryScanCancellation = new();
    private bool recoveryScanRunning;
    private Button? recoveryRescan;
    private readonly List<Button> selectionCommands = [];
    private Button? undoCommand, redoCommand, exportCommand, fitCommand;
    private Button? grainReviewExportCommand;
    internal Func<DxfGrainlineReviewPlan,bool>? ConfirmDxfGrainlineExportForVerification { get; set; }
    internal Action? BeforeGrainlineExportPreparationForVerification { get; set; }
    private readonly TextBox name = new(), x = new(), y = new();
    private readonly CheckBox snap = new() { Content = "1 mm ızgaraya yakala", IsChecked = true };
    private readonly Dictionary<string, Button> toolButtons = [];
    private string? filePath;
    private ProjectFileVersion? fileVersion;
    private bool savedFileHasGradingRules;
    private bool savedFileHasMetadata;
    private bool savedFileHasNestingSnapshot;
    private bool savedFileHasGrainline;
    private bool savedFileHasImportProvenance;
    private RecoveredDocument? recoveredSource;
    // A saved project can still have a pending drawing omitted from that file.
    // Defer archival of its original recovery record until that drawing is resolved.
    private bool recoveredDocumentSaved;
    // An interrupted recovery may have loaded the document without all of its
    // pending UI state. Its only complete source must never be auto-archived.
    private bool recoveryRestoreIncomplete;
    // A committed main-window edit whose presentation failed needs an explicit
    // successful refresh before another potentially repeated edit is allowed.
    private bool mainMutationReviewRequired;
    private RecoveryStore? recoveryStore;
    private RecoveryWriteCoordinator? recoveryWriter;
    private long recoveryGeneration;
    private long recoveryRequest, recoveryReported;
    private bool recoveryMayExist, recoveryClearFailed, recoveryClearWarning, recoveryClosing, recoveryCloseReady;
    private long recoveryClearRequest, recoveryClearReported;
    private DateTime? lastRecoveryAt;
    private bool recoverySaveWarning;
    private readonly RecoveryWarningState recoveryWarnings = new();
    private bool syncing;
    private bool dirty;
    private sealed record PropertyDraft(string Name, string X, string Y, string BaseName, double BaseX, double BaseY);
    private readonly Dictionary<Guid, PropertyDraft> propertyDrafts = [];
    private bool HasUnsavedDocumentWork => dirty || propertyDrafts.Count > 0;
    private bool HasUnsavedWork => HasUnsavedDocumentWork || (canvas?.HasPendingDrawing ?? false);
    internal bool HasPendingCanvasOrPropertyWork => propertyDrafts.Count>0 || (canvas is not null && (canvas.HasPendingDrawing || canvas.HasActiveDragPreview || canvas.LargeVertexValidationRunning || canvas.LargeCompletionRunning));
    private Guid? areaPieceId;
    private ProjectDocument? areaDocument;
    private Point2[]? areaPoints;
    private string? areaResult;
    private long areaGeneration;
    private (Point2[] Points, long Generation)? pendingArea;
    private bool areaWorkerRunning;
    private bool windowClosed;
    internal bool ExportRunning { get; private set; }
    internal bool DxfImportRunning { get; private set; }
    internal Action? BeforeDxfPreparationForVerification { get; set; }
    internal Action? AfterDxfPreparationForVerification { get; set; }
    internal Action? BeforeDxfSourceCheckForVerification { get; set; }
    internal Action? AfterDxfSourceCheckForVerification { get; set; }
    internal Func<DxfImportResult,bool>? ConfirmDxfReviewForVerification { get; set; }
    internal Func<DxfImportResult,IReadOnlyList<DxfGrainlineSelection>?>? ConfirmDxfGrainlineReviewForVerification { get; set; }
    internal Func<PatternPiece,int,bool>? ConfirmDeleteWithGradingForVerification { get; set; }
    internal Action<Window>? MetadataDialogSuccessForVerification { get; set; }
    internal Func<(bool Accepted,bool Saved)>? DxfDiscardDecisionForVerification { get; set; }
    private CancellationTokenSource? dxfImportCancellation;
    private Button? dxfImportCommand;
    private DxfPreparationStamp? dxfPreparationStamp;
    private DxfImportResult? preparedDxf;
    private readonly Button dxfReview = new() { Content="DXF önizlemesini aç", Visibility=Visibility.Collapsed, Margin=new Thickness(0,4,0,0) };
    private readonly Button dxfCancel = new() { Content="DXF okumayı iptal et", Visibility=Visibility.Collapsed, HorizontalAlignment=HorizontalAlignment.Left, Padding=new Thickness(4,2,4,2), Margin=new Thickness(0,2,0,0) };
    private readonly TextBlock dxfNotice = new() { Visibility=Visibility.Collapsed, FontSize=11, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,4,0,0), Focusable=true };
    private readonly TextBlock sourceHistoryNotice = new() { TextWrapping=TextWrapping.Wrap, FontSize=11, Margin=new Thickness(0,4,0,0), Focusable=true };
    private readonly Button sourceHistoryDetails = new() { Content="DXF kaynak ve aktarım notları", Margin=new Thickness(0,4,0,0), HorizontalAlignment=HorizontalAlignment.Left };
    private double preparedDxfFactor;
    private string preparedDxfSource="";
    private DxfTextEncoding? preparedDxfEncoding;
    private bool preparedDxfSavedState;
    private bool dxfSourceCheckCancelled;
    private bool dxfReviewActive;
    private string? preparedDxfPath;
    private sealed record DxfPreparationStamp(ProjectDocument Original, ProjectDocument Snapshot,
        Dictionary<Guid,PropertyDraft> Drafts, RecoveryPendingDrawing? Drawing, Guid? Selection,
        int UndoCount, int RedoCount, bool Dirty, string? FilePath, ProjectFileVersion? FileVersion);
    internal Task? ExportTask { get; private set; }
    internal string? ExportError { get; private set; }
    internal bool ExportWasCancelled { get; private set; }
    internal Action? BeforeHpglPreparationForVerification { get; set; }
    internal Action? AfterHpglPreparationForVerification { get; set; }
    internal Func<HpglPreparedOutput,IReadOnlyList<PatternPiece>,bool>? ConfirmHpglReviewForVerification { get; set; }
    private CancellationTokenSource? exportCancellation;
    internal bool IsWindowClosed => windowClosed;
    private readonly DispatcherTimer autosaveTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private static Brush Panel => StudioTheme.Surface;
    private static Brush Ink => StudioTheme.Text;
    private static Brush Muted => StudioTheme.Muted;
    private const string SourceHistoryNotice = "Kaynak ve önceki aktarım uyarıları projede saklanmaz. Üretimden önce ölçüleri ve yerleşimi kaynakla karşılaştırın.";

    public MainWindow()
    {
        Title = AppPaths.IsIsolatedPreview ? "Mono Di Tree CAD · Geliştirme Önizlemesi" : "Mono Di Tree CAD · 0.4 Önizleme"; Width = 1440; Height = 900; MinWidth = 1024; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        StudioTheme.Apply(this);
        var root = new DockPanel { LastChildFill = true }; Content = root;

        // Project identity and save state remain visible independently of the inspector.
        var header = new Grid { Margin = new Thickness(22, 16, 22, 16) };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new StackPanel { Margin = new Thickness(0, 0, 28, 0) };
        brand.Children.Add(new TextBlock { Text = "Mono Di Tree CAD", Foreground = StudioTheme.Accent, FontWeight = FontWeights.Bold, FontSize = 22 });
        brand.Children.Add(new TextBlock { Text = "STUDIO  /  KALIP TASARIMI", Foreground = Muted, FontSize = 9, Margin = new Thickness(1, 3, 0, 0) });
        header.Children.Add(brand);
        var project = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        projectTitle.FontSize = 15; projectTitle.FontWeight = FontWeights.SemiBold; projectTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        projectTitle.TextWrapping = TextWrapping.NoWrap; projectTitle.MaxHeight = 24;
        project.Children.Add(projectTitle);
        saveState.FontSize = 11; saveState.Margin = new Thickness(0, 5, 0, 0); project.Children.Add(saveState);
        // This format does not retain import provenance. Keep the limitation visible
        // after opening, editing, or saving a project without claiming that its geometry is unsafe.
        sourceHistoryNotice.Text=SourceHistoryNotice;sourceHistoryNotice.Foreground=StudioTheme.Muted;
        AutomationProperties.SetName(sourceHistoryNotice,SourceHistoryNotice);
        AutomationProperties.SetHelpText(sourceHistoryNotice,"Dışa aktarma ve simülasyon öncesinde, varsa özgün dosyayla ölçü ve yerleşimi kontrol edin. Bu bilgi doğrulanmış üretim onayı değildir.");
        project.Children.Add(sourceHistoryNotice);
        AutomationProperties.SetName(sourceHistoryDetails,"DXF kaynak ve aktarım notlarını incele");
        sourceHistoryDetails.Click+=(_,_)=>CreateDxfSourceHistoryWindow().ShowDialog();
        project.Children.Add(sourceHistoryDetails);
        System.Windows.Automation.AutomationProperties.SetName(nestingHistoryNotice,"Pastal geçmişi durumu");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(nestingHistoryNotice,System.Windows.Automation.AutomationLiveSetting.Polite);
        project.Children.Add(nestingHistoryNotice);
        System.Windows.Automation.AutomationProperties.SetName(nestingHistoryDetails,"Pastal geçmişini incele");
        nestingHistoryDetails.Click+=(_,_)=> { if(Document.NestingSnapshot is not null) CreateNestingHistoryWindow().ShowDialog(); };
        project.Children.Add(nestingHistoryDetails);
        System.Windows.Automation.AutomationProperties.SetName(exportNotice,"Dışa aktarma durumu");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(exportNotice,System.Windows.Automation.AutomationLiveSetting.Polite);
        project.Children.Add(exportNotice);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(dxfNotice,System.Windows.Automation.AutomationLiveSetting.Polite);
        project.Children.Add(dxfNotice);
        System.Windows.Automation.AutomationProperties.SetName(dxfCancel,"DXF okumayı iptal et");
        project.Children.Add(dxfCancel);
        dxfCancel.Click += (_,_) => RequestDxfCancellation();
        project.Children.Add(dxfReview);
        dxfReview.Click += (_,_) => ReviewPreparedDxf();
        System.Windows.Automation.AutomationProperties.SetName(exportCancel,"HPGL dışa aktarımını iptal et");
        project.Children.Add(exportCancel);
        exportCancel.Click += (_, _) => RequestExportCancellation();
        project.Children.Add(recoveryNotice);
        project.Children.Add(recoveryDetails);
        project.Children.Add(recoveryScanNotice);
        AutomationProperties.SetLiveSetting(recoveryScanNotice,AutomationLiveSetting.Polite);
        AutomationProperties.SetName(recoveryScanDetails,"Kurtarma taraması hatası ayrıntıları");
        project.Children.Add(recoveryScanDetails);
        project.Children.Add(recoveryOpen);
        recoveryDetails.Click += (_, _) => MessageBox.Show(this, recoveryNotice.ToolTip as string ?? "Ayrıntı bulunmuyor.", "Kurtarma uyarısı ayrıntıları", MessageBoxButton.OK, MessageBoxImage.Information);
        recoveryScanDetails.Click += (_, _) => MessageBox.Show(this, recoveryScanNotice.ToolTip as string ?? "Ayrıntı bulunmuyor.", "Kurtarma taraması hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
        recoveryOpen.Click += (_, _) => OfferRecovery();
        Grid.SetColumn(project, 1); header.Children.Add(project);
        var mode = new Border { Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(12, 8, 12, 8), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = "YEREL ÇALIŞMA  ·  mm", Foreground = Muted, FontSize = 10 } };
        Grid.SetColumn(mode, 2); header.Children.Add(mode);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

        var commandArea = new WrapPanel { Margin = new Thickness(16, 0, 16, 12) };
        var files = CommandGroup(commandArea, "PROJE");
        AddButton(files, "Yeni", NewDocument, "Yeni proje · Ctrl+N");
        AddButton(files, "Aç…", Open, "Proje aç · Ctrl+O");
        projectOpenCancel=AddButton(files,"Açmayı iptal et",CancelProjectOpen,"Okuma sonucunu kullanma; mevcut çalışma korunur.");
        projectOpenCancel.Visibility=Visibility.Collapsed;
        var saveButton = AddButton(files, "Kaydet", Save, "Kaydet · Ctrl+S", projectSaveAction:true); saveButton.Background = StudioTheme.Accent; saveButton.Foreground = StudioTheme.Background;
        AddButton(files, "Farklı kaydet…", SaveAs, "Farklı kaydet · Ctrl+Shift+S", projectSaveAction:true);
        AddButton(files, "Yeni adla kaydet…", SaveNewCopy, "Yeni bir dosya oluşturarak kaydet", projectSaveAction:true);
        recoveryRescan = AddButton(files, "Kurtarma tara", async () => await BeginRecoveryScan(), "Kurtarma kayıtlarını yeniden tara; açık proje değiştirilmez.");
        AddButton(files, "Parça ara", FocusPieceSearch, "Parça adı veya kimliğiyle arama alanına git");
        var exchange = CommandGroup(commandArea, "DOSYA AKTARIMI");
        dxfImportCommand=AddButton(exchange, "DXF al…", ImportDxf, "DXF dosyasından geometri aktar");
        exportCommand = AddButton(exchange, "Tümünü dışa aktar…", Export, "Projedeki tüm parçaları DXF, SVG, HPGL veya PDF inceleme dosyasına aktar. Kaynak geçmişi projede saklanmaz; üretimden önce ölçüleri ve yerleşimi kontrol edin. Üretim parçası seçmek için Makine simülatörünü kullanın.");
        grainReviewExportCommand=AddButton(exchange,"İplik işaretli DXF inceleme dosyası…",ExportGrainlineReview,"Yalnız inceleme için; işaret çizgileri alıcı yazılımda kesim yolu olarak algılanabilir.");
        var textile = CommandGroup(commandArea, "TEKSTİL");
        AddButton(textile, "Tekstil araçları…", OpenTextileTools, "Serileme, dikiş payı, nokta düzenleme ve temel pastal");
        var machineControls=new StackPanel();textile.Children.Add(machineControls);
        machineQueueCommand=AddButton(machineControls, "Makine simülatörü…", OpenMachineCenter, "Dosya aktarım simülasyonu; fiziksel makine bağlantısı yok. Kaynak geçmişi projede saklanmaz; üretimden önce ölçüleri ve yerleşimi kontrol edin.");
        AutomationProperties.SetName(machineQueueMessage,"Makine simülatörü hazırlık durumu");
        AutomationProperties.SetHelpText(machineQueueMessage,"Durum ve hata ayrıntılarını seçip kopyalayabilirsiniz; uzun metni kaydırın.");
        AutomationProperties.SetLiveSetting(machineQueueMessage,AutomationLiveSetting.Polite);
        machineControls.Children.Add(machineQueueMessage);
        DockPanel.SetDock(commandArea, Dock.Top); root.Children.Add(commandArea);

        var footer = new DockPanel { LastChildFill = true };
        var units = new TextBlock { Text = "MILIMETRE   /   1 mm yakalama", Foreground = Muted, FontSize = 10, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(units, Dock.Right); footer.Children.Add(units);
        status.Foreground = Muted; status.FontSize = 11; status.TextWrapping = TextWrapping.Wrap;
        System.Windows.Automation.AutomationProperties.SetName(status,"Uygulama durumu");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status,System.Windows.Automation.AutomationLiveSetting.Polite);
        System.Windows.Automation.AutomationProperties.SetName(statusViewer,"Durum mesajı ayrıntıları");
        System.Windows.Automation.AutomationProperties.SetHelpText(statusViewer,"Uzun mesajları okumak için yön tuşlarıyla kaydırın.");
        KeyboardNavigation.SetIsTabStop(statusViewer,true);
        statusViewer.Content=status; footer.Children.Add(statusViewer);
        var statusBar = new Border { Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 9, 18, 9), Child = footer };
        DockPanel.SetDock(statusBar, Dock.Bottom); root.Children.Add(statusBar);
        var dismissCanvasError = new Button { Content="Çizim uyarısını kapat", Padding=new Thickness(8,4,8,4), Margin=new Thickness(8,0,0,0), VerticalAlignment=VerticalAlignment.Center };
        dismissCanvasError.Click+=(_,_)=>ClearCanvasError();
        DockPanel.SetDock(dismissCanvasError,Dock.Right);canvasErrorPanel.Children.Add(dismissCanvasError);
        System.Windows.Automation.AutomationProperties.SetName(canvasError,"Çizim işlemi uyarısı");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(canvasError,System.Windows.Automation.AutomationLiveSetting.Polite);
        System.Windows.Automation.AutomationProperties.SetName(canvasErrorViewer,"Çizim uyarısı ayrıntıları");
        System.Windows.Automation.AutomationProperties.SetHelpText(canvasErrorViewer,"Uzun uyarıyı okumak için yön tuşlarıyla kaydırın.");
        KeyboardNavigation.SetIsTabStop(canvasErrorViewer,true);canvasErrorViewer.Content=canvasError;canvasErrorPanel.Children.Add(canvasErrorViewer);
        DockPanel.SetDock(canvasErrorPanel,Dock.Bottom);root.Children.Add(canvasErrorPanel);

        var workspace = new Grid { Margin = new Thickness(12, 0, 12, 12) };
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(146) });
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(270) });
        root.Children.Add(workspace);

        var rail = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var tools = new StackPanel(); DockPanel.SetDock(tools, Dock.Top); rail.Children.Add(tools);
        tools.Children.Add(Label("ÇİZİM ARAÇLARI", Muted, 10));
        foreach (var (tool, glyph, shortcut) in new[] { ("Seç", "↖", "V"), ("Çizgi", "╱", "L"), ("Çoklu çizgi", "⌁", "P"), ("Dikdörtgen", "▭", "R"), ("Bézier", "∿", "B"), ("Yay", "◠", "A") })
        {
            var selectedTool = tool;
            var button = AddButton(tools, tool, () => { SetTool(selectedTool); if(ActiveTool==selectedTool)canvas?.Focus(); }, tool + " · " + shortcut + " (kalıp alanı odaktayken)");
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(10, 10, 8, 10);
            var item = new Grid(); item.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); item.ColumnDefinitions.Add(new()); item.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            item.Children.Add(new TextBlock { Text = glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            var label = new TextBlock { Text = tool, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(label, 1); item.Children.Add(label);
            var key = new TextBlock { Text = shortcut, FontSize = 10, Opacity = .65, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(key, 2); item.Children.Add(key);
            button.Content = item;
            System.Windows.Automation.AutomationProperties.SetName(button, tool);
            toolButtons[tool] = button;
        }
        AddButton(tools, "Koordinatla nokta gir…", OpenCoordinateDrawing, "Önce çizim aracını seçin; X ve Y koordinatlarını milimetre olarak girin.");
        tools.Children.Add(new Border { Height = 1, Background = StudioTheme.Border, Margin = new Thickness(3, 14, 3, 10) });
        selectionCommands.Add(AddButton(tools, "Parçayı çoğalt…", DuplicateSelectedPiece, "Geometriyi aynı konumda kopyalar; parça bilgileri, iplik yönü ve uygun serileme kuralı isteğe bağlıdır. Pastal geçmişi kopyalanmaz."));
        selectionCommands.Add(AddButton(tools, "Parçayı sil", DeleteSelectionFromKeyboard, "Seçili parçayı sil · Delete"));
        tools.Children.Add(Label("GÖRÜNÜM", Muted, 10));
        selectionCommands.Add(AddButton(tools, "Seçili parçayı göster", () => canvas?.FitSelectedPiece(), "Seçili parçayı çalışma alanına sığdır · G (kalıp alanı odaktayken)"));
        var navigation = new WrapPanel(); tools.Children.Add(navigation);
        void ViewButton(string glyph,string label,Action action,string hint)
        {
            var button=AddButton(navigation,glyph,action,hint);
            AutomationProperties.SetName(button,label);
        }
        ViewButton("+","Görünümü yakınlaştır",()=>canvas?.ZoomView(true),"Yakınlaştır · Page Up");
        ViewButton("−","Görünümü uzaklaştır",()=>canvas?.ZoomView(false),"Uzaklaştır · Page Down");
        ViewButton("←","Görünümü sola kaydır",()=>canvas?.PanView(80,0),"Sola kaydır · Alt+Sol");
        ViewButton("→","Görünümü sağa kaydır",()=>canvas?.PanView(-80,0),"Sağa kaydır · Alt+Sağ");
        ViewButton("↑","Görünümü yukarı kaydır",()=>canvas?.PanView(0,80),"Yukarı kaydır · Alt+Yukarı");
        ViewButton("↓","Görünümü aşağı kaydır",()=>canvas?.PanView(0,-80),"Aşağı kaydır · Alt+Aşağı");
        var hints = new TextBlock { Text = "GEZİNME\n\nTekerlek  Yakınlaş\nOrta tuş  Kaydır\n\nKalıp alanı odaktayken:\nPage Up / Down  Yakınlık\nAlt + oklar  Görünümü kaydır\nG  Seçili parçayı göster\nEnter  Konturu kapat\nBackspace  Son çizim noktasını kaldır\nEsc  Çizimi iptal\nDelete  Parçayı sil", Foreground = Muted, FontSize = 10, LineHeight = 18, Margin = new Thickness(7, 24, 0, 0), TextWrapping = TextWrapping.Wrap };
        tools.Children.Add(hints);
        var railScroller = new ScrollViewer { Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        workspace.Children.Add(railScroller);

        var inspector = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        inspector.Children.Add(Label("PARÇA ÖZELLİKLERİ", Muted, 10));
        System.Windows.Automation.AutomationProperties.SetName(gradingGeometryNotice,"Kayıtlı serileme kuralı eski geometriye ait. Tekstil araçlarında artışları gözden geçirin.");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(gradingGeometryNotice,System.Windows.Automation.AutomationLiveSetting.Polite);
        inspector.Children.Add(gradingGeometryNotice);
        System.Windows.Automation.AutomationProperties.SetName(pieceLimits, "Seçili parça işlem sınırları");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(pieceLimits, System.Windows.Automation.AutomationLiveSetting.Polite);
        inspector.Children.Add(pieceLimits);
        System.Windows.Automation.AutomationProperties.SetName(propertyDraftConflict,"Seçili parça taslak çakışması ve mevcut değerler");
        System.Windows.Automation.AutomationProperties.SetHelpText(propertyDraftConflict,"Uyarıyı ve mevcut değerleri seçip kopyalayabilirsiniz. Uzun metni kaydırın; kontrol karakterleri Unicode koduyla gösterilir.");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(propertyDraftConflict,System.Windows.Automation.AutomationLiveSetting.Polite);
        inspector.Children.Add(propertyDraftConflict);
        inspector.Children.Add(Label("Parça adı")); StyleInput(name); inspector.Children.Add(name);
        inspector.Children.Add(Label("Konum · sol üst köşe (mm)"));
        var coords = new Grid(); coords.ColumnDefinitions.Add(new()); coords.ColumnDefinitions.Add(new());
        var xGroup = new StackPanel { Margin = new Thickness(0, 0, 5, 0) }; xGroup.Children.Add(Label("X", Muted, 10)); StyleInput(x); xGroup.Children.Add(x);
        var yGroup = new StackPanel { Margin = new Thickness(5, 0, 0, 0) }; yGroup.Children.Add(Label("Y", Muted, 10)); StyleInput(y); yGroup.Children.Add(y);
        coords.Children.Add(xGroup); Grid.SetColumn(yGroup, 1); coords.Children.Add(yGroup); inspector.Children.Add(coords);
        System.Windows.Automation.AutomationProperties.SetName(name, "Parça adı");
        System.Windows.Automation.AutomationProperties.SetName(x, "X konumu milimetre");
        System.Windows.Automation.AutomationProperties.SetName(y, "Y konumu milimetre");
        selectionCommands.Add(AddButton(inspector, "Konumu ve adı uygula", ApplyProperties));
        AddButton(inspector, "Alan taslaklarını incele…", ReviewPropertyDrafts, "Taslakları tek tek inceleyin ve seçtiğiniz taslağı kaldırın");
        AddButton(inspector, "Alan taslaklarını temizle", ClearPropertyDrafts, "Uygulanmamış ad ve konum taslaklarının tümünü sil");
        name.TextChanged += (_, _) => CapturePropertyDraft();
        x.TextChanged += (_, _) => CapturePropertyDraft();
        y.TextChanged += (_, _) => CapturePropertyDraft();
        selectionCommands.Add(AddButton(inspector, "Parça bilgileri…", EditPieceMetadata, "Adetli pastal kesim rolünü, adedi ve malzemeyi kullanır. Temel pastal her parçayı bir kez yerleştirir; makine çıktısı bu bilgileri taşımaz."));
        selectionCommands.Add(AddButton(inspector,"İplik yönü…",EditGrainline,"Seçili parçanın iplik yönü çizgisini girin veya kaldırın. SVG çizgiyi görsel olarak içerir; işaretli parçaların DXF ve HPGL çıktısı henüz desteklenmez."));
        AutomationProperties.SetName(grainlineStatus,"İplik yönü durumu");
        AutomationProperties.SetLiveSetting(grainlineStatus,AutomationLiveSetting.Polite);
        inspector.Children.Add(grainlineStatus);
        var transforms = new UniformGrid { Columns = 2 }; inspector.Children.Add(transforms);
        selectionCommands.Add(AddButton(transforms, "90° döndür", () => Transform((points,center) => Geometry.Rotate(points,90,center)), "Parçayı merkezinden saat yönünün tersine döndür"));
        var rotateClockwise=AddButton(transforms, "90° sağa", () => Transform((points,center) => Geometry.Rotate(points,-90,center)), "Parçayı merkezinden saat yönünde 90 derece döndür");
        AutomationProperties.SetName(rotateClockwise,"Parçayı saat yönünde 90 derece döndür");
        selectionCommands.Add(rotateClockwise);
        selectionCommands.Add(AddButton(transforms, "Aynala", () => Transform((points,center) => Geometry.Mirror(points,true,center.X)), "Parçayı merkezinden geçen dikey eksene göre aynala"));
        var mirrorHorizontal=AddButton(transforms, "Yatay aynala", () => Transform((points,center) => Geometry.Mirror(points,false,center.Y)), "Parçayı merkezinden geçen yatay eksene göre aynala");
        AutomationProperties.SetName(mirrorHorizontal,"Parçayı yatay eksene göre aynala");
        selectionCommands.Add(mirrorHorizontal);
        metrics.Foreground = Ink; metrics.FontSize = 12; metrics.LineHeight = 22; metrics.TextWrapping = TextWrapping.Wrap;
        inspector.Children.Add(new Border { Background = StudioTheme.Raised, CornerRadius = new CornerRadius(5), Padding = new Thickness(12), Margin = new Thickness(3, 14, 3, 14), Child = metrics });
        snap.Margin = new Thickness(4, 4, 0, 14); inspector.Children.Add(snap);
        snap.Checked += (_, _) => units.Text = "MILIMETRE   /   1 mm yakalama";
        snap.Unchecked += (_, _) => units.Text = "MILIMETRE   /   Serbest koordinat";
        inspector.Children.Add(new Border { Height = 1, Background = StudioTheme.Border, Margin = new Thickness(3, 0, 3, 12) });
        pieceCount.Foreground = Muted; pieceCount.FontSize = 10; pieceCount.Margin = new Thickness(4, 8, 0, 8); inspector.Children.Add(pieceCount);
        inspector.Children.Add(Label("Parça adı veya kimliği ara",Muted,10));
        var pieceSearchRow=new DockPanel();
        var clearPieceSearch=new Button { Content="Temizle", Padding=new Thickness(8,4,8,4), Margin=new Thickness(3,2,3,3) };
        AutomationProperties.SetName(clearPieceSearch,"Parça aramasını temizle");
        clearPieceSearch.Click+=(_,_)=>pieceSearch.Clear();
        DockPanel.SetDock(clearPieceSearch,Dock.Right);pieceSearchRow.Children.Add(clearPieceSearch);
        AutomationProperties.SetName(pieceSearch,"Parça adı veya kimliği ara");
        AutomationProperties.SetHelpText(pieceSearch,"Adın veya parça kimliğinin bir bölümünü girin. Arama, seçili parçayı değiştirmez.");
        pieceSearchRow.Children.Add(pieceSearch);inspector.Children.Add(pieceSearchRow);
        AutomationProperties.SetName(pieceSearchStatus,"Parça arama sonucu");
        AutomationProperties.SetLiveSetting(pieceSearchStatus,AutomationLiveSetting.Polite);
        inspector.Children.Add(pieceSearchStatus);
        pieces.MinHeight = 120; pieces.MaxHeight = 260; pieces.BorderThickness = new Thickness(0);
        VirtualizingPanel.SetIsVirtualizing(pieces,true); VirtualizingPanel.SetVirtualizationMode(pieces,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(pieces,true);
        AutomationProperties.SetName(pieces,"Proje parçaları");
        AutomationProperties.SetHelpText(pieces,"Aynı adlı parçaları satırdaki tam kimlikten ayırt edin. Seçilen parçanın adı ve kimliği aşağıda kopyalanabilir.");
        var pieceRows=new FrameworkElementFactory(typeof(StackPanel));
        var pieceName=new FrameworkElementFactory(typeof(TextBlock));
        pieceName.SetBinding(TextBlock.TextProperty,new Binding(nameof(PatternPiece.Name)) { Converter=new SafePieceNameConverter() });
        pieceName.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        pieceRows.AppendChild(pieceName);
        var pieceId=new FrameworkElementFactory(typeof(TextBlock));
        pieceId.SetBinding(TextBlock.TextProperty,new Binding(nameof(PatternPiece.Id)) { StringFormat="Kimlik: {0:D}" });
        pieceId.SetValue(TextBlock.FontSizeProperty,10d);
        pieceId.SetValue(TextBlock.ForegroundProperty,Muted);
        pieceRows.AppendChild(pieceId);
        pieces.ItemTemplate=new DataTemplate { VisualTree=pieceRows };
        pieces.SelectionChanged += (_, _) => { if (!syncing) { var id=(pieces.SelectedItem as PatternPiece)?.Id; if(id!=SelectedId && (BlockForActiveDrag() || BlockForLargeVertexValidation())) { syncing=true;pieces.SelectedItem=Selected;syncing=false;return; } SelectedId=id; Refresh(); } };
        inspector.Children.Add(pieces);
        AddButton(inspector,"Birden fazla parçayı taşı…",OpenGroupMove,"Parça listesinden birden fazla parça seçip aynı X/Y kadar taşıyın; işlem tek adımda geri alınır.");
        pieceSearch.TextChanged+=(_,_)=>RefreshPieceSearch();
        AutomationProperties.SetName(selectedPieceIdentity,"Seçili parçanın tam adı ve kimliği");
        AutomationProperties.SetHelpText(selectedPieceIdentity,"Adı ve kimliği seçip kopyalayabilirsiniz. Görünmeyen karakterler Unicode koduyla gösterilir.");
        inspector.Children.Add(selectedPieceIdentity);
        var inspectorShell = new Border { Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(10, 0, 0, 0), Child = new ScrollViewer { Content = inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Grid.SetColumn(inspectorShell, 2); workspace.Children.Add(inspectorShell);

        var canvasArea = new DockPanel();
        var canvasHeader = new DockPanel { LastChildFill = true, Margin = new Thickness(10, 5, 10, 5) };
        var viewCommands = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(viewCommands, Dock.Right); canvasHeader.Children.Add(viewCommands);
        undoCommand = AddButton(viewCommands, "↶", Undo, "Geri al · Ctrl+Z");
        redoCommand = AddButton(viewCommands, "↷", Redo, "Yinele · Ctrl+Y");
        fitCommand = AddButton(viewCommands, "Tümünü göster", () => canvas?.Fit(), "Geometriyi çalışma alanına sığdır · F");
        System.Windows.Automation.AutomationProperties.SetName(undoCommand, "Geri al");
        System.Windows.Automation.AutomationProperties.SetName(redoCommand, "Yinele");
        canvasTool.Foreground = Muted; canvasTool.FontSize = 11; canvasTool.VerticalAlignment = VerticalAlignment.Center; canvasHeader.Children.Add(canvasTool);
        DockPanel.SetDock(canvasHeader, Dock.Top); canvasArea.Children.Add(canvasHeader);
        AutomationProperties.SetName(historyCapacityNotice,"İşlem geçmişi kapasite uyarısı");
        AutomationProperties.SetHelpText(historyCapacityNotice,"Geri alma ve yineleme geçmişi boyut ve adım sayısı ile sınırlıdır. Kalan adımları araç çubuğundan inceleyin.");
        AutomationProperties.SetLiveSetting(historyCapacityNotice,AutomationLiveSetting.Polite);
        DockPanel.SetDock(historyCapacityNotice,Dock.Top); canvasArea.Children.Add(historyCapacityNotice);
        canvas = new CadCanvas(this); canvasArea.Children.Add(canvas);
        var canvasShell = new Border { Background = StudioTheme.Surface, BorderBrush = StudioTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = canvasArea };
        Grid.SetColumn(canvasShell, 1); workspace.Children.Add(canvasShell);

        KeyDown += OnKeyDown;
        try { recoveryStore=new RecoveryStore(AppPaths.RecoveryDirectory); recoveryWriter=new RecoveryWriteCoordinator(recoveryStore); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ShowRecoveryWarning("Otomatik kurtarma başlatılamadı. Çalışmanızı Kaydet ile koruyun.",ex.Message); }
        if(recoveryStore is null)recoveryRescan.IsEnabled=false;
        Closing += async (_, e) =>
        {
            CancelProjectOpen();
            if(recoveryCloseReady)
            {
                windowClosed=true; canvas.CancelLargeCompletion();canvas.CancelLargeVertexValidation(); areaGeneration++; pendingArea=null; autosaveTimer.Stop();
                return;
            }
            e.Cancel=true;
            if(recoveryClosing || !CanDiscard())return;
            recoveryClosing=true; autosaveTimer.Stop(); IsEnabled=false;
            var disposingRecovery=false;
            try
            {
                // Always leave the current Closing event before requesting Close again.
                await Task.Yield();
                await ClearRecoveryAsync();
                if(recoveryWriter is not null) { disposingRecovery=true; await recoveryWriter.DisposeAsync(); }
                recoveryWriter=null; recoveryStore=null;
                recoveryCloseReady=true; Close();
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                IsEnabled=true; recoveryClosing=false; autosaveTimer.Start();
                if(disposingRecovery)
                {
                    // A failed DisposeAsync cannot be retried on the same coordinator.
                    // Its lease state is uncertain; use a new session instead of reusing it.
                    recoveryWriter=null; recoveryStore=null;
                    try { recoveryStore=new RecoveryStore(AppPaths.RecoveryDirectory); recoveryWriter=new RecoveryWriteCoordinator(recoveryStore); }
                    catch(Exception restart) when(restart is IOException or UnauthorizedAccessException or InvalidOperationException)
                    { recoveryRescan.IsEnabled=false; ShowRecoveryWarning("Kurtarma oturumu kapatılamadı ve yeniden başlatılamadı; pencere açık. Çalışmanızı Kaydet ile koruyun.",ex.Message+"\n"+restart.Message); return; }
                    ShowRecoveryWarning("Kurtarma oturumu kapatılırken hata oluştu; yeni oturum açıldı ve pencere açık tutuldu.",ex.Message);
                }
                else ShowRecoveryWarning("Kurtarma kaydı temizlenemedi; pencere açık tutuldu. Çalışmanızı Kaydet ile koruyun.",ex.Message);
            }
        };
        Closed += (_, _) => { recoveryScanCancellation.Cancel(); if(!recoveryScanRunning) { recoveryCursor?.Dispose(); recoveryCursor=null; } };
        autosaveTimer.Tick += async (_, _) => await SaveRecoveryAsync();
        autosaveTimer.Start();
        Loaded += async (_, _) => { Refresh(); canvas.Fit(); canvas.Focus(); await BeginRecoveryScan(); };
        SetTool("Seç"); Refresh();
    }
    private static TextBlock Label(string text, Brush? color = null, int size = 12) => new() { Text = text, Foreground = color ?? Ink, FontSize = size, Margin = new Thickness(4, 8, 0, 7) };
    internal void FocusPieceSearch()
    {
        pieceSearch.BringIntoView();
        pieceSearch.Focus();
    }
    private bool MatchesPieceSearch(object item)
    {
        if(item is not PatternPiece piece)return false;
        var query=pieceSearchQuery;
        var comparer=CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
        if(query.Length==0 || comparer.IndexOf(piece.Name,query,CompareOptions.IgnoreCase)>=0 ||
            piece.Id.ToString("D").Contains(query,StringComparison.OrdinalIgnoreCase))return true;
        // The list shows escaped controls rather than their raw characters.
        // Only create the displayed string when it differs from the real name.
        return PieceNameNeedsEscaping(piece.Name) &&
            comparer.IndexOf(SafePieceName(piece.Name),query,CompareOptions.IgnoreCase)>=0;
    }
    private static bool PieceNameNeedsEscaping(string value)
    {
        if(value.Length==0)return true;
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            if(char.IsHighSurrogate(c) && i+1<value.Length && char.IsLowSurrogate(value[i+1]))
            {
                var category=CharUnicodeInfo.GetUnicodeCategory(value,i);i++;
                if(category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)return true;
                continue;
            }
            if(char.IsSurrogate(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                return true;
        }
        return false;
    }
    private void RefreshPieceSearch()
    {
        pieceSearchQuery=pieceSearch.Text.Trim();
        if(pieces.ItemsSource is null)return;
        var wasSyncing=syncing;syncing=true;
        try
        {
            CollectionViewSource.GetDefaultView(pieces.ItemsSource).Refresh();
            var selected=Selected;
            pieces.SelectedItem=selected is not null && MatchesPieceSearch(selected) ? selected : null;
            UpdatePieceSearchStatus();
        }
        finally { syncing=wasSyncing; }
    }
    private void UpdatePieceSearchStatus()
    {
        var count=pieces.Items.Count;var total=Document.Pieces.Count;
        var selected=Selected;var selectionHidden=selected is not null && !MatchesPieceSearch(selected);
        var message=(count==0 && pieceSearchQuery.Length>0 ? "Eşleşen parça yok." : $"{count} / {total} parça gösteriliyor.")+
            (selectionHidden ? " Seçili parça arama dışında; çalışma alanındaki seçim korunuyor." : "");
        if(pieceSearchStatus.Text==message)return;
        pieceSearchStatus.Text=message;
        AutomationProperties.SetName(pieceSearchStatus,"Parça arama sonucu: "+message);
        if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(pieceSearchStatus) is { } peer)
            peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private static string SafePieceName(string value)
    {
        var result=new StringBuilder();
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            if(char.IsHighSurrogate(c) && i+1<value.Length && char.IsLowSurrogate(value[i+1]))
            {
                var pairCategory=CharUnicodeInfo.GetUnicodeCategory(value,i);var low=value[++i];
                if(pairCategory is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                    result.Append($"\\u{(int)c:X4}\\u{(int)low:X4}");
                else { result.Append(c);result.Append(low); }
                continue;
            }
            var category=char.GetUnicodeCategory(c);
            if(char.IsSurrogate(c) || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) result.Append($"\\u{(int)c:X4}");
            else result.Append(c);
        }
        return result.Length==0?"(boş)":result.ToString();
    }
    private sealed class SafePieceNameConverter : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture) => value is string name ? SafePieceName(name) : "";
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture) => throw new NotSupportedException();
    }
    private sealed class DxfPreviewNameConverter : IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
        {
            if(value is not PatternPiece piece)return "";
            var name=piece.Name;var length=Math.Min(name.Length,80);
            if(length<name.Length && length>0 && char.IsHighSurrogate(name[length-1]))length--;
            var shortName=SafePieceName(name[..length])+(length<name.Length?"…":"");
            return $"{piece.Id:D}\n{shortName}";
        }
        public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
    }
    private static void StyleInput(TextBox box) { box.Padding = new Thickness(8); box.Margin = new Thickness(3); box.MinWidth = 0; }
    private static StackPanel CommandGroup(System.Windows.Controls.Panel parent, string title)
    {
        var group = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        group.Children.Add(new TextBlock { Text = title, FontSize = 9, Foreground = Muted, Margin = new Thickness(5, 0, 0, 5) });
        var commands = new StackPanel { Orientation = Orientation.Horizontal }; group.Children.Add(commands); parent.Children.Add(group); return commands;
    }
    private static Button AddButton(System.Windows.Controls.Panel parent, string text, Action action, string? tooltip = null, bool projectSaveAction = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(3), Cursor = Cursors.Hand, ToolTip = tooltip };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (ProjectConflictException) when(projectSaveAction) { ShowProjectSaveConflict(Window.GetWindow(button)); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "İşlem tamamlanamadı", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        parent.Children.Add(button); return button;
    }
    internal static Action<Window?,string>? ProjectSaveConflictForVerification;
    private static void ShowProjectSaveConflict(Window? owner)
    {
        const string baseMessage="Proje dosyası başka bir uygulamada değiştirilmiş veya silinmiş. Bu kayıt yapılmadı; açık çalışmanız korunuyor. ";
        var message=owner is not null && owner is not MainWindow
            ? baseMessage+"Bu uyarıyı kapatın ve önceki pencerede Vazgeç'i seçin. Ardından ana pencerede Farklı kaydet ile yeni bir dosya adı seçin. Diğer dosyayı açmadan önce açık çalışmanızı kaydedin."
            : baseMessage+"Çalışmanızı korumak için Farklı kaydet ile yeni bir dosya adı seçin. Diğer dosyayı açmadan önce açık çalışmanızı kaydedin.";
        if(ProjectSaveConflictForVerification is { } verify) { verify(owner,message); return; }
        if(owner is null) MessageBox.Show(message,"Proje kaydedilemedi",MessageBoxButton.OK,MessageBoxImage.Warning);
        else MessageBox.Show(owner,message,"Proje kaydedilemedi",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    private void NewDocument()
    {
        if (!CanDiscard()) return;
        var applied=false;
        try
        {
            LoadDocumentCore(new ProjectDocument(),false,()=>applied=true);
            SetStatus("Yeni proje · Çizim aracını seçerek başlayın");
        }
        catch(Exception ex) when(applied && ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            throw new InvalidOperationException("Yeni proje açıldı, ancak görünüm yenilenemedi. Önceki çalışma geri getirilmedi; ana çalışma alanını kontrol edin.",ex);
        }
    }
    public void SetStatus(string value) { status.Text = value; status.ToolTip = value; statusViewer.ScrollToTop(); }
    internal void ShowCanvasError(string message)
    {
        var changed=canvasError.Text!=message || canvasErrorPanel.Visibility!=Visibility.Visible;
        canvasError.Text=message;canvasError.ToolTip=message;canvasErrorPanel.Visibility=Visibility.Visible;canvasErrorViewer.ScrollToTop();
        System.Windows.Automation.AutomationProperties.SetName(canvasError,"Çizim işlemi uyarısı: "+message);
        if(changed && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(canvasError) is { } peer)
            peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        SetStatus(message);
    }
    internal void ClearCanvasError()
    {
        canvasError.Text="";canvasError.ToolTip=null;canvasErrorPanel.Visibility=Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(canvasError,"Çizim işlemi uyarısı");
    }
    private void ShowRecoveryWarning(string message,string detail,RecoveryWarningState.Kind kind=RecoveryWarningState.Kind.General)
    {
        recoveryWarnings.Set(kind,message,detail);
        recoverySaveWarning=recoveryWarnings.Has(RecoveryWarningState.Kind.Save);
        recoveryClearWarning=recoveryWarnings.Has(RecoveryWarningState.Kind.Clear);
        RefreshRecoveryWarnings();SetStatus(message);
    }
    private void ClearRecoveryWarning(RecoveryWarningState.Kind kind)
    {
        recoveryWarnings.Clear(kind);
        recoverySaveWarning=recoveryWarnings.Has(RecoveryWarningState.Kind.Save);
        recoveryClearWarning=recoveryWarnings.Has(RecoveryWarningState.Kind.Clear);
        RefreshRecoveryWarnings();
    }
    private void ClearGeneralRecoveryWarning(string message)
    {
        if(recoveryWarnings.ClearGeneral(message))RefreshRecoveryWarnings();
    }
    private void RefreshRecoveryWarnings()
    {
        recoveryNotice.Text=recoveryWarnings.Message;recoveryNotice.ToolTip=recoveryWarnings.Detail;
        recoveryNotice.Visibility=recoveryWarnings.HasAny?Visibility.Visible:Visibility.Collapsed;
        recoveryDetails.Visibility=recoveryNotice.Visibility;
    }
    public void FitView() => canvas.Fit();
    public void Select(Guid? id) { if(id!=SelectedId && (BlockForActiveDrag() || BlockForLargeVertexValidation()))return;SelectedId=id; Refresh(); }
    private void OpenCoordinateDrawing()
    {
        if(BlockForIncompleteRecovery() || BlockForActiveDrag() || BlockForLargeVertexValidation())return;
        if(ActiveTool is not ("Çizgi" or "Dikdörtgen" or "Bézier" or "Yay" or "Çoklu çizgi"))
        { SetStatus("Koordinatla çizim için önce bir çizim aracı seçin."); return; }
        var window = new CoordinateDrawingWindow(this,
            () => canvas.ToolGuidance(ActiveTool),
            point => recoveryRestoreIncomplete ? "Kurtarma işlemi tamamlanmadı; önce kurtarma durumunu çözün." : canvas.AddDrawingPointAt(point),
            CoordinateDrawingFinishProblem, RemoveCoordinateDrawingPoint, CoordinateDrawingSummary);
        if(window.ShowDialog()==true)
        {
            if(BlockForIncompleteRecovery())return;
            if(ActiveTool != "Çoklu çizgi")
            { SetStatus("Konturu bitirmek için Çoklu çizgi aracını seçin."); return; }
            canvas.FinishPolyline();
        }
    }
    internal string? CoordinateDrawingFinishProblem() =>
        recoveryRestoreIncomplete ? "Kurtarma işlemi tamamlanmadı; önce kurtarma durumunu çözün."
        : ActiveTool != "Çoklu çizgi" ? "Bitirmek için önce Çoklu çizgi aracını seçin."
        : canvas.PendingDrawingPointCount < 3 ? "Konturu bitirmek için en az üç nokta ekleyin."
        : canvas.LargeCompletionRunning || canvas.LargeVertexValidationRunning ? "Doğrulama sürüyor; bitmesini bekleyin."
        : null;
    internal string? RemoveCoordinateDrawingPoint()
    {
        if(recoveryRestoreIncomplete)return "Kurtarma işlemi tamamlanmadı; önce kurtarma durumunu çözün.";
        if(canvas.LargeCompletionRunning || canvas.LargeVertexValidationRunning)return "Doğrulama sürüyor; bitmesini bekleyin.";
        if(canvas.HasActiveDragPreview)return "Önce sürüklemeyi tamamlayın veya iptal edin.";
        var count=canvas.PendingDrawingPointCount;
        if(count==0)return "Kaldırılacak çizim noktası yok.";
        canvas.RemoveLastPendingPoint();
        return canvas.PendingDrawingPointCount==count-1 ? null : "Nokta kaldırılamadı; çizim durumunu kontrol edin.";
    }
    internal string CoordinateDrawingSummary()
    {
        if(canvas.LastPendingDrawingPoint is not { } point)return "Bekleyen çizim noktası yok.";
        return $"Bekleyen nokta sayısı: {canvas.PendingDrawingPointCount}. Son nokta: X {MeasurementInput.Format(point.X)} mm, Y {MeasurementInput.Format(point.Y)} mm.";
    }
    public void SetTool(string tool)
    {
        if(BlockForIncompleteRecovery())return;
        if(BlockForActiveDrag())return;
        if(tool!=ActiveTool && BlockForLargeVertexValidation())return;
        if(canvas is not null && canvas.HasPendingDrawing)
        {
            if(tool==ActiveTool)return;
            if(!ConfirmStableDiscardPendingDrawing("Araç değiştirilirse"))return;
        }
        ActiveTool=tool; canvas?.CancelDrawing();
        RefreshToolPresentation(tool);
    }
    private void RefreshToolPresentation(string tool)
    {
        foreach(var button in toolButtons)
        {
            button.Value.Background = button.Key == tool ? StudioTheme.Accent : StudioTheme.Surface;
            button.Value.Foreground = button.Key == tool ? StudioTheme.Background : StudioTheme.Text;
            button.Value.BorderBrush = button.Key == tool ? StudioTheme.Accent : StudioTheme.Border;
        }
        canvasTool.Text = "KALIP ALANI   /   " + tool.ToUpper(CultureInfo.GetCultureInfo("tr-TR"));
        SetStatus(canvas is null ? $"{tool} · Koordinatlar mm" : canvas.ToolGuidance(tool));
    }
    internal Action? AfterCommitBeforeRefreshForVerification { get; set; }
    public void Commit(string description, Action action) => CommitObserved(description,action,null);
    internal bool CanRetainCurrentUndoSnapshot => session.CanRetainCurrentUndoSnapshot;
    // Called only after a changed document is committed, before UI refresh can fail.
    internal void CommitObserved(string description, Action action, Action? onCommitted)
    {
        ArgumentNullException.ThrowIfNull(action);
        if(recoveryRestoreIncomplete)
            throw new InvalidOperationException("Kurtarılan çizim tam açılamadı; özgün kurtarma kaydını yeniden açmadan düzenleme yapılamaz.");
        bool changed;
        try { changed = session.Execute(_ => action(), ProjectStorage.Validate); }
        catch { dirty = session.IsDirty; Refresh(); throw; }
        if(changed) { dirty=true; onCommitted?.Invoke(); AfterCommitBeforeRefreshForVerification?.Invoke(); }
        dirty = session.IsDirty; Refresh(); SetStatus(changed ? description : "Değişiklik yok · Çalışmanız ve işlem geçmişi korundu");
    }
    public void AddPiece(List<Point2> points,bool closed) => AddPiece(points,closed,null);
    internal void AddPiece(List<Point2> points,bool closed,Action? onCommitted)
    { if (points.Count < (closed ? 3 : 2)) return; CommitObserved("Kalıp geometrisi oluşturuldu",() => { var p=new PatternPiece { Name=$"Parça {Document.Pieces.Count+1}", Points=[.. points], Closed=closed }; Document.Pieces.Add(p); SelectedId=p.Id; },onCommitted); }
    internal Action? AfterDxfLoadBeforeRefreshForVerification { get; set; }
    internal Action? AfterProjectLoadBeforeRefreshForVerification { get; set; }
    public void LoadDocument(ProjectDocument document) => LoadDocumentCore(document,false,null);
    private void LoadDocumentCore(ProjectDocument document,bool markUnsaved,Action? onLoaded,bool openingProject=false)
    {
        EnsureNoExport();
        // Load validates and clones before replacing the live session. Once it
        // succeeds, publish the outcome before any later UI operation can fail.
        session.Load(document,Validate);
        // The session's counter spans loads; only losses since this document was
        // installed belong in its on-screen warning.
        historyCapacityBaseline=session.CapacityDiscardedSnapshots;
        historyCapacityAnnounced=historyCapacityBaseline;
        mainMutationReviewRequired=false;
        projectEpoch=Guid.NewGuid();
        if(markUnsaved)session.MarkUnsaved();
        dirty=markUnsaved;
        propertyDrafts.Clear();filePath=null;fileVersion=null;savedFileHasGradingRules=false;savedFileHasMetadata=false;savedFileHasNestingSnapshot=false;savedFileHasGrainline=false;savedFileHasImportProvenance=false;
        SelectedId=null;recoveredSource=null;recoveredDocumentSaved=false;recoveryRestoreIncomplete=false;
        onLoaded?.Invoke();
        ClearRecovery();canvas.CancelDrawing();canvas.IsEnabled=true;
        if(markUnsaved)AfterDxfLoadBeforeRefreshForVerification?.Invoke();
        if(openingProject)AfterProjectLoadBeforeRefreshForVerification?.Invoke();
        Refresh();canvas.Fit();SetStatus("Çalışma alanı hazır · Ölçüler milimetre cinsindedir");
    }
    public static void Validate(ProjectDocument d) => ProjectStorage.Validate(d);
    internal Action? DuringRefreshForVerification { get; set; }
    private void Refresh()
    {
        var wasSyncing=syncing;syncing=true;
        try
        {
        DuringRefreshForVerification?.Invoke();
        var source=Document.Pieces;
        // Transactions replace the owned list, but List<T> does not notify WPF if
        // changed in place. Keep the view during selection-only refreshes while
        // detecting structural or display changes to the same list.
        bool sourceChanged=!ReferenceEquals(pieces.ItemsSource,source);
        bool contentsChanged=displayedPieces.Count!=source.Count;
        if(!contentsChanged)
            for(int i=0;i<source.Count;i++)
                if(!ReferenceEquals(displayedPieces[i].Piece,source[i]) || displayedPieces[i].Id!=source[i].Id || displayedPieces[i].Name!=source[i].Name)
                { contentsChanged=true; break; }
        if(sourceChanged || contentsChanged)
        {
            displayedPieces.Clear();
            foreach(var piece in source) displayedPieces.Add((piece,piece.Id,piece.Name));
            if(sourceChanged) { pieces.ItemsSource=source;pieces.Items.Filter=MatchesPieceSearch; }
            else CollectionViewSource.GetDefaultView(source).Refresh();
        }
        var listSelection=Selected;
        var visibleSelection=listSelection is not null && MatchesPieceSearch(listSelection) ? listSelection : null;
        if(!ReferenceEquals(pieces.SelectedItem,visibleSelection)) pieces.SelectedItem=visibleSelection;
        UpdatePieceSearchStatus();
        var safeProjectName=SafePieceName(Document.Name);
        projectTitle.Text = safeProjectName;
        projectTitle.ToolTip = safeProjectName;
        System.Windows.Automation.AutomationProperties.SetName(projectTitle,"Proje adı: "+safeProjectName);
        UpdateNestingHistoryNotice();
        sourceHistoryNotice.Text=Document.ImportProvenance is null ? SourceHistoryNotice :
            "DXF kaynak ve aktarım notları projede saklanıyor. Geometri değişmiş olabilir; ölçüleri ve yerleşimi kaynakla karşılaştırın.";
        AutomationProperties.SetName(sourceHistoryNotice,sourceHistoryNotice.Text);
        UpdateSaveState();
        pieceCount.Text = $"PROJE PARÇALARI  /  {Document.Pieces.Count}";
        var selected=Selected; var b=selected is { Points.Count: >0 } p ? Geometry.Bounds(p.Points) : default;
        selectedPieceIdentity.Text=selected is null ? "Parça seçilmedi." : $"{SafePieceName(selected.Name)}\nKimlik: {selected.Id:D}";
        var line=selected?.Grainline;
        var grainMessage=selected is null ? "İplik yönü için bir parça seçin."
            : line is null ? "Bu parçada iplik yönü çizgisi yok."
            : $"İplik yönü: X {MeasurementInput.Format(line.Start.X)}, Y {MeasurementInput.Format(line.Start.Y)} → X {MeasurementInput.Format(line.End.X)}, Y {MeasurementInput.Format(line.End.Y)} mm. " +
              (line.NeedsReview ? "Konturla ilişkisi yeniden incelenmeli. " : "İnceleme işareti yok. ")+
              "Üretim veya makine uygunluğu doğrulanmadı; işaretli parçaların çıktı desteği yok.";
        if(grainlineStatus.Text!=grainMessage)
        {
            grainlineStatus.Text=grainMessage;AutomationProperties.SetName(grainlineStatus,grainMessage);
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(grainlineStatus) is { } grainPeer)
                grainPeer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        var pointCount=selected?.Points.Count ?? 0;
        if(pointCount>2000)
        {
            pieceLimits.Text=pointCount>Geometry.MaxValidatedContourVertices
                ? $"{pointCount} nokta: Pastal parça başına 2000 noktayla sınırlıdır. Dikiş payı ve beden serileme 4096 noktanın üzerini desteklemez; nokta düzenleme arka planda doğrulanır. HPGL iş genelindeki 50 milyon kenar çifti sınırına tabidir; dosya çıktısı fiziksel makine uyumluluğunu doğrulamaz."
                : $"{pointCount} nokta: Pastal parça başına 2000 noktayla sınırlıdır. HPGL iş genelindeki 50 milyon kenar çifti sınırına tabidir; dosya çıktısı fiziksel makine uyumluluğunu doğrulamaz.";
            pieceLimits.Visibility=Visibility.Visible;
        }
        else { pieceLimits.Text=""; pieceLimits.Visibility=Visibility.Collapsed; }
        var selectedRule=selected is null ? null : Document.GradingRules.FirstOrDefault(rule=>rule.SourcePieceId==selected.Id);
        var staleGrading=selected is not null && selectedRule is not null &&
            (selectedRule.SourceClosed!=selected.Closed ||
             (!selectedRule.SourcePoints.SequenceEqual(selected.Points) && !TextileToolsWindow.IsTranslation(selectedRule.SourcePoints,selected.Points)));
        var gradingNoticeWasVisible=gradingGeometryNotice.Visibility==Visibility.Visible;
        gradingGeometryNotice.Text=staleGrading
            ? "Kayıtlı serileme kuralı eski geometriye ait. Tekstil araçlarında artışları gözden geçirin."
            : "";
        gradingGeometryNotice.Visibility=staleGrading ? Visibility.Visible : Visibility.Collapsed;
        if(staleGrading && !gradingNoticeWasVisible && System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(gradingGeometryNotice) is { } gradingPeer)
            gradingPeer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        var draft=selected is not null && propertyDrafts.TryGetValue(selected.Id,out var stored) ? stored : null;
        name.Text=draft?.Name ?? selected?.Name ?? "";
        x.Text=draft?.X ?? (selected is null ? "" : MeasurementInput.Format(b.MinX));
        y.Text=draft?.Y ?? (selected is null ? "" : MeasurementInput.Format(b.MinY));
        }
        finally { syncing=wasSyncing; }
        UpdatePropertyDraftConflict();
        const string numberHelp="Virgül veya nokta ondalıktır; binlik ayırıcı kullanmayın.";
        x.ToolTip="X: "+x.Text+" mm\n"+numberHelp;y.ToolTip="Y: "+y.Text+" mm\n"+numberHelp;
        UpdateMetrics();
        var hasSelection = Selected is { Points.Count: > 0 };
        name.IsEnabled = x.IsEnabled = y.IsEnabled = hasSelection;
        foreach (var command in selectionCommands) command.IsEnabled = hasSelection;
        if (undoCommand is not null) undoCommand.IsEnabled = session.UndoCount > 0;
        if (redoCommand is not null) redoCommand.IsEnabled = session.RedoCount > 0;
        UpdateHistoryCapacityNotice();
        if (exportCommand is not null) exportCommand.IsEnabled = Document.Pieces.Count > 0 && !ExportRunning && !DxfImportRunning;
        if (grainReviewExportCommand is not null) grainReviewExportCommand.IsEnabled = Document.Pieces.Count > 0 && !ExportRunning && !DxfImportRunning;
        if (fitCommand is not null) fitCommand.IsEnabled = Document.Pieces.Any(piece => piece.Points.Count > 0);
        canvas?.InvalidateVisual();
    }
    private void UpdateHistoryCapacityNotice()
    {
        var current=session.CapacityDiscardedSnapshots;
        if(current<=historyCapacityBaseline)
        {
            historyCapacityNotice.Visibility=Visibility.Collapsed;
            historyCapacityNotice.Text="";
            return;
        }
        const string message="İşlem geçmişi kapasitesi aşıldı; bazı geri alma veya yineleme adımları saklanamadı.";
        historyCapacityNotice.Text=message;
        historyCapacityNotice.Visibility=Visibility.Visible;
        AutomationProperties.SetName(historyCapacityNotice,message);
        if(current>historyCapacityAnnounced)
        {
            historyCapacityAnnounced=current;
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(historyCapacityNotice) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }
    private void UpdateNestingHistoryNotice()
    {
        var current=Document;
        if(ReferenceEquals(nestingHistoryDocument,current))return;
        nestingHistoryDocument=current;
        var generation=++nestingHistoryGeneration;
        if(current.NestingSnapshot is null)
        { nestingHistoryNotice.Text="";nestingHistoryNotice.Visibility=Visibility.Collapsed;nestingHistoryDetails.Visibility=Visibility.Collapsed;return; }
        nestingHistoryNotice.Visibility=Visibility.Visible;
        nestingHistoryDetails.Visibility=Visibility.Visible;
        nestingHistoryNotice.Foreground=StudioTheme.Warning;
        nestingHistoryNotice.Text="Pastal geçmişi karşılaştırılıyor; üretim onayı değildir.";
        var detached=current.Clone();
        _ = CompareNestingHistoryAsync(detached,current,generation);
    }
    private async Task CompareNestingHistoryAsync(ProjectDocument detached,ProjectDocument source,long generation)
    {
        bool? matches;
        try { matches=await Task.Run(()=>detached.NestingSnapshot!.IsCurrentFor(detached)).ConfigureAwait(false); }
        catch(Exception) { matches=null; }
        if(Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)return;
        await Dispatcher.InvokeAsync(()=>
        {
            if(windowClosed || generation!=nestingHistoryGeneration || !ReferenceEquals(Document,source))return;
            nestingHistoryNotice.Text=matches switch
            {
                true => "Pastal geçmişi: parçalarda değişiklik algılanmadı; üretim onayı değildir.",
                false => "Pastal geçmişi: parçalar değişmiş; geçmiş ayarları güncel yerleşimi doğrulamaz.",
                null => "Pastal geçmişi karşılaştırılamadı; geçmiş ayarlarını doğrulanmış saymayın."
            };
        });
    }
    /// <summary>Read-only historical information. It does not certify geometry or machine suitability.</summary>
    private sealed record NestingHistoryRow(NestingPlacementSnapshot Placement, string Label);

    private static string SafeHistoryName(string value)
    {
        var result=new System.Text.StringBuilder();
        for(var i=0;i<value.Length;i++)
        {
            var c=value[i];
            if(char.IsHighSurrogate(c) && i+1<value.Length && char.IsLowSurrogate(value[i+1]))
            { result.Append(c);result.Append(value[++i]);continue; }
            var category=char.GetUnicodeCategory(c);
            if(char.IsSurrogate(c) || category is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator)
                result.Append($"\\u{(int)c:X4}");
            else result.Append(c);
        }
        return result.Length==0 ? "(boş)" : result.ToString();
    }

    internal Window CreateNestingHistoryWindow()
    {
        var history=Document.NestingSnapshot?.Clone() ?? throw new InvalidOperationException("Bu projede pastal geçmişi yok.");
        var window=new Window { Owner=this,Title="Pastal geçmişi",Width=760,Height=620,MinWidth=540,MinHeight=430,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(window);
        var root=new DockPanel { Margin=new Thickness(18) };window.Content=root;
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock { Text="Bu bilgiler hesaplama zamanına ait geçmiş kaydıdır; doğrulanmış üretim veya makine onayı değildir. Kaynak parça kimlikleri tarihsel bilgilerdir; kaynak proje bağlı değildir ve doğrulanmamıştır.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8) });
        var state=new TextBlock { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,0,0,8) };
        state.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source=nestingHistoryNotice });
        header.Children.Add(state);
        var dimensions=new TextBox { IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            Text=$"Kumaş eni: {MeasurementInput.Format(history.FabricWidth)} mm\nKullanılan boy: {MeasurementInput.Format(history.UsedLength)} mm\nParça boşluğu: {MeasurementInput.Format(history.Gap)} mm\nHer kenar payı: {MeasurementInput.Format(history.EdgeMargin)} mm\nAlgoritma: {history.Algorithm}" };
        System.Windows.Automation.AutomationProperties.SetName(dimensions,"Kaydedilmiş geçmiş pastal ölçüleri");header.Children.Add(dimensions);
        var bottom=new StackPanel { Margin=new Thickness(0,8,0,0) };DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        bottom.Children.Add(new TextBlock { Text="Seçili eşleme · kopyalanabilir",Margin=new Thickness(0,0,0,4) });
        var detail=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=58,MaxHeight=80,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(detail,"Seçili geçmiş parça eşlemesi");bottom.Children.Add(detail);
        var close=AddButton(bottom,"Kapat",window.Close);close.IsCancel=true;close.HorizontalAlignment=HorizontalAlignment.Right;
        var currentNames=Document.Pieces.ToDictionary(piece=>piece.Id,piece=>SafeHistoryName(piece.Name));
        var rows=history.Placements.Select(p=>new NestingHistoryRow(p,
            $"{(currentNames.TryGetValue(p.PlacedPieceId,out var name) ? "Mevcut parça: "+name : "Parça projede yok")} · Kopya: {p.CopyNumber} · Kimlik: {p.PlacedPieceId}")).ToArray();
        var mappings=new ListBox { ItemsSource=rows,DisplayMemberPath="Label",HorizontalContentAlignment=HorizontalAlignment.Stretch };
        VirtualizingPanel.SetIsVirtualizing(mappings,true);VirtualizingPanel.SetVirtualizationMode(mappings,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(mappings,true);ScrollViewer.SetVerticalScrollBarVisibility(mappings,ScrollBarVisibility.Auto);
        System.Windows.Automation.AutomationProperties.SetName(mappings,"Geçmişte yerleştirilen parçalar");
        System.Windows.Automation.AutomationProperties.SetHelpText(mappings,"Satırlardaki adlar mevcut projeye aittir; tarihsel kaynak adı olarak doğrulanmamıştır. Seçilen eşlemenin kimlikleri altta gösterilir.");
        mappings.SelectionChanged+=(_,_)=>
        {
            detail.Text=mappings.SelectedItem is NestingHistoryRow { Placement: var p }
                ? $"Kaynak parça: {p.SourcePieceId}\nKopya: {p.CopyNumber}\nYerleştirilmiş parça: {p.PlacedPieceId}"
                : "";
        };
        root.Children.Add(mappings);
        return window;
    }
    private void UpdateSaveState()
    {
        // Once all edits have been undone or the last unapplied draft discarded,
        // an older autosave must not later reappear as current unsaved work.
        // Only clear this session's own successful snapshot; failures remain visible.
        if (!HasUnsavedWork && recoveredDocumentSaved) ArchiveRecoveredSource();
        if (!HasUnsavedWork && (lastRecoveryAt is not null || recoveryMayExist)) ClearRecovery();
        if(recoverySaveWarning)
        {
            var warning=HasUnsavedWork
                ? "Otomatik kurtarma güncellenemedi. Son değişikliklerinizi Kaydet ile koruyun."
                : "Otomatik kurtarma güncellenemedi. Şu anda kaydedilmemiş değişiklik yok; yeni değişiklikler otomatik kurtarılamayabilir.";
            if(recoveryWarnings.UpdateMessage(RecoveryWarningState.Kind.Save,warning))RefreshRecoveryWarnings();
        }
        var safeTitle=SafePieceName(Document.Name);
        if(safeTitle.Length>120)
        {
            var length=120;
            if(char.IsHighSurrogate(safeTitle[length-1]))length--;
            safeTitle=safeTitle[..length]+"…";
        }
        Title = $"{safeTitle}{(HasUnsavedWork ? " *" : "")} — {(AppPaths.IsIsolatedPreview ? "Mono Di Tree CAD · Geliştirme Önizlemesi" : "Mono Di Tree CAD · 0.4 Önizleme")}";
        var recoveryTime=lastRecoveryAt is { } at ? $" · Son kurtarma: {at.ToString("g",CultureInfo.CurrentCulture)}" : "";
        saveState.Text = (canvas?.HasPendingDrawing ?? false) ? "● Tamamlanmamış çizim"+recoveryTime : propertyDrafts.Count > 0 ? $"● Alan taslağı: {propertyDrafts.Count} parça{recoveryTime}" : dirty ? "● Kaydedilmemiş değişiklikler"+recoveryTime : filePath is null ? "Yeni proje · Henüz dosyaya kaydedilmedi" : "✓ Kaydedildi · " + Path.GetFileName(filePath);
        saveState.Foreground = HasUnsavedWork ? StudioTheme.Warning : StudioTheme.Muted;
        saveState.ToolTip = recoveryClearFailed ? "Önceki kurtarma kaydı temizlenemedi ve korunuyor; Kaydet, yeni proje veya kapatma sırasında yeniden denenecek." : HasUnsavedWork ? "Otomatik kurtarma son başarılı kayıt zamanını gösterir; sonraki değişiklikler henüz kurtarma kaydında olmayabilir. Kalıcı kayıt için Kaydet'i kullanın." : filePath;
    }
    internal void PendingDrawingChanged() => UpdateSaveState();
    private void CapturePropertyDraft()
    {
        if(syncing || Selected is not { Points.Count: >0 } piece)return;
        var bounds=Geometry.Bounds(piece.Points);
        if(name.Text==piece.Name && x.Text==MeasurementInput.Format(bounds.MinX) && y.Text==MeasurementInput.Format(bounds.MinY)) propertyDrafts.Remove(piece.Id);
        else
        {
            var previous=propertyDrafts.GetValueOrDefault(piece.Id);
            propertyDrafts[piece.Id]=new PropertyDraft(name.Text,x.Text,y.Text,previous?.BaseName ?? piece.Name,previous?.BaseX ?? bounds.MinX,previous?.BaseY ?? bounds.MinY);
        }
        UpdatePropertyDraftConflict();UpdateSaveState();
    }
    private void UpdatePropertyDraftConflict()
    {
        if(Selected is not { Points.Count: >0 } piece || !propertyDrafts.TryGetValue(piece.Id,out var draft))
        { propertyDraftConflict.Text="";propertyDraftConflict.Visibility=Visibility.Collapsed;return; }
        var bounds=Geometry.Bounds(piece.Points);
        if(piece.Name==draft.BaseName && bounds.MinX==draft.BaseX && bounds.MinY==draft.BaseY)
        { propertyDraftConflict.Text="";propertyDraftConflict.Visibility=Visibility.Collapsed;return; }
        propertyDraftConflict.Text="Bu alan taslağı önceki parça durumuna ait; alanlardaki taslak henüz uygulanmadı.\n"+
            $"Mevcut parça adı: {SafePieceName(piece.Name)}\nMevcut X: {MeasurementInput.Format(bounds.MinX)} mm\nMevcut Y: {MeasurementInput.Format(bounds.MinY)} mm\n"+
            "Taslağı inceleyin; uygulamak için eski taslağı kaldırıp mevcut değerlerle yeniden girin.";
        propertyDraftConflict.Visibility=Visibility.Visible;
    }
    internal Func<string,bool>? ConfirmClearPropertyDraftsForVerification { get; set; }
    private void ClearPropertyDrafts()
    {
        if(propertyDrafts.Count==0)return;
        var epoch=projectEpoch;var document=Document;var snapshot=document.Clone();
        var approvedDrafts=new Dictionary<Guid,PropertyDraft>(propertyDrafts);
        var message=$"{approvedDrafts.Count} parçanın uygulanmamış ad ve konum taslakları silinsin mi?";
        var confirmed=ConfirmClearPropertyDraftsForVerification is { } confirm
            ? confirm(message)
            : MessageBox.Show(this,message,"Alan taslaklarını temizle",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
        if(!confirmed)return;
        if(projectEpoch!=epoch || !ReferenceEquals(Document,document) || !DocumentSession.ContentEquals(snapshot,Document) ||
           propertyDrafts.Count!=approvedDrafts.Count || approvedDrafts.Any(pair=>!propertyDrafts.TryGetValue(pair.Key,out var current) || !ReferenceEquals(pair.Value,current)))
        { SetStatus("Proje veya alan taslakları onay sırasında değişti; hiçbir taslak silinmedi. Güncel taslakları yeniden inceleyin.");return; }
        propertyDrafts.Clear();Refresh();
    }
    private void ReviewPropertyDrafts()
    {
        if(propertyDrafts.Count==0) { MessageBox.Show(this,"İncelenecek alan taslağı yok.","Alan taslakları",MessageBoxButton.OK,MessageBoxImage.Information);return; }
        var prompt=CreatePropertyDraftReviewWindow();
        prompt.ShowDialog();
    }
    internal Window CreatePropertyDraftReviewWindow(Func<string,bool>? confirmRemoval=null)
    {
        var prompt=new Window { Owner=this,Title="Alan taslaklarını incele",Width=650,Height=470,MinWidth=500,MinHeight=340,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(prompt);
        var panel=new DockPanel { Margin=new Thickness(20) };prompt.Content=panel;
        var intro=new TextBlock { Text="Seçili taslağı kaldırabilirsiniz. Parçası silinmiş taslaklar kayıt işlemini engeller; parçayı geri alarak da taslağı koruyabilirsiniz.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) };
        DockPanel.SetDock(intro,Dock.Top);panel.Children.Add(intro);
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) };DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);
        var list=new ListBox { HorizontalContentAlignment=HorizontalAlignment.Stretch };
        System.Windows.Automation.AutomationProperties.SetName(list,"Parça alan taslakları");
        var content=new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        content.Children.Add(list);
        var detailFields=new StackPanel { Margin=new Thickness(0,6,0,0) };
        TextBox DetailField(string label)
        {
            detailFields.Children.Add(new TextBlock { Text=label,Margin=new Thickness(0,4,0,2) });
            var field=new TextBox { IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,
                MinHeight=28,MaxHeight=60,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
            System.Windows.Automation.AutomationProperties.SetName(field,label);
            detailFields.Children.Add(field);return field;
        }
        var draftName=DetailField("Taslak adı");
        var draftX=DetailField("Taslak X konumu");
        var draftY=DetailField("Taslak Y konumu");
        var detailScroll=new ScrollViewer { Content=detailFields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        Grid.SetRow(detailScroll,1);content.Children.Add(detailScroll);panel.Children.Add(content);
        list.SelectionChanged+=(_,_)=>
        {
            var selected=list.SelectedItem is ListBoxItem { Tag:Guid id } && propertyDrafts.TryGetValue(id,out var draft) ? draft : null;
            draftName.Text=selected?.Name ?? "";draftX.Text=selected?.X ?? "";draftY.Text=selected?.Y ?? "";
        };
        void Populate()
        {
            list.Items.Clear();
            var existing=Document.Pieces.Select(piece=>piece.Id).ToHashSet();
            foreach(var (id,draft) in propertyDrafts)
            {
                // Full identity stays available even when several pieces share a name.
                var title=draft.BaseName.Length>120 ? draft.BaseName[..120]+"…" : draft.BaseName;
                var state=existing.Contains(id) ? "Parça projede" : "Parça projede yok · kaydı engeller";
                list.Items.Add(new ListBoxItem { Tag=id,Content=new TextBlock { Text=$"{title}  ·  {state}\nKimlik: {id}",TextWrapping=TextWrapping.Wrap },Padding=new Thickness(8) });
            }
        }
        AddButton(buttons,"Seçili taslağı kaldır",()=>
        {
            if(list.SelectedItem is not ListBoxItem { Tag:Guid id } || !propertyDrafts.TryGetValue(id,out var draft))return;
            var epoch=projectEpoch;var document=Document;var snapshot=document.Clone();
            var label=draft.BaseName.Length>120 ? draft.BaseName[..120]+"…" : draft.BaseName;
            var message=$"“{label}” ({id}) için uygulanmamış ad ve konum taslağı kaldırılsın mı? Diğer taslaklar korunur.";
            var confirmed=confirmRemoval is null ? MessageBox.Show(prompt,message,"Taslağı kaldır",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes : confirmRemoval(message);
            if(!confirmed)return;
            if(projectEpoch!=epoch || !ReferenceEquals(Document,document) || !DocumentSession.ContentEquals(snapshot,Document) ||
               !propertyDrafts.TryGetValue(id,out var current) || !ReferenceEquals(draft,current))
            { SetStatus("Proje veya seçili taslak onay sırasında değişti; taslak kaldırılmadı. Güncel taslakları yeniden inceleyin.");Populate();return; }
            propertyDrafts.Remove(id);Refresh();Populate();
            SetStatus("Seçili alan taslağı kaldırıldı; diğer taslaklar korundu.");
        });
        var close=AddButton(buttons,"Kapat",()=>prompt.Close());close.IsCancel=true;
        Populate();return prompt;
    }
    private void UpdateMetrics()
    {
        var piece=Selected;
        var count=piece?.Points.Count ?? 0;
        var vertices=piece is { Closed:true } && count>1 && piece.Points[0]==piece.Points[^1] ? count-1 : count;
        if(piece is null || !piece.Closed || count<=512 || vertices>Geometry.MaxValidatedContourVertices)
        {
            areaGeneration++; pendingArea=null; areaPieceId=null; areaDocument=null; areaPoints=null; areaResult=null;
            metrics.Text=piece is null ? $"{Document.Pieces.Count} parça · Seçim yok" : FormatPieceMetrics(piece);
            return;
        }
        if(areaPieceId!=piece.Id || !ReferenceEquals(areaDocument,Document) || areaPoints is null || !areaPoints.SequenceEqual(piece.Points))
        {
            areaPieceId=piece.Id; areaDocument=Document; areaPoints=piece.Points.ToArray(); areaResult=null;
            pendingArea=(areaPoints,++areaGeneration);
            if(!areaWorkerRunning) _ = CalculateAreaAsync();
        }
        var prefix=FormatPieceMetrics(piece);
        metrics.Text=prefix[..prefix.LastIndexOf("\nAlan:",StringComparison.Ordinal)]+"\n"+(areaResult ?? "Alan: hesaplanıyor…");
    }
    private async Task CalculateAreaAsync()
    {
        areaWorkerRunning=true;
        try
        {
            while(!windowClosed && pendingArea is { } request)
            {
                pendingArea=null;
                var (points,generation)=request;
                string result;
                try
                {
                    result=await Task.Run(() =>
                    {
                        try { Geometry.ValidateContour(points); return $"Alan: {DisplayMeasure(Math.Abs(Geometry.SignedArea(points))/100)} cm²"; }
                        catch(ArgumentException) { return "Alan: geçersiz konturda hesaplanamıyor"; }
                    });
                }
                catch(Exception) { result="Alan: hesaplanamadı"; }
                if(windowClosed || generation!=areaGeneration || areaPieceId!=SelectedId || !ReferenceEquals(areaDocument,Document) || Selected is not { Closed:true } current || !points.SequenceEqual(current.Points)) continue;
                areaResult=result;
                UpdateMetrics();
            }
        }
        finally { areaWorkerRunning=false; }
    }
    // Quadratic contour validation is deliberately bounded on the UI thread.
    internal static string FormatPieceMetrics(PatternPiece piece)
    {
        var points=piece.Points;
        if(points.Count==0) return "Nokta yok · Ölçü hesaplanamıyor";
        var bounds=Geometry.Bounds(points);
        double length=0;
        for(var i=1;i<points.Count;i++) length+=Geometry.Distance(points[i-1],points[i]);
        if(piece.Closed && points.Count>1 && points[0]!=points[^1]) length+=Geometry.Distance(points[^1],points[0]);
        var text=$"{DisplayMeasure(bounds.Width)} × {DisplayMeasure(bounds.Height)} mm\n{points.Count} nokta · {(piece.Closed ? "Kapalı kontur" : "Açık çizgi")}\n{(piece.Closed ? "Çevre" : "Çizgi uzunluğu")}: {DisplayMeasure(length)} mm";
        if(!piece.Closed) return text+"\nAlan: açık çizgide hesaplanmaz";
        if(points.Count>512) return text+"\nAlan: doğrulama sınırını aşıyor";
        try { Geometry.ValidateContour(points); return text+$"\nAlan: {DisplayMeasure(Math.Abs(Geometry.SignedArea(points))/100)} cm²"; }
        catch(ArgumentException) { return text+"\nAlan: geçersiz konturda hesaplanamıyor"; }
    }
    private static string DisplayMeasure(double value) => value > 0 && value < 0.01
        ? value.ToString("G6", CultureInfo.CurrentCulture)
        : value.ToString("0.##", CultureInfo.CurrentCulture);
    private void ApplyProperties()
    {
        if(SelectedId is { } id)ApplyPropertyDrafts([id]);
    }
    private List<(Guid Id, string Name, double X, double Y)> ValidatePropertyDrafts(ProjectDocument document, IEnumerable<Guid> ids)
    {
        var changes=new List<(Guid Id, string Name, double X, double Y)>();
        foreach(var id in ids.ToArray())
        {
            if(!propertyDrafts.TryGetValue(id,out var draft))continue;
            var piece=document.Pieces.FirstOrDefault(p=>p.Id==id);
            if(piece is not { Points.Count: >0 })throw new InvalidDataException("Alan taslağına ait parça bulunamadı. Silme işlemini geri alın veya ‘Alan taslaklarını incele’ ile yalnızca bu taslağı kaldırın.");
            var bounds=Geometry.Bounds(piece.Points);
            if(piece.Name!=draft.BaseName || bounds.MinX!=draft.BaseX || bounds.MinY!=draft.BaseY)
                throw new InvalidDataException($"“{SafePieceName(piece.Name)} (kimlik: {piece.Id:D})” parçası taslak oluşturulduktan sonra değişti. Yanlış konumlandırmayı önlemek için taslağı temizleyip yeniden girin.");
            if(!MeasurementInput.TryParseMillimeters(draft.X,1e8,out var px) || !MeasurementInput.TryParseMillimeters(draft.Y,1e8,out var py))
                throw new InvalidDataException($"“{SafePieceName(piece.Name)} (kimlik: {piece.Id:D})” için geçerli X ve Y koordinatları girin. Taslak korunuyor.");
            changes.Add((id,draft.Name,px,py));
        }
        return changes;
    }
    private static void ApplyDraftChanges(ProjectDocument document, IReadOnlyList<(Guid Id, string Name, double X, double Y)> changes)
    {
        // Prepare the entire batch before changing any piece. A precision or
        // bounds failure must leave every property draft applicable/reviewable.
        var prepared=new List<(PatternPiece Piece, string Name, List<Point2>? Points, Grainline? Grainline, GradingRuleSet? Rule, GradingRuleSet? MovedRule)>(changes.Count);
        foreach(var change in changes)
        {
            var piece=document.Pieces.First(p=>p.Id==change.Id);var bounds=Geometry.Bounds(piece.Points);
            var dx=change.X-bounds.MinX;var dy=change.Y-bounds.MinY;
            List<Point2>? translated;
            Grainline? translatedGrain;
            try
            {
                translated=dx!=0 || dy!=0 ? Geometry.Translate(piece.Points,dx,dy) : null;
                translatedGrain=translated is null ? piece.Grainline?.Clone()
                    : GrainlineTransform.Translate(piece.Grainline,piece.Points[0],translated[0],dx,dy);
            }
            catch(ArgumentException ex)
            {
                throw new InvalidDataException($"“{SafePieceName(piece.Name)} (kimlik: {piece.Id:D})” konumu güvenle uygulanamadı. Taslak korunuyor. {ex.Message}",ex);
            }
            if(translated is not null)
            {
                var actual=Geometry.Bounds(translated);
                if(!TargetPositionMatches(actual.MinX,change.X) || !TargetPositionMatches(actual.MinY,change.Y))
                    throw new InvalidDataException($"“{SafePieceName(piece.Name)} (kimlik: {piece.Id:D})” için istenen konum sayısal hassasiyetle yeterince temsil edilemiyor. Taslak korunuyor; daha küçük koordinatlar kullanın.");
            }
            var rule=translated is null ? null : document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==piece.Id);
            var movedRule=rule?.TranslatedFor(piece,translated!,dx,dy);
            prepared.Add((piece,change.Name,translated,translatedGrain,rule,movedRule));
        }
        foreach(var item in prepared)
        {
            if(item.Points is not null) { item.Piece.Points=item.Points;item.Piece.Grainline=item.Grainline; }
            if(item.MovedRule is not null)document.GradingRules[document.GradingRules.IndexOf(item.Rule!)]=item.MovedRule;
            item.Piece.Name=item.Name;
        }
    }
    // An eight-ULP bound is a representation check around the requested double,
    // not a manufacturing tolerance. In particular, signed zero only matches
    // zero or a few subnormal values, not a rounded-away requested position.
    private static bool TargetPositionMatches(double actual,double requested)
    {
        if(actual==requested)return true;
        var ulp=Math.Max(Math.Abs(Math.BitIncrement(requested)-requested),Math.Abs(requested-Math.BitDecrement(requested)));
        return Math.Abs(actual-requested)<=8*ulp;
    }
    private static string StaleRulesNotice(ProjectDocument document,IReadOnlyList<(Guid Id, string Name, double X, double Y)> changes)
    {
        var stale=new List<PatternPiece>();
        foreach(var change in changes)
        {
            var piece=document.Pieces.First(p=>p.Id==change.Id);var bounds=Geometry.Bounds(piece.Points);
            if(change.X==bounds.MinX && change.Y==bounds.MinY)continue;
            var rule=document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==piece.Id);
            if(rule is not null && (rule.SourceClosed!=piece.Closed || rule.Deltas.Count!=piece.Points.Count || !rule.SourcePoints.SequenceEqual(piece.Points)))stale.Add(piece);
        }
        if(stale.Count==0)return "";
        var names=string.Join(", ",stale.Take(3).Select(p=>$"{SafePieceName(p.Name)} ({p.Id:D})"));
        return $" · {stale.Count} parçada eski serileme kuralı korundu; yeni konumla ilişkisini serileme atölyesinde gözden geçirin: {names}"+
            (stale.Count>3 ? $" ve {stale.Count-3} parça daha." : ".");
    }
    private void ApplyPropertyDrafts(IEnumerable<Guid> ids)
    {
        if(BlockForIncompleteRecovery())return;
        var changes=ValidatePropertyDrafts(Document,ids);
        if(changes.Count==0)return;
        var staleNotice=StaleRulesNotice(Document,changes);
        bool changed;
        try
        {
            changed=session.Execute(document=>ApplyDraftChanges(document,changes),ProjectStorage.Validate);
        }
        catch { dirty=session.IsDirty;Refresh();throw; }
        foreach(var change in changes)propertyDrafts.Remove(change.Id);
        dirty=session.IsDirty;
        try { Refresh();SetStatus("Parça özellikleri uygulandı"+staleNotice); }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            if(changed)_=SaveRecoveryAsync();
            throw new InvalidOperationException(changed
                ? "Parça özellikleri projeye uygulandı ancak görünüm yenilenemedi. Çalışmayı kontrol edip kaydedin; işlem geri alınabilir."
                : "Parça değerleri değişmedi; taslak temizlendi ancak görünüm yenilenemedi. Çalışma alanını kontrol edin.",ex);
        }
    }
    internal sealed record GrainlineEditStamp(ProjectDocument Document,ProjectDocument Snapshot,Guid Epoch,Guid PieceId);

    // The window holds only a detached drawing; this stamp must be rechecked
    // immediately before the single document transaction.
    internal GrainlineEditStamp? CaptureGrainlineEdit()
    {
        if(!EnsureMainMutationReady() || BlockForIncompleteRecovery() || BlockForDxfImport() || BlockForExport())return null;
        if(HasPendingCanvasOrPropertyWork)
        { SetStatus("İplik yönü düzenlenmedi · Önce bekleyen çizimi, sürüklemeyi veya alan taslaklarını tamamlayın.");return null; }
        if(Selected is not { } piece)
        { SetStatus("İplik yönünü düzenlemek için önce bir parça seçin.");return null; }
        var current=Document;return new(current,current.Clone(),projectEpoch,piece.Id);
    }
    internal string? GrainlineEditProblem(GrainlineEditStamp stamp)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        if(windowClosed || recoveryClosing || recoveryRestoreIncomplete || DxfImportRunning || ExportRunning || HasPendingCanvasOrPropertyWork ||
           projectEpoch!=stamp.Epoch || !ReferenceEquals(Document,stamp.Document) || SelectedId!=stamp.PieceId ||
           !DocumentSession.ContentEquals(Document,stamp.Snapshot))
            return "Proje, seçim veya bekleyen çalışma değişti. İplik yönü uygulanmadı; değerleri not edip güncel parçayla yeniden açın.";
        return null;
    }
    internal bool ApplyGrainlineEdit(GrainlineEditStamp stamp,Grainline? value)
    {
        if(GrainlineEditProblem(stamp) is { } problem) { SetStatus(problem);return false; }
        var piece=Document.Pieces.FirstOrDefault(p=>p.Id==stamp.PieceId);
        if(piece is null) { SetStatus("Kaynak parça bulunamadı; iplik yönü uygulanmadı.");return false; }
        var prepared=value?.Clone();
        if(prepared is not null)
        {
            Geometry.Validate(prepared.Start);Geometry.Validate(prepared.End);
            if(prepared.Start==prepared.End)throw new ArgumentException("İplik yönünün iki ucu farklı olmalıdır.");
        }
        if(GrainlineTransform.Same(piece.Grainline,prepared))
        { SetStatus("İplik yönü değişmedi · Proje ve işlem geçmişi korundu.");return false; }
        var committed=false;
        try
        {
            CommitObserved(prepared is null ? "İplik yönü kaldırıldı" : "İplik yönü uygulandı",
                ()=>piece.Grainline=prepared,()=>committed=true);
            return committed;
        }
        catch(Exception ex) when(committed && ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            mainMutationReviewRequired=true;
            throw new InvalidOperationException($"İplik yönü projeye uygulandı (parça: {stamp.PieceId:D}), ancak görünüm yenilenemedi. Sonucu ve geri alma geçmişini kontrol edin; işlemi hemen tekrar etmeyin.",ex);
        }
    }
    private void EditGrainline()
    {
        var stamp=CaptureGrainlineEdit();if(stamp is null)return;
        var source=stamp.Snapshot.Pieces.First(p=>p.Id==stamp.PieceId);
        var dialog=new GrainlineEditorWindow(this,source,()=>GrainlineEditProblem(stamp));
        if(dialog.ShowDialog()==true)ApplyGrainlineEdit(stamp,dialog.Result);
    }
    private void OpenGroupMove()
    {
        if(!EnsureMainMutationReady() || BlockForIncompleteRecovery() || BlockForDxfImport() || BlockForExport())return;
        if(HasPendingCanvasOrPropertyWork) { SetStatus("Önce bekleyen çizim veya alan taslaklarını tamamlayın.");return; }
        if(Document.Pieces.Count<2) { SetStatus("Toplu taşıma için en az iki parça gereklidir.");return; }
        new GroupMoveWindow(this).ShowDialog();
    }

    private void EditPieceMetadata()
    {
        if(!EnsureMainMutationReady())return;
        BuildPieceMetadataDialog()?.ShowDialog();
    }
    private bool EnsureMainMutationReady()
    {
        if(!mainMutationReviewRequired)return true;
        try
        {
            Refresh();
            mainMutationReviewRequired=false;
            SetStatus("Görünüm yenilendi · Önceki işlem uygulanmıştı. Sonucu kontrol edin; yeni işlem için tekrar seçin.");
        }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            throw new InvalidOperationException("Önceki işlem projeye uygulandı ancak görünüm hâlâ yenilenemiyor. Tekrar işlem yapmadan çalışmayı kontrol edin.",ex);
        }
        return false;
    }
    // Constructed separately so the stale-document boundary can also be checked
    // without displaying a modal window in background verification.
    internal Window? BuildPieceMetadataDialog()
    {
        if(!EnsureMainMutationReady())return null;
        if (Selected is not { } piece) return null;
        var originalEpoch=projectEpoch;
        var pieceId=piece.Id;
        var originalMetadata=piece.Metadata?.Clone();
        var dialog=new Window { Owner=this, Title="Parça bilgileri", Width=480, Height=600, MinWidth=400, MinHeight=360, WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(dialog);
        var root=new DockPanel { Margin=new Thickness(20) }; dialog.Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var panel=new StackPanel();
        var scroller=new ScrollViewer { Content=panel, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Focusable=true };
        AutomationProperties.SetName(scroller,"Parça kimliği ve bilgileri");root.Children.Add(scroller);
        var selectedName=new TextBox { Text=SafePieceName(piece.Name), IsReadOnly=true, TextWrapping=TextWrapping.Wrap, MaxHeight=54, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Margin=new Thickness(3), Padding=new Thickness(5) };
        var selectedIdentity=new TextBox { Text=pieceId.ToString("D"), IsReadOnly=true, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(3), Padding=new Thickness(5) };
        AutomationProperties.SetName(selectedName,"Düzenlenen parça adı");
        AutomationProperties.SetHelpText(selectedName,"Adın tamamını görmek ve kopyalamak için alanı kullanın; kontrol karakterleri açık gösterilir.");
        AutomationProperties.SetName(selectedIdentity,"Düzenlenen parçanın tam kimliği");
        AutomationProperties.SetHelpText(selectedIdentity,"Aynı adlı parçaları ayırt etmek için tam kimliği seçip kopyalayabilirsiniz.");
        panel.Children.Add(new Label { Content="Düzenlenen parça",Target=selectedName,Padding=new Thickness(0) });panel.Children.Add(selectedName);
        panel.Children.Add(new Label { Content="Parça kimliği",Target=selectedIdentity,Padding=new Thickness(0) });panel.Children.Add(selectedIdentity);
        panel.Children.Add(new TextBlock { Text="Adetli pastalda kesim rolü, adet ve tek malzeme kullanılır. Temel pastal her parçayı bir kez yerleştirir ve rol veya adedi dikkate almaz. Dışa aktarılan DXF, SVG ve HPGL dosyaları bu bilgileri taşımaz.", TextWrapping=TextWrapping.Wrap, Foreground=Muted, Margin=new Thickness(0,0,0,12) });
        panel.Children.Add(Label("Parça rolü"));
        var role=new ComboBox { ItemsSource=new[] { "Belirtilmedi", "Kesim", "Yardımcı" }, SelectedItem=piece.Metadata?.Role ?? "Belirtilmedi", Margin=new Thickness(3), Padding=new Thickness(8) }; AutomationProperties.SetName(role,"Parça rolü"); panel.Children.Add(role);
        TextBox Field(string label,string? value) { panel.Children.Add(Label(label)); var input=new TextBox { Text=value ?? "", MaxLength=128 }; AutomationProperties.SetName(input,label); StyleInput(input); panel.Children.Add(input); return input; }
        var size=Field("Beden",piece.Metadata?.Size); var material=Field("Malzeme",piece.Metadata?.Material); var quantity=Field("Adet (1–100000)",piece.Metadata?.Quantity?.ToString(CultureInfo.InvariantCulture));
        var error=new TextBlock { Foreground=StudioTheme.Danger,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,6,3,3) }; AutomationProperties.SetName(error,"Parça bilgileri doğrulama hatası"); AutomationProperties.SetLiveSetting(error,AutomationLiveSetting.Assertive); panel.Children.Add(error);
        void ShowValidationError(string message)
        {
            error.Text=message;
            AutomationProperties.SetName(error,"Parça bilgileri doğrulama hatası: "+message);
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(error) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        var cancel=AddButton(actions,"Vazgeç",()=>dialog.DialogResult=false);cancel.IsCancel=true;
        Button apply=null!;
        apply=AddButton(actions,"Bilgileri uygula",()=>
        {
            var current=Selected;
            if(projectEpoch!=originalEpoch || SelectedId!=pieceId || current is null || current.Id!=pieceId)
            {
                ShowValidationError("Proje veya seçili parça bu pencere açıkken değişti; bilgiler uygulanmadı. Girdileriniz burada korunuyor. Değerleri not edip pencereyi kapatın, parçayı yeniden seçin ve bilgileri tekrar girin.");
                return;
            }
            static bool SameMetadata(PieceMetadata? a,PieceMetadata? b) =>
                a is null ? b is null : b is not null && a.Role==b.Role && a.Size==b.Size && a.Material==b.Material && a.Quantity==b.Quantity;
            if(!SameMetadata(current.Metadata,originalMetadata))
            {
                ShowValidationError("Bu parçanın bilgileri pencere açıkken değişti; üzerine yazılmadı. Girdileriniz burada korunuyor. Pencereyi kapatmadan değerlerinizi not edin, ardından güncel bilgileri kontrol edin.");
                return;
            }
            static string? Clean(string text) => string.IsNullOrWhiteSpace(text)?null:text.Trim();
            var selectedRole=role.SelectedItem as string; var sizeValue=Clean(size.Text); var materialValue=Clean(material.Text); var raw=quantity.Text.Trim(); int? count=null;
            if(raw.Length>0) { if(!int.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out var value)||value<1||value>100000) { ShowValidationError("Adet 1 ile 100000 arasında tam sayı olmalıdır.");return; } count=value; }
            if((sizeValue?.Any(char.IsControl) ?? false)||(materialValue?.Any(char.IsControl) ?? false)) { ShowValidationError("Beden ve malzeme kontrol karakteri içeremez.");return; }
            var roleValue=selectedRole=="Belirtilmedi"?null:selectedRole;
            var metadata=roleValue is null && sizeValue is null && materialValue is null && count is null ? null : new PieceMetadata { Role=roleValue,Size=sizeValue,Material=materialValue,Quantity=count };
            // Commit against the currently owned piece, not the snapshot captured
            // when the dialog opened. Unrelated edits and geometry are retained.
            var committed=false;
            try { CommitObserved("Parça bilgileri uygulandı",()=>current.Metadata=metadata,()=>committed=true); }
            catch(Exception ex) when(committed && ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                mainMutationReviewRequired=true;apply.IsEnabled=false;
                ShowValidationError($"Parça bilgileri projeye uygulandı (kimlik: {pieceId:D}) ancak görünüm yenilenemedi. Pencereyi kapatıp sonucu kontrol edin; işlem geri alınabilir. Ayrıntı: {ex.Message}");
                return;
            }
            if(MetadataDialogSuccessForVerification is { } completed)completed(dialog);
            else dialog.DialogResult=true;
        });apply.IsDefault=true;
        return dialog;
    }
    private void Transform(Func<IReadOnlyList<Point2>,Point2,List<Point2>> transform)
    {
        if(!EnsureMainMutationReady())return;
        if(Selected is not { Points.Count: >0 } piece)return;
        if(canvas.LargeVertexValidationRunning || canvas.LargeCompletionRunning)
        { SetStatus("Dönüşüm uygulanmadı · Doğrulamanın bitmesini bekleyin veya Esc ile iptal edin.");return; }
        if(canvas.HasActiveDragPreview)
        { SetStatus("Dönüşüm uygulanmadı · Sürüklemeyi tamamlayın veya Esc ile iptal edin.");return; }
        if(canvas.HasPendingDrawing)
        { SetStatus("Dönüşüm uygulanmadı · Önce çizimi tamamlayın veya Esc ile iptal edin.");return; }
        var center=Geometry.Bounds(piece.Points).Center;
        var result=transform(piece.Points,center);
        var gradingNeedsReview=!result.SequenceEqual(piece.Points) && Document.GradingRules.Any(r=>r.SourcePieceId==piece.Id);
        var transformedGrain=GrainlineTransform.Transform(piece.Grainline,piece.Points[0],result[0],points=>transform(points,center));
        var committed=false;
        try {
            CommitObserved("Geometri dönüşümü uygulandı",()=> { piece.Points=result;piece.Grainline=transformedGrain; },()=>committed=true);
            if(gradingNeedsReview)ShowCanvasError("Dönüşüm uygulandı; kayıtlı serileme kuralı önceki kontura ait. Serilemeden önce kuralı gözden geçirin.");
        }
        catch(Exception ex) when(committed && ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            mainMutationReviewRequired=true;
            throw new InvalidOperationException($"Geometri dönüşümü projeye uygulandı (parça kimliği: {piece.Id:D}) ancak görünüm yenilenemedi. Sonucu ve geri alma geçmişini kontrol edin; işlemi hemen tekrar etmeyin.",ex);
        }
    }
    internal Func<PatternPiece,bool>? ConfirmDuplicateForVerification { get; set; }
    internal Func<PatternPiece,DuplicateOptions?>? ChooseDuplicateForVerification { get; set; }
    internal void DuplicateSelectedPiece()
    {
        if(!EnsureMainMutationReady() || BlockForIncompleteRecovery() || BlockForDxfImport() || BlockForExport())return;
        if(HasPendingCanvasOrPropertyWork)
        { SetStatus("Parça çoğaltılmadı · Önce tamamlanmamış çizimi, sürüklemeyi veya alan taslaklarını tamamlayın ya da iptal edin."); return; }
        if(Selected is not { } sourcePiece)
        { SetStatus("Çoğaltmak için önce bir parça seçin.");return; }
        var originalDocument=Document;
        var originalContent=originalDocument.Clone();
        var originalEpoch=projectEpoch;
        var sourceId=sourcePiece.Id;
        var sourceRule=Document.GradingRules.SingleOrDefault(r=>r.SourcePieceId==sourceId);
        // Bound the input before escaping so control-character escape sequences
        // and surrogate pairs are never truncated midway in the confirmation.
        var previewLength=Math.Min(sourcePiece.Name.Length,20);
        if(previewLength<sourcePiece.Name.Length && previewLength>0 && char.IsHighSurrogate(sourcePiece.Name[previewLength-1]))previewLength--;
        var shownName=SafePieceName(sourcePiece.Name[..previewLength])+(previewLength<sourcePiece.Name.Length?"…":"");
        DuplicateOptions? choice;
        if(ChooseDuplicateForVerification is { } choose) choice=choose(sourcePiece.Clone());
        else if(ConfirmDuplicateForVerification is { } confirm) choice=confirm(sourcePiece.Clone()) ? new DuplicateOptions(false,true) : null;
        else
        {
            var dialog=new DuplicatePieceWindow(this,sourcePiece.Clone(),shownName,sourceRule?.Clone());
            choice=dialog.ShowDialog()==true ? dialog.Options : null;
        }
        if(choice is null)return;
        if(HasPendingCanvasOrPropertyWork || recoveryRestoreIncomplete || BlockForDxfImport() || BlockForExport() || projectEpoch!=originalEpoch ||
           !ReferenceEquals(Document,originalDocument) || SelectedId!=sourceId || !DocumentSession.ContentEquals(Document,originalContent))
        { SetStatus("Proje, seçim veya bekleyen çalışma değişti · Parça çoğaltılmadı. Yeniden kontrol edin.");return; }
        if(choice.Value.CopyGrading && !DuplicatePieceWindow.RuleMatches(sourcePiece,sourceRule))
        { SetStatus("Kaynak serileme kuralı geometriyle eşleşmiyor veya yok · Parça çoğaltılmadı.");return; }
        const string suffix=" · kopya";
        var length=Math.Min(sourcePiece.Name.Length,1024-suffix.Length);
        if(length<sourcePiece.Name.Length && length>0 && char.IsHighSurrogate(sourcePiece.Name[length-1]))length--;
        var duplicate=new PatternPiece { Id=Guid.NewGuid(),Name=sourcePiece.Name[..length]+suffix,
            Points=[..sourcePiece.Points],Closed=sourcePiece.Closed,Metadata=choice.Value.CopyMetadata ? sourcePiece.Metadata?.Clone() : null,
            Grainline=choice.Value.CopyGrainline ? sourcePiece.Grainline?.Clone() : null };
        var copiedRule=choice.Value.CopyGrading ? sourceRule!.Clone() : null;
        if(copiedRule is not null)copiedRule.SourcePieceId=duplicate.Id;
        var committed=false;
        try
        {
            CommitObserved("Parça çoğaltıldı",()=> { Document.Pieces.Add(duplicate);if(copiedRule is not null)Document.GradingRules.Add(copiedRule);SelectedId=duplicate.Id; },()=>committed=true);
            if(committed)SetStatus($"Parça çoğaltıldı · Kaynak {sourceId:D} · Yeni {duplicate.Id:D}. Aynı konumdalar; üretim bilgisi {(duplicate.Metadata is null ? "kopyalanmadı" : "kopyalandı; adedi kontrol edin")}; iplik yönü {(duplicate.Grainline is null ? "kopyalanmadı" : "kopyalandı")}; serileme kuralı {(copiedRule is null ? "kopyalanmadı" : "kopyalandı")}. Pastal ve kaynak geçmişi aktarılmadı.");
        }
        catch(Exception ex) when(committed && ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            mainMutationReviewRequired=true;
            throw new InvalidOperationException($"Parça projede çoğaltıldı (yeni kimlik: {duplicate.Id:D}), ancak görünüm yenilenemedi. Sonucu ve geri alma geçmişini kontrol edin; işlemi hemen tekrar etmeyin.",ex);
        }
    }
    private void DeleteSelection()
    {
        if(!EnsureMainMutationReady())return;
        if(Selected is not {} p) return;
        if(propertyDrafts.ContainsKey(p.Id))
        { SetStatus("Parça silinmedi · Önce bu parçanın alan taslağını uygulayın veya taslakları inceleyip kaldırın.");return; }
        var count=Document.GradingRules.Count(r=>r.SourcePieceId==p.Id);
        if(count>0)
        {
            var originalDocument=Document;
            var originalContent=Document.Clone();
            var name=p.Name;
            var length=Math.Min(name.Length,80);
            if(length<name.Length && length>0 && char.IsHighSurrogate(name[length-1]))length--;
            var shownName=SafePieceName(name[..length])+(length<name.Length?"…":"");
            var confirmed=ConfirmDeleteWithGradingForVerification is { } confirm
                ? confirm(p.Clone(),count)
                : MessageBox.Show(this,$"‘{shownName}’ parçası ve bu parçaya bağlı {count} serileme kuralı silinecek. Geri alma yalnızca bu oturumdaki işlem geçmişi açıkken mümkündür; uygulama kapandıktan sonra bu geçmiş korunmaz. Devam edilsin mi?",
                    "Serileme kuralları da silinecek",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
            if(!confirmed)return;
            if(!ReferenceEquals(Document,originalDocument) || SelectedId!=p.Id || propertyDrafts.ContainsKey(p.Id) || !DocumentSession.ContentEquals(Document,originalContent))
            { SetStatus("Proje veya seçim değişti · Parça silinmedi. Tekrar kontrol edin.");return; }
        }
        var committed=false;
        try { CommitObserved("Parça silindi",()=> { Document.Pieces.Remove(p); Document.GradingRules.RemoveAll(r=>r.SourcePieceId==p.Id); SelectedId=null; },()=>committed=true); }
        catch(Exception ex) when(committed && ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            mainMutationReviewRequired=true;
            throw new InvalidOperationException($"Parça projeden silindi (kimlik: {p.Id:D}) ancak görünüm yenilenemedi. Geri alma geçmişini kontrol edin; işlemi hemen tekrar etmeyin.",ex);
        }
    }
    internal Func<bool>? ConfirmDiscardDrawingForVerification { get; set; }
    private bool ConfirmDiscardPendingDrawing(string action)
    {
        if(canvas is null || !canvas.HasPendingDrawing)return true;
        if(ConfirmDiscardDrawingForVerification is { } confirm)return confirm();
        return MessageBox.Show(this,$"{action} tamamlanmamış çizimin {canvas.PendingDrawingPointCount} noktası silinecek. Çizim proje dosyasına dahil değildir. İşleme devam edilirse bu oturumun otomatik kurtarma kaydı da temizlenebilir veya güncellenebilir; çizimi daha sonra kurtaramayabilirsiniz. Devam edilsin mi?",
            "Tamamlanmamış çizim",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
    }
    private bool ConfirmStableDiscardPendingDrawing(string action)
    {
        if(canvas is null || !canvas.HasPendingDrawing)return true;
        var stamp=CaptureDxfPreparationStamp();
        if(!ConfirmDiscardPendingDrawing(action))return false;
        if(DxfStampMatches(stamp))return true;
        SetStatus("Onay sırasında proje, seçim veya bekleyen çizim değişti; işlem uygulanmadı. Güncel çalışmayı kontrol edin.");
        return false;
    }
    private void Undo() => ApplyHistoryChange(false);
    private void Redo() => ApplyHistoryChange(true);
    private void ApplyHistoryChange(bool redo)
    {
        if(BlockForIncompleteRecovery() || BlockForActiveDrag() || BlockForLargeVertexValidation() || (redo ? session.RedoCount : session.UndoCount)==0 ||
           !ConfirmStableDiscardPendingDrawing(redo ? "Yinelenirse" : "Geri alınırsa"))return;
        if(BlockForActiveDrag() || BlockForLargeVertexValidation() || !(redo ? session.Redo() : session.Undo()))return;
        // The history operation has already changed the document. A later UI
        // failure must not encourage the user to undo the next history entry.
        dirty=session.IsDirty;
        try { canvas.CancelDrawing(); Refresh(); }
        catch(Exception ex)
        { throw new InvalidOperationException((redo ? "Yineleme" : "Geri alma")+" uygulandı; görünüm yenilenemedi. Tekrar denemeden önce proje ve işlem geçmişini kontrol edin.",ex); }
    }
    private bool CanDiscard() => CanDiscard(out _);
    private bool CanDiscard(out bool saved)
    {
        saved=false;
        if (BlockForActiveDrag()) return false;
        if (BlockForDxfImport()) return false;
        if (BlockForExport()) return false;
        if(BlockForLargeVertexValidation())return false;
        // Confirm without clearing: a later save, dialog cancellation or load failure
        // must leave the unfinished drawing intact until replacement actually occurs.
        if(!ConfirmStableDiscardPendingDrawing("Bu işlem sürdürülürse"))return false;
        var approvedDrawing=canvas.CapturePendingDrawing();
        if (!HasUnsavedDocumentWork) return true;
        var saveCompleted=false;
        var prompt=CreateDiscardPrompt(()=>saveCompleted=true);
        var accepted=prompt.ShowDialog() == true;
        var currentDrawing=canvas.CapturePendingDrawing();
        if(accepted && (approvedDrawing is null ? currentDrawing is not null :
            currentDrawing is null || approvedDrawing.Tool!=currentDrawing.Tool || !approvedDrawing.Points.SequenceEqual(currentDrawing.Points)))
        { SetStatus("Kaydetme onayı sırasında bekleyen çizim değişti; işlem uygulanmadı. Güncel çizimi kontrol edin.");return false; }
        saved=accepted && saveCompleted;
        return accepted;
    }
    internal Window CreateDiscardPrompt(Action onSaved)
    {
        ArgumentNullException.ThrowIfNull(onSaved);
        var prompt = new Window { Owner = this, Title = "Değişiklikleri kaydet", Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(prompt);
        var panel = new StackPanel { Margin = new Thickness(24) }; prompt.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Çalışmanızı kaydetmek ister misiniz?", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var fullName=SafePieceName(Document.Name);
        var length=Math.Min(fullName.Length,120);
        if(length<fullName.Length && length>0 && char.IsHighSurrogate(fullName[length-1]))length--;
        var shownName=fullName[..length]+(length<fullName.Length ? "…" : "");
        var documentNotice=new TextBlock { Text = $"“{shownName}” projesinde kaydedilmemiş değişiklikler var.", TextWrapping = TextWrapping.NoWrap, TextTrimming=TextTrimming.CharacterEllipsis, Foreground = Muted, Margin = new Thickness(0, 0, 0, 20), ToolTip=fullName };
        AutomationProperties.SetName(documentNotice,"Kaydedilmemiş proje: "+fullName);
        panel.Children.Add(documentNotice);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        var cancel = AddButton(buttons, "Vazgeç", () => prompt.DialogResult = false); cancel.IsCancel = true;
        AddButton(buttons, "Kaydetmeden devam et", () => prompt.DialogResult = true);
        var save = AddButton(buttons, "Kaydet", () => { Save(); if (!HasUnsavedDocumentWork) { onSaved();prompt.DialogResult = true; } }, projectSaveAction:true);
        save.Background = StudioTheme.Accent; save.Foreground = StudioTheme.Background; save.IsDefault = true;
        return prompt;
    }
    private void Open()
    {
        if(BlockForDxfImport())return;
        if(projectOpenRunning) { SetStatus("Proje açma işlemi sürüyor.");return; }
        var dlg = new OpenFileDialog { Filter = "Mono Di Tree CAD proje (*.tcad)|*.tcad|JSON (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        _ = OpenProjectFileAsync(dlg.FileName);
    }
    internal void CancelProjectOpen()
    {
        projectOpenGeneration++;
        if(projectOpenCancel is not null)projectOpenCancel.Visibility=Visibility.Collapsed;
        if(projectOpenRunning && !windowClosed)SetStatus("Proje açma iptal edildi; okuma bitene kadar yeni açma işlemi bekleyecek ve sonuç kullanılmayacak.");
    }
    private bool OpenApprovalStillValid(DxfPreparationStamp stamp, Guid epoch, long generation) =>
        generation==projectOpenGeneration && !windowClosed && !recoveryClosing && !recoveryRestoreIncomplete &&
        !DxfImportRunning && !dxfReviewActive && !ExportRunning && projectEpoch==epoch && DxfStampMatches(stamp);

    internal async Task OpenProjectFileAsync(string path)
    {
        if(projectOpenRunning || windowClosed || recoveryClosing)return;
        projectOpenRunning=true;
        var generation=++projectOpenGeneration;
        if(projectOpenCancel is not null)projectOpenCancel.Visibility=Visibility.Visible;
        SetStatus("Proje arka planda okunuyor…");
        try
        {
            var loaded=await Task.Run(()=> { BeforeProjectReadForVerification?.Invoke();return ProjectStorage.LoadWithVersion(path); });
            if(generation!=projectOpenGeneration || windowClosed || recoveryClosing)return;
            if(BlockForDxfImport() || BlockForExport() || BlockForActiveDrag() || BlockForLargeVertexValidation())return;
            if(recoveryRestoreIncomplete || dxfReviewActive)
            { SetStatus("Proje açılmadı · Önce devam eden kurtarma veya DXF incelemesini tamamlayın.");return; }
            var epoch=projectEpoch;
            var stamp=CaptureDxfPreparationStamp();
            if(canvas.HasPendingDrawing)
            {
                var accepted=ConfirmOpenDrawingForVerification?.Invoke() ?? ConfirmDiscardPendingDrawing("Bu işlem sürdürülürse");
                if(!accepted) { SetStatus("Proje açma iptal edildi · Mevcut çalışma korundu.");return; }
                if(!OpenApprovalStillValid(stamp,epoch,generation))
                { if(generation==projectOpenGeneration && !windowClosed && !recoveryClosing)SetStatus("Çalışma değişti · Proje açılmadı. Yeniden deneyin.");return; }
            }
            if(HasUnsavedDocumentWork)
            {
                bool accepted,saved;
                if(ConfirmOpenDiscardForVerification is { } confirm) { var decision=confirm();accepted=decision.Accepted;saved=decision.Saved; }
                else
                {
                    saved=false;
                    var didSave=false;
                    accepted=CreateDiscardPrompt(()=>didSave=true).ShowDialog()==true;
                    saved=didSave;
                }
                // Saving can apply drafts and change the document. Require a fresh
                // open rather than extending approval to an altered state.
                if(saved) { if(!windowClosed)SetStatus("Çalışma kaydedildi. Projeyi açmak için yeniden seçin.");return; }
                if(!accepted) { SetStatus("Proje açma iptal edildi · Mevcut çalışma korundu.");return; }
                if(!OpenApprovalStillValid(stamp,epoch,generation))
                { if(generation==projectOpenGeneration && !windowClosed && !recoveryClosing)SetStatus("Çalışma değişti · Proje açılmadı. Yeniden deneyin.");return; }
            }
            if(!OpenApprovalStillValid(stamp,epoch,generation))
            { if(generation==projectOpenGeneration && !windowClosed && !recoveryClosing)SetStatus("Çalışma değişti · Proje açılmadı. Yeniden deneyin.");return; }
            // The prompt may have been open while another application changed
            // the source. Compare the same byte-based version before replacing
            // the workspace; this narrows, but cannot eliminate, later races.
            var currentVersion=await Task.Run(()=>ProjectStorage.CaptureVersion(loaded.Version.FullPath));
            if(!OpenApprovalStillValid(stamp,epoch,generation))
            { if(generation==projectOpenGeneration && !windowClosed && !recoveryClosing)SetStatus("Çalışma değişti · Proje açılmadı. Yeniden deneyin.");return; }
            if(!Equals(currentVersion,loaded.Version))
            { SetStatus("Kaynak dosya okunduktan sonra değişti veya kaldırıldı · Proje açılmadı. Dosyayı yeniden seçin.");return; }
            ApplyOpenedProject(loaded);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            if(generation==projectOpenGeneration && !windowClosed && !recoveryClosing)
            {
                SetStatus("Proje açma işlemi tamamlanamadı · Ayrıntıyı kontrol edin.");
                if(ProjectOpenErrorForVerification is { } report)report(ex);
                else MessageBox.Show(this,ex.Message,"İşlem tamamlanamadı",MessageBoxButton.OK,MessageBoxImage.Warning);
            }
        }
        finally
        {
            projectOpenRunning=false;
            if(projectOpenCancel is not null)projectOpenCancel.Visibility=Visibility.Collapsed;
        }
    }
    // Called only after the user has approved replacement of the current work.
    // Parsing and validation occur before changing the live document.
    internal void OpenProjectFile(string path)
    {
        var loaded=ProjectStorage.LoadWithVersion(path);
        ApplyOpenedProject(loaded);
    }
    private void ApplyOpenedProject(ProjectLoadResult loaded)
    {
        var applied=false;
        try
        {
            LoadDocumentCore(loaded.Project,false,()=>
            {
                filePath=loaded.Version.FullPath;fileVersion=loaded.Version;
                savedFileHasGradingRules=loaded.Project.GradingRules.Count>0;
                savedFileHasMetadata=loaded.Project.Pieces.Any(p=>p.Metadata is not null);
                savedFileHasNestingSnapshot=loaded.Project.NestingSnapshot is not null;
                savedFileHasGrainline=loaded.Project.Pieces.Any(p=>p.Grainline is not null);
                savedFileHasImportProvenance=loaded.Project.ImportProvenance is not null;
                applied=true;
            },openingProject:true);
            SetStatus("Proje açıldı: "+filePath);
        }
        catch(Exception ex) when(applied && ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            throw new InvalidOperationException("Proje açıldı, ancak görünüm yenilenemedi. Açılan proje ve dosya ilişkisi korundu; önceki çalışma geri getirilmedi. Ana çalışma alanını kontrol edin.",ex);
        }
    }
    private void Save()
    {
        EnsureNoActiveDrag();
        EnsureNoExport();
        EnsureNoLargeVertexValidation();
        if (filePath is null) { SaveAs(); return; }
        if ((Document.GradingRules.Count>0 && !savedFileHasGradingRules) || (Document.Pieces.Any(p=>p.Metadata is not null) && !savedFileHasMetadata) || (Document.NestingSnapshot is not null && !savedFileHasNestingSnapshot) || (Document.Pieces.Any(p=>p.Grainline is not null) && !savedFileHasGrainline) || (Document.ImportProvenance is not null && !savedFileHasImportProvenance))
        {
            var choice=ChooseGradingCompatibility(true);
            if(choice==GradingSaveChoice.Cancel)return;
            if(choice==GradingSaveChoice.SaveAs) { SaveAsCore(true); return; }
        }
        SaveTo(filePath);
    }
    private void SaveTo(string path) => SaveToCore(path,null);
    private void SaveToCore(string path,ProjectFileVersion? approvedVersion)
    {
        EnsureNoActiveDrag();
        EnsureNoExport();
        var recoveryDirectory = Path.TrimEndingDirectorySeparator(AppPaths.RecoveryDirectory) + Path.DirectorySeparatorChar;
        if(Path.GetFullPath(path).StartsWith(recoveryDirectory,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Kalıcı projeyi yönetilen kurtarma klasörünün dışında kaydedin.");
        EnsureWritableDestination(path);
        // Prepare a detached snapshot so a failed write cannot change the live session.
        var changes=ValidatePropertyDrafts(Document,propertyDrafts.Keys);
        var staleNotice=StaleRulesNotice(Document,changes);
        var candidate=Document.Clone();
        ApplyDraftChanges(candidate,changes);
        Validate(candidate);
        var expected=approvedVersion ?? (fileVersion is not null && filePath is not null && string.Equals(Path.GetFullPath(path),Path.GetFullPath(filePath),StringComparison.OrdinalIgnoreCase)
            ? fileVersion : ProjectStorage.CaptureVersion(path));
        var savedVersion=ProjectStorage.SaveIfUnchanged(candidate,path,expected);
        // Track the new version even if updating the live session unexpectedly fails.
        filePath=path; fileVersion=savedVersion;
        try
        {
            if(changes.Count>0)session.Execute(document=>ApplyDraftChanges(document,changes),ProjectStorage.Validate);
        }
        catch(Exception ex)
        {
            dirty=session.IsDirty;
            try { Refresh(); }
            catch(Exception refreshEx) when(refreshEx is not OutOfMemoryException and not StackOverflowException and not AccessViolationException) { }
            throw new InvalidOperationException($"Dosya kaydedildi: {SafePieceName(Path.GetFullPath(path))}. Ancak alan taslakları çalışma alanına uygulanamadı. Taslaklar korundu; yeniden deneyin.",ex);
        }
        foreach(var change in changes)propertyDrafts.Remove(change.Id);
        filePath = path; fileVersion=savedVersion; savedFileHasGradingRules=Document.GradingRules.Count>0; savedFileHasMetadata=Document.Pieces.Any(p=>p.Metadata is not null); savedFileHasNestingSnapshot=Document.NestingSnapshot is not null; savedFileHasGrainline=Document.Pieces.Any(p=>p.Grainline is not null); savedFileHasImportProvenance=Document.ImportProvenance is not null; session.MarkSaved(); dirty = false;
        if(recoveredSource is not null) recoveredDocumentSaved=true;
        // A saved project does not include the unfinished drawing. Keep all recovery
        // snapshots until it is committed or explicitly discarded.
        try
        {
            if(!canvas.HasPendingDrawing) { ArchiveRecoveredSource(); ClearRecovery(); }
            Refresh(); SetStatus("Proje kaydedildi: " + path + (canvas.HasPendingDrawing ? " · Tamamlanmamış çizim proje dosyasına dahil değil; otomatik kurtarma sonraki kayıtta güncellenir" : "") + staleNotice);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            throw new InvalidOperationException($"Dosya kaydedildi: {SafePieceName(Path.GetFullPath(path))}. Ancak görünüm veya kurtarma durumu güncellenemedi. Kaydedilen dosya ve çalışma alanını kontrol edin.",ex);
        }
    }
    private void SaveNewCopy()
    {
        EnsureNoActiveDrag(); EnsureNoExport(); EnsureNoLargeVertexValidation();
        if((Document.GradingRules.Count>0 || Document.Pieces.Any(p=>p.Metadata is not null) || Document.NestingSnapshot is not null || Document.Pieces.Any(p=>p.Grainline is not null) || Document.ImportProvenance is not null) && ChooseGradingCompatibility(false)==GradingSaveChoice.Cancel)return;
        var dlg=new SaveFileDialog { Title="Yeni proje dosyası oluştur", Filter="Mono Di Tree CAD proje (*.tcad)|*.tcad", DefaultExt=".tcad", FileName=SafeFileName(Document.Name)+" - kopya.tcad", InitialDirectory=AppPaths.PrepareProjectsDirectory(), OverwritePrompt=false };
        if(dlg.ShowDialog(this)!=true)return;
        SaveNewCopyTo(dlg.FileName);
    }
    internal void SaveNewCopyTo(string path)
    {
        var expected=CaptureSaveAsDestination(path,true);
        if(expected.Sha256 is not null)
            throw new IOException("Bu adla bir dosya zaten var; üzerine yazılmadı. Yeni bir ad seçin.");
        // The missing-version token commits with fail-if-exists, including when
        // another process creates the destination after this capture.
        SaveAsTo(path,expected,true);
    }
    private void SaveAs() => SaveAsCore(false);
    private void SaveAsCore(bool compatibilityAcknowledged)
    {
        EnsureNoActiveDrag();
        EnsureNoExport();
        EnsureNoLargeVertexValidation();
        if((Document.GradingRules.Count>0 || Document.Pieces.Any(p=>p.Metadata is not null) || Document.NestingSnapshot is not null || Document.Pieces.Any(p=>p.Grainline is not null) || Document.ImportProvenance is not null) && !compatibilityAcknowledged && ChooseGradingCompatibility(false)==GradingSaveChoice.Cancel)return;
        var dlg = new SaveFileDialog { Filter="Mono Di Tree CAD proje (*.tcad)|*.tcad", DefaultExt=".tcad", FileName=SafeFileName(Document.Name)+".tcad", InitialDirectory=AppPaths.PrepareProjectsDirectory() };
        if (dlg.ShowDialog(this) != true) return;
        // Choosing “Farklı kaydet” in the compatibility dialog promises to
        // preserve the currently open file, including under aliases.
        var requireDistinctTarget=compatibilityAcknowledged && filePath is not null;
        var expected=CaptureSaveAsDestination(dlg.FileName,requireDistinctTarget);
        if(expected.Sha256 is not null && MessageBox.Show(this,
            "Seçilen dosya bu projeyle değiştirilsin mi? Kayıt tamamlanana kadar aynı dosyayı başka uygulamalarda değiştirmeyin. Dosyayı korumak için farklı bir ad seçebilirsiniz.",
            "Mevcut dosyayı değiştir",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        SaveAsTo(dlg.FileName,expected,requireDistinctTarget);
    }
    private void ValidateSaveAsDestination(string path,bool requireDistinctTarget)
    {
        EnsureNoExport(); EnsureNoLargeVertexValidation();
        if(!string.Equals(Path.GetExtension(path),".tcad",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Proje dosyası .tcad uzantısıyla kaydedilmelidir.");
        var recoveryDirectory=Path.TrimEndingDirectorySeparator(AppPaths.RecoveryDirectory)+Path.DirectorySeparatorChar;
        if(Path.GetFullPath(path).StartsWith(recoveryDirectory,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Kalıcı projeyi yönetilen kurtarma klasörünün dışında kaydedin.");
        EnsureWritableDestination(path);
        if(requireDistinctTarget && filePath is not null &&
            string.Equals(Path.GetFullPath(path),Path.GetFullPath(filePath),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Mevcut dosyayı korumak için farklı bir hedef seçin.");
        if(filePath is not null && !string.Equals(Path.GetFullPath(path),Path.GetFullPath(filePath),StringComparison.OrdinalIgnoreCase))
        {
            // A removed source must not prevent rescuing the in-memory document to
            // a different path. Other identity errors still fail closed.
            bool same;
            try { same=FileIdentity.SameExistingFile(filePath,path); }
            catch(FileNotFoundException) { same=ExistingSaveTargetWhenSourceMissing(path); }
            catch(DirectoryNotFoundException) { same=ExistingSaveTargetWhenSourceMissing(path); }
            if(same) throw new InvalidOperationException("Seçilen dosya açık projeyle aynı dosyadır; farklı bir hedef seçin.");
        }
    }
    private static bool ExistingSaveTargetWhenSourceMissing(string path)
    {
        try
        {
            using var handle=File.OpenHandle(Path.GetFullPath(path),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            throw new InvalidOperationException("Açık proje dosyası bulunamadı; var olan hedefin aynı dosya olmadığı doğrulanamıyor. Yeni bir dosya adı seçin.");
        }
        catch(FileNotFoundException) { return false; }
        catch(DirectoryNotFoundException) { return false; }
    }
    internal ProjectFileVersion CaptureSaveAsDestination(string path,bool requireDistinctTarget=false)
    {
        ValidateSaveAsDestination(path,requireDistinctTarget);
        var captured=ProjectStorage.CaptureVersion(path);
        if(filePath is not null && fileVersion is not null && string.Equals(Path.GetFullPath(path),Path.GetFullPath(filePath),StringComparison.OrdinalIgnoreCase)
            && !string.Equals(captured.Sha256,fileVersion.Sha256,StringComparison.Ordinal))
            throw new IOException("Açık proje dosyası dışarıda değişti; dosyayı yeniden açın veya farklı adla kaydedin.");
        return captured;
    }
    internal void SaveAsTo(string path,ProjectFileVersion approvedVersion,bool requireDistinctTarget=false)
    {
        ArgumentNullException.ThrowIfNull(approvedVersion);
        ValidateSaveAsDestination(path,requireDistinctTarget);
        if(filePath is not null && fileVersion is not null && string.Equals(Path.GetFullPath(path),Path.GetFullPath(filePath),StringComparison.OrdinalIgnoreCase)
            && (!string.Equals(approvedVersion.FullPath,Path.GetFullPath(filePath),StringComparison.OrdinalIgnoreCase)
                || !string.Equals(approvedVersion.Sha256,fileVersion.Sha256,StringComparison.Ordinal)))
            throw new IOException("Açık proje dosyası dışarıda değişti; dosyayı yeniden açın veya farklı adla kaydedin.");
        SaveToCore(path,approvedVersion);
    }
    private enum GradingSaveChoice { Cancel, SaveCurrent, SaveAs }
    private GradingSaveChoice ChooseGradingCompatibility(bool existingFile)
    {
        var choice=GradingSaveChoice.Cancel;
        var prompt=new Window { Owner=this,Title="Proje biçimi uyumluluğu",Width=530,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(prompt);
        var panel=new StackPanel { Margin=new Thickness(20) };prompt.Content=panel;
        var description=Document.ImportProvenance is not null
            ? "DXF kaynak ve aktarım notları proje biçimi sürüm 5 ile saklanır. Önceki TekstilCAD sürümleri bu dosyayı açamaz. Eski sürümle çalışacaksanız mevcut dosyayı koruyup farklı adla kaydedin."
            : Document.Pieces.Any(p=>p.Grainline is not null)
            ? "İplik yönü proje biçimi sürüm 4 ile saklanır. Önceki TekstilCAD sürümleri bu dosyayı açamaz. Eski sürümle çalışacaksanız mevcut dosyayı koruyup farklı adla kaydedin."
            : Document.NestingSnapshot is not null
            ? "Pastal geçmişi proje biçimi sürüm 3 ile saklanır. Önceki TekstilCAD sürümleri bu dosyayı açamaz. Eski sürümle çalışacaksanız mevcut dosyayı koruyup farklı adla kaydedin."
            : "Serileme kuralları ve parça bilgileri yeni proje biçiminde saklanır. Önceki TekstilCAD Studio 0.3; parça bilgileri içeren dosyalarda ayrıca 0.4 sürümü bu dosyayı açamaz. Eski sürümle çalışacaksanız mevcut dosyayı koruyup farklı adla kaydedin.";
        panel.Children.Add(new TextBlock { Text=description,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,18) });
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };panel.Children.Add(actions);
        var cancel=AddButton(actions,"Vazgeç",()=>prompt.DialogResult=false);cancel.IsCancel=true;
        if(existingFile)
        {
            AddButton(actions,"Bu dosyaya kaydet",()=> { choice=GradingSaveChoice.SaveCurrent;prompt.DialogResult=true; });
            var saveAs=AddButton(actions,"Farklı kaydet…",()=> { choice=GradingSaveChoice.SaveAs;prompt.DialogResult=true; });saveAs.IsDefault=true;
        }
        else { var proceed=AddButton(actions,"Devam et",()=> { choice=GradingSaveChoice.SaveAs;prompt.DialogResult=true; });proceed.IsDefault=true; }
        prompt.ShowDialog();return choice;
    }
    // Used only as a dialog suggestion; the project name itself is unchanged.
    // Leave room within the Windows 255 UTF-16-unit component limit for .tcad
    // and all supported export extensions.
    private static string SafeFileName(string? text)
    {
        const int maxUnits = 240;
        var invalid=Path.GetInvalidFileNameChars();
        var input=text ?? "";
        var output=new StringBuilder(Math.Min(input.Length,maxUnits));
        for(var i=0;i<input.Length && output.Length<maxUnits;i++)
        {
            var c=input[i];
            if(char.IsHighSurrogate(c) && i+1<input.Length && char.IsLowSurrogate(input[i+1]))
            {
                if(output.Length+2>maxUnits)break;
                output.Append(c).Append(input[++i]);
                continue;
            }
            var category=char.GetUnicodeCategory(c);
            output.Append(char.IsSurrogate(c) || invalid.Contains(c) || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator ? '_' : c);
        }
        var name=output.ToString().TrimEnd(' ','.');
        if(string.IsNullOrWhiteSpace(name))name="Proje";
        var device=name.Split('.')[0].TrimEnd(' ');
        var reserved=device.Equals("CON",StringComparison.OrdinalIgnoreCase) || device.Equals("PRN",StringComparison.OrdinalIgnoreCase)
            || device.Equals("AUX",StringComparison.OrdinalIgnoreCase) || device.Equals("NUL",StringComparison.OrdinalIgnoreCase)
            || (device.Length==4 && (device.StartsWith("COM",StringComparison.OrdinalIgnoreCase) || device.StartsWith("LPT",StringComparison.OrdinalIgnoreCase))
                && (device[3] is >= '1' and <= '9' or '¹' or '²' or '³'));
        if(reserved)name="_"+name;
        return name;
    }
    private static void EnsureWritableDestination(string path) => ProjectStorage.EnsureWritableDestination(path);
    internal Func<(bool Accepted,double? ToleranceMm)>? ConfirmDxfBulgeOptionsForVerification { get; set; }
    private async void ImportDxf()
    {
        if(BlockForDxfImport() || BlockForExport())return;
        var dlg = new OpenFileDialog { Filter="ASCII DXF (*.dxf;*.aama)|*.dxf;*.aama|Tüm dosyalar|*.*" };
        if(dlg.ShowDialog(this) != true) return;
        var optionsState=new DxfOptionsState();
        var options=CreateDxfOptionsWindow(optionsState);
        if(options.ShowDialog()!=true)return;
        var factor=optionsState.Factor;
        double? bulgeTolerance=null;
        if(!optionsState.UseBlockPlacements)
        {
            if(ConfirmDxfBulgeOptionsForVerification is { } confirm)
            {
                var choice=confirm();if(!choice.Accepted)return;bulgeTolerance=choice.ToleranceMm;
            }
            else
            {
                var bulgeOptions=new DxfBulgeOptionsWindow(this);
                if(bulgeOptions.ShowDialog()!=true)return;
                bulgeTolerance=bulgeOptions.SelectedToleranceMm;
            }
        }
        try
        {
            var imported=optionsState.UseBlockPlacements
                ? await StartInspectedDxfPreparationAsync(dlg.FileName,factor,optionsState.Encoding)
                : await StartDxfPreparationWithBulgeAsync(dlg.FileName,factor,optionsState.Encoding,bulgeTolerance);
            if(imported is null || windowClosed)return;
            preparedDxfFactor=factor;preparedDxfSource=Path.GetFileName(dlg.FileName);
            dxfNotice.Text="DXF hazır. Proje henüz değiştirilmedi; önizlemeyi açarak ölçüleri kontrol edin.";
            dxfReview.Visibility=Visibility.Visible;
        }
        catch(Exception ex)
        { if(!windowClosed) { dxfNotice.Text="DXF okunamadı; mevcut çalışma korundu: "+ex.Message; SetStatus(dxfNotice.Text); } }
    }
    internal sealed class DxfOptionsState
    {
        internal double Factor { get; set; }=1;
        internal DxfTextEncoding Encoding { get; set; }=DxfTextEncoding.Utf8;
        internal bool UseBlockPlacements { get; set; }
    }
    internal sealed class DxfLayerSelectionState
    {
        internal IReadOnlyList<string> SelectedLayers { get; set; }=Array.Empty<string>();
        internal double ComponentToleranceMm { get; set; }
    }
    private sealed record DxfLayerChoice(string Name,string Display);
    internal Func<DxfBlockPlacementInspection,DxfLayerSelectionState,bool>? ConfirmDxfLayersForVerification { get; set; }
    internal Action? AfterDxfInspectionForVerification { get; set; }

    internal Window CreateDxfLayerSelectionWindow(DxfBlockPlacementInspection inspection,DxfLayerSelectionState state)
    {
        ArgumentNullException.ThrowIfNull(inspection);ArgumentNullException.ThrowIfNull(state);
        var dialog=new Window { Owner=this,Title="DXF katmanlarını kontrol edin",Width=600,Height=550,MinWidth=440,MinHeight=340,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(dialog);
        var root=new DockPanel { Margin=new Thickness(16) };dialog.Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var cancel=AddButton(actions,"Vazgeç",()=>dialog.DialogResult=false);cancel.IsDefault=true;cancel.IsCancel=true;
        var layers=new ListBox { SelectionMode=SelectionMode.Multiple,MaxHeight=180,MinHeight=64,HorizontalContentAlignment=HorizontalAlignment.Stretch,DisplayMemberPath=nameof(DxfLayerChoice.Display) };
        VirtualizingPanel.SetIsVirtualizing(layers,true);VirtualizingPanel.SetVirtualizationMode(layers,VirtualizationMode.Recycling);ScrollViewer.SetCanContentScroll(layers,true);
        System.Windows.Automation.AutomationProperties.SetName(layers,"Onaylanacak DXF katmanları");
        System.Windows.Automation.AutomationProperties.SetHelpText(layers,"Başlangıçta hiçbir katman onaylı değildir. Dosyada kullanılan tüm katmanların anlamını kaynakta doğrulayarak onaylayın; kullanılan bir katman onaylanmazsa aktarım durur.");
        var choices=new List<DxfLayerChoice>(inspection.Layers.Length);
        foreach(var layer in inspection.Layers)
        {
            var details=layer.Hidden is null ? "görünürlük bilinmiyor" : layer.Hidden.Value ? "gizli veya donmuş" : "görünür";
            details+=layer.PlotEnabled is null ? ", çıktı durumu bilinmiyor" : layer.PlotEnabled.Value ? ", çıktıya açık" : ", çıktıya kapalı";
            details+=", çizgi tipi: "+(layer.LineType is null ? "bilinmiyor" : SafePieceName(layer.LineType));
            choices.Add(new(layer.Name,SafePieceName(layer.Name)+" — "+details));
        }
        layers.ItemsSource=choices;
        var tolerance=new TextBox { Text="0",MaxLength=32,Margin=new Thickness(0,0,0,4) };
        System.Windows.Automation.AutomationProperties.SetName(tolerance,"DXF koordinat toleransı mm");
        // Keep validation feedback adjacent to the actions. The input fields and
        // support notes may extend beyond the scroll viewport on small screens.
        var error=new TextBlock { TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,4,0,0),Visibility=Visibility.Collapsed };
        System.Windows.Automation.AutomationProperties.SetName(error,"DXF katman onayı hata bildirimi");
        System.Windows.Automation.AutomationProperties.SetAutomationId(error,"DxfLayerValidationError");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(error,System.Windows.Automation.AutomationLiveSetting.Assertive);
        DockPanel.SetDock(error,Dock.Bottom);root.Children.Add(error);
        void ClearError()
        {
            error.Text="";error.Visibility=Visibility.Collapsed;
            System.Windows.Automation.AutomationProperties.SetName(error,"DXF katman onayı hata bildirimi");
        }
        void ShowError(string message)
        {
            error.Text=message;error.Visibility=Visibility.Visible;
            System.Windows.Automation.AutomationProperties.SetName(error,"DXF katman onayı hata bildirimi: "+message);
            if(System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(error) is { } peer)
                peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        tolerance.TextChanged+=(_,_)=>ClearError();
        layers.SelectionChanged+=(_,_)=>ClearError();
        AddButton(actions,"Onayla ve okumayı başlat",()=>
        {
            if(!MeasurementInput.TryParseMillimeters(tolerance.Text,1,out var value) || value<0)
            { ShowError("0 ile 1 mm arasında geçerli bir tolerans girin.");return; }
            var selected=layers.SelectedItems.Cast<DxfLayerChoice>().Select(item=>item.Name).ToArray();
            if(selected.Length==0) { ShowError("Dosyada kullanılan katmanların anlamını kaynakta doğrulayın ve onaylayın.");return; }
            state.SelectedLayers=selected;state.ComponentToleranceMm=value;dialog.DialogResult=true;
        });
        var fixedNotice=new TextBlock { Text="Kodlama uyarılarını ve destek sınırlarını aşağıda okuyun; devam etmeden önce kullanılan tüm katmanları doğrulayın.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6) };
        DockPanel.SetDock(fixedNotice,Dock.Top);root.Children.Add(fixedNotice);
        var body=new StackPanel();var scroller=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        System.Windows.Automation.AutomationProperties.SetName(scroller,"DXF katman seçimi ve uyarılar");root.Children.Add(scroller);
        body.Children.Add(new TextBlock { Text="Kodlama ve destek sınırları",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,3) });
        foreach(var warning in inspection.Warnings.Concat(inspection.Limitations))
            body.Children.Add(new TextBlock { Text="• "+warning,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,4) });
        var sourceCounts=inspection.SupplementalGeometry.SourceCounts;
        if(!sourceCounts.IsEmpty)
        {
            var inventory="Kaynak kayıtları (parça veya yerleştirme sayısı değildir; çoklu çizgiler düz de olabilir):\n"+
                string.Join("\n",sourceCounts.Select(c=>$"{c.Section} · {c.EntityType}: {c.Count}"));
            var inventoryText=new TextBlock { Text=inventory,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,8) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(inventoryText,"DxfSourceInventory");
            body.Children.Add(inventoryText);
        }
        body.Children.Add(new TextBlock { Text="Dosyada kullanılan tüm katmanların anlamını kaynakta doğrulayın ve onaylayın. Kullanılan bir katman onaylanmazsa aktarım durur; seçim filtreleme yapmaz. Hiçbir katman önceden onaylı değildir. Renkler projede korunmaz ve kesim/dikiş anlamı otomatik belirlenmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,8) });
        body.Children.Add(layers);
        body.Children.Add(new TextBlock { Text="Koordinat toleransı (mm, her eksen için): 0 tam eşleşme ister ve geçerli ondalık ölçüleri reddedebilir. Üretim toleransı doğrulanmamıştır; izin verilen farkı dikkatle belirleyin (0–1 mm).",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,4) });
        body.Children.Add(tolerance);
        return dialog;
    }
    internal Window CreateDxfOptionsWindow(DxfOptionsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var options=new Window { Owner=this,Title="DXF içe aktarma seçenekleri",Width=520,Height=380,MinWidth=440,MinHeight=340,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(options);var root=new DockPanel { Margin=new Thickness(18) };options.Content=root;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var cancel=AddButton(actions,"Vazgeç",()=>options.DialogResult=false);cancel.IsCancel=true;cancel.IsDefault=true;
        ComboBox units=null!,encodings=null!,methods=null!;
        AddButton(actions,"DXF okumayı başlat",()=> { if(units.SelectedIndex is <0 or >2 || encodings.SelectedIndex is <0 or >2 || methods.SelectedIndex is <0 or >1)throw new InvalidOperationException("Birim, kodlama ve okuma yöntemini seçin.");state.Factor=new[] { 1.0,10.0,25.4 }[units.SelectedIndex];state.Encoding=encodings.SelectedIndex switch { 1 => DxfTextEncoding.Windows1254, 2 => DxfTextEncoding.Windows1252, _ => DxfTextEncoding.Utf8 };state.UseBlockPlacements=methods.SelectedIndex==1;options.DialogResult=true; });
        var content=new StackPanel();
        var scroll=new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        KeyboardNavigation.SetIsTabStop(scroll,true);System.Windows.Automation.AutomationProperties.SetName(scroll,"DXF ölçü ve kodlama seçenekleri");root.Children.Add(scroll);
        content.Children.Add(new TextBlock { Text="Dosyadaki 1 birimin karşılığını seçin. Birim başlığı otomatik ölçeklendirilmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        units=new ComboBox { ItemsSource=new[] { "Milimetre · 1 mm", "Santimetre · 10 mm", "İnç · 25,4 mm" },SelectedIndex=0,Padding=new Thickness(8),Margin=new Thickness(0,0,0,10) };
        System.Windows.Automation.AutomationProperties.SetName(units,"DXF dosya birimi");content.Children.Add(units);
        content.Children.Add(new TextBlock { Text="Metin kodlaması",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4) });
        encodings=new ComboBox { ItemsSource=new[] { "UTF-8", "Türkçe Windows-1254", "Batı Avrupa Windows-1252" },SelectedIndex=0,Padding=new Thickness(8),Margin=new Thickness(0,0,0,6) };
        System.Windows.Automation.AutomationProperties.SetName(encodings,"DXF metin kodlaması");content.Children.Add(encodings);
        content.Children.Add(new TextBlock { Text="Okuma yöntemi",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,4) });
        methods=new ComboBox { ItemsSource=new[] { "Standart aktarım (blok yerleşimleri uygulanmaz)", "Kontrollü aktarım (sınırlı destek)" },SelectedIndex=0,Padding=new Thickness(8),Margin=new Thickness(0,0,0,6) };
        System.Windows.Automation.AutomationProperties.SetName(methods,"DXF okuma yöntemi");content.Children.Add(methods);
        content.Children.Add(new TextBlock { Text="Kontrollü aktarım, model alanındaki çizgileri ve varsa blok yerleşimlerini okur. Sınırlı 2B düz ve çoklu çizgiler desteklenir; blok dönüşleri seçilen toleransla doğrulanır. Bulge yaylarını yaklaştırma yalnız standart aktarım için ayrı onayla seçilebilir; kontrollü aktarımda desteklenmez. Eğriler, 3B ve doğrulanamayan veriler açıklamayla durdurulur.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        content.Children.Add(new TextBlock { Text="Kodlama otomatik tahmin edilmez. Yanlış seçim harfleri değiştirebilir; kaynak dosyanın kodlamasını kontrol edin. Okuma kaynak dosyayı değiştirmez; proje ancak önizlemede onaylanırsa değiştirilir.",TextWrapping=TextWrapping.Wrap });
        return options;
    }
    internal Window CreateDxfSourceHistoryWindow()
    {
        var dialog=new Window { Owner=this,Title="DXF kaynak ve aktarım notları",Width=680,Height=520,MinWidth=440,MinHeight=320,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(dialog);
        var root=new DockPanel { Margin=new Thickness(16) };dialog.Content=root;
        var close=new Button { Content="Kapat",IsCancel=true,Padding=new Thickness(12,7,12,7),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };
        close.Click+=(_,_)=>dialog.Close();DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
        var history=Document.ImportProvenance;
        var text=history is null ? "Bu projede DXF kaynak geçmişi yok veya bilinmiyor. Uyarı bulunmadığı anlamına gelmez."
            : "Bu bilgiler içe aktarma anına aittir. Kaynak dosya projeye gömülmez; mevcut dosya, güncel geometri ve üretim uygunluğu doğrulanmaz. Parçalar sonradan değiştirilmiş veya silinmiş olabilir. Kodlama, yöntem, birim başlığı, katman seçimi ve geometri özeti bu kayıtta saklanmaz.\n\n"+
              "Kaynak dosya adı: "+SafePieceName(history.SourceFileName)+"\nOkunan baytların SHA-256 özeti: "+history.SourceSha256+
              "\nOkunan boyut: "+history.SourceByteLength+" bayt\nSeçilen ölçek: 1 birim = "+MeasurementInput.Format(history.MillimetersPerUnit)+" mm\n"+
              "Aktarımdaki parça kimliği sayısı: "+history.PieceIds.Count+"\n\nAktarım uyarıları ("+history.Warnings.Count+"):\n"+
              (history.Warnings.Count==0 ? "Kaydedilmiş uyarı yok; aktarımın eksiksiz olduğu anlamına gelmez." : string.Join("\n",history.Warnings.Select(w=>"• "+SafePieceName(w))));
        var details=new TextBox { Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(details,"DXF kaynak geçmişi ve aktarım uyarıları");root.Children.Add(details);
        return dialog;
    }

    private async void ReviewPreparedDxf() => await ReviewPreparedDxfAsync();
    internal async Task ReviewPreparedDxfAsync()
    {
        var imported=preparedDxf;
        if(imported is null || DxfImportRunning || dxfReviewActive)return;
        dxfReviewActive=true;
        dxfReview.Visibility=Visibility.Collapsed;
        var applied=false;
        ProjectDocument? importSnapshot = null;
        bool PreparedImportMatches()
        {
            try { if (importSnapshot is not null && DocumentSession.ContentEquals(importSnapshot, imported.Project)) return true; }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException)
            { /* An unstable or malformed copy cannot be approved. */ }
            // Do not allow a later retry to re-baseline the mutated copy.
            dxfPreparationStamp = null;
            dxfReview.Visibility = Visibility.Collapsed;
            dxfNotice.Text = "Hazırlanan DXF kopyası değişti veya doğrulanamadı; aktarım uygulanmadı. Dosyayı yeniden içe aktarın.";
            if (!windowClosed) SetStatus(dxfNotice.Text);
            return false;
        }
        try
        {
            if(!IsPreparedDxfRetryCurrent(imported)) { dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text);return; }
            // Keep an independent baseline before any preview or modal callback.
            importSnapshot = imported.Project.Clone();
            if (!PreparedImportMatches()) return;
            var retryPlacements=false;
            var accepted=ConfirmDxfReviewForVerification is { } confirm ? confirm(imported) : BuildImportPreviewCapturedWithState(importSnapshot.Clone(),imported.Warnings,preparedDxfFactor,preparedDxfSource,imported.Units,imported.SourceSha256,imported.SourceByteLength,()=>retryPlacements=true,imported.SkippedRelevantInsertEntities).ShowDialog()==true;
            if (!PreparedImportMatches()) return;
            if(retryPlacements) { await RetryPreparedDxfPlacementsAsync(imported);return; }
            if(!accepted) { OfferDxfReviewRetry(imported,"DXF önizlemesi kapatıldı; proje değiştirilmedi.");return; }
            // Enforce the same restriction here, including non-visual callers:
            // confirming a preview cannot make skipped INSERT placements valid.
            if(imported.SkippedRelevantInsertEntities is not false)
            {
                OfferDxfReviewRetry(imported,imported.SkippedRelevantInsertEntities is true
                    ? "Blok yerleşimleri okunmadığından DXF projeye uygulanmadı. Yerleşimleri kontrollü okuyun veya kaynak uygulamadan desteklenen bir DXF alın."
                    : "Blok yerleşimlerinin aktarım durumu doğrulanamadığı için DXF projeye uygulanmadı. Dosyayı yeniden içe aktarın.");
                return;
            }
            if(!IsPreparedDxfRetryCurrent(imported)) { dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text);return; }
            IReadOnlyList<DxfGrainlineSelection> grainSelections = Array.Empty<DxfGrainlineSelection>();
            string? grainFingerprint = null;
            var hasGrainCandidates = imported.Project.Pieces.Any(p => p.Closed && p.Points.Count >= 3 && p.Grainline is null) &&
                imported.Project.Pieces.Any(p => !p.Closed && p.Points.Count == 2 && p.Points[0] != p.Points[1] && p.Grainline is null && p.Metadata is null);
            var grainReviewUnavailable = false;
            if (hasGrainCandidates && (ConfirmDxfReviewForVerification is null || ConfirmDxfGrainlineReviewForVerification is not null))
            {
                // The mapper also validates captured internal metadata, which
                // is deliberately not exposed to this assembly. Probe its
                // complete contract before offering this optional review.
                if (imported.SkippedRelevantInsertEntities is false && imported.ImportedBlockDefinitions is false &&
                    imported.ImportParameters is not null && imported.SourceSha256 is { Length:64 } && imported.SourceByteLength is >= 0)
                {
                    try { grainFingerprint = DxfTextileGrainlineMapping.CaptureFingerprint(imported); }
                    catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { grainReviewUnavailable = true; }
                }
                else grainReviewUnavailable = true;
            }
            if (grainFingerprint is not null)
            {
                IReadOnlyList<DxfGrainlineSelection>? chosen;
                if (ConfirmDxfGrainlineReviewForVerification is { } review) chosen = review(imported);
                else
                {
                    var window = new DxfGrainlineReviewWindow(this, imported with { Project = importSnapshot.Clone() });
                    chosen = window.ShowDialog() == true ? window.Selections : null;
                }
                if (!PreparedImportMatches()) return;
                if (chosen is null) { OfferDxfReviewRetry(imported,"İplik yönü incelemesi iptal edildi; DXF uygulanmadı ve mevcut çalışma korundu.");return; }
                grainSelections = chosen.ToArray();
                if (!PreparedImportMatches()) return;
                if (!IsPreparedDxfRetryCurrent(imported)) { dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text);return; }
            }
            bool saved,discard;
            if(DxfDiscardDecisionForVerification is { } decision) { var choice=decision();discard=choice.Accepted;saved=choice.Saved; }
            else discard=CanDiscard(out saved);
            if (!PreparedImportMatches()) return;
            if(!discard) { OfferDxfReviewRetry(imported,"DXF uygulanmadı; mevcut çalışma korundu.");return; }
            // Save or other asynchronous work during the discard dialog can change
            // the live document. Never reuse a stale decision to replace it.
            var allowSaved=preparedDxfSavedState || saved;
            if(!IsDxfPreparationCurrent(imported) && !(allowSaved && IsDxfPreparationCurrentAfterSave(imported))) { dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text);return; }
            preparedDxfSavedState=allowSaved;
            if(!await VerifyDxfSourceBeforeApplyAsync(imported,allowSaved)) { if(dxfSourceCheckCancelled)OfferDxfReviewRetry(imported,"DXF kaynak kontrolü iptal edildi; proje değiştirilmedi.");return; }
            if(!IsPreparedDxfRetryCurrent(imported)) { dxfNotice.Text="Çalışma alanı doğrulamadan sonra değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text);return; }
            if (!PreparedImportMatches()) return;
            // Persist only the verified import snapshot, never an absolute source
            // path. A missing/oversized history fails before replacing the document.
            var candidate = grainSelections.Count == 0
                ? importSnapshot.Clone()
                : DxfTextileGrainlineMapping.Apply(imported with { Project = importSnapshot.Clone() }, grainSelections,
                    grainFingerprint ?? throw new InvalidOperationException("İplik yönü inceleme kimliği eksik.")).Project;
            var historyWarnings = imported.Warnings.ToList();
            if (grainReviewUnavailable)
                historyWarnings.Add("İplik yönü eşlemesi için kaynak bilgileri doğrulanamadı; çizgiler ayrı parçalar olarak korundu. Çizgilerin anlamını kaynakta kontrol edin.");
            if (grainSelections.Count > 0)
                historyWarnings.Add("DXF çizgileri kullanıcı seçimiyle iplik yönüne eşlendi; eşleşmeler ve kaynak işaret denetim ayrıntıları projede saklanmaz. Tüm iplik yönleri kontrol bekliyor; kaynakla karşılaştırın.");
            var history=new DxfImportProvenance { SourceFileName=preparedDxfSource,
                SourceSha256=imported.SourceSha256 ?? "", SourceByteLength=imported.SourceByteLength ?? -1,
                MillimetersPerUnit=preparedDxfFactor, Warnings=historyWarnings, PieceIds=candidate.Pieces.Select(p=>p.Id).ToList() };
            history.Validate();candidate.ImportProvenance=history;Validate(candidate);
            if (!PreparedImportMatches()) return;
            LoadDocumentCore(candidate,true,()=> { applied=true;preparedDxf=null;dxfPreparationStamp=null;preparedDxfPath=null;preparedDxfSavedState=false; });
            dxfNotice.Text=$"DXF: {Document.Pieces.Count} parça · 1 birim = {preparedDxfFactor} mm · Kaynak kontrol anından sonra değişmiş olabilir; ölçüleri kontrol edin";
            if (grainSelections.Count > 0) dxfNotice.Text += " · İplik yönleri kontrol bekliyor; kaynakla karşılaştırın";
            else if (grainReviewUnavailable) dxfNotice.Text += " · İplik yönü eşlemesi doğrulanamadı; çizgiler ayrı parça olarak korundu";
            SetStatus(dxfNotice.Text);
        }
        catch(Exception ex)
        { if(!windowClosed) { if(applied) { _ = SaveRecoveryAsync(); dxfNotice.Text="DXF projeye yüklendi ancak görünüm yenilenemedi. Yeni projeyi kontrol edip farklı adla kaydedin; eski çalışma geri getirilmedi. Ayrıntı: "+ex.Message; SetStatus(dxfNotice.Text); } else if (PreparedImportMatches()) OfferDxfReviewRetry(imported,"DXF uygulanamadı; mevcut çalışma korundu: "+ex.Message); } }
        finally { dxfReviewActive=false; }
    }
    private bool IsPreparedDxfRetryCurrent(DxfImportResult imported) => IsDxfPreparationCurrent(imported)
        || (preparedDxfSavedState && IsDxfPreparationCurrentAfterSave(imported));
    private void OfferDxfReviewRetry(DxfImportResult imported,string message)
    {
        if(!windowClosed && IsPreparedDxfRetryCurrent(imported))
        { dxfReview.Visibility=Visibility.Visible;dxfNotice.Text=message+" Aynı kopyayı yeniden inceleyebilirsiniz; kaynak uygulama öncesi tekrar kontrol edilir."; }
        else dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Yeniden içe aktarın.";
        if(!windowClosed)SetStatus(dxfNotice.Text);
    }
    private static bool HasSkippedDxfInsert(IReadOnlyList<string> warnings) => warnings.Any(w=>
        w.StartsWith("Atlanan desteklenmeyen DXF verileri:",StringComparison.OrdinalIgnoreCase)
        && w.Split(':',2).Last().Split(',').Any(item=>item.Trim().Equals("INSERT",StringComparison.OrdinalIgnoreCase)));

    // The previous review remains available on a cancelled/failed retry only
    // while the same document stamp is still valid. Its source is rechecked
    // both before switching methods and before any later application.
    internal async Task RetryPreparedDxfPlacementsAsync(DxfImportResult imported)
    {
        if(imported.ImportParameters?.BulgeToleranceMm is not null)
        {
            if(!windowClosed) { dxfNotice.Text="Bulge yaklaştırması kontrollü blok aktarımıyla birlikte kullanılamaz. Yöntemi değiştirmek için DXF dosyasını yeniden açın; mevcut çalışma korundu.";SetStatus(dxfNotice.Text); }
            return;
        }
        if(windowClosed || DxfImportRunning || !IsPreparedDxfRetryCurrent(imported) ||
            imported.SkippedRelevantInsertEntities is not true || preparedDxfPath is null ||
            preparedDxfEncoding is not { } encoding || !Enum.IsDefined(encoding) ||
            imported.SourceSha256 is not { Length:64 } expectedHash || imported.SourceByteLength is not >=0 ||
            !double.IsFinite(preparedDxfFactor) || preparedDxfFactor<=0 || dxfPreparationStamp is null)
        {
            if(!windowClosed) { dxfNotice.Text="Önceki DXF hazırlığı geçerli değil; dosyayı yeniden açın. Mevcut çalışma korundu.";SetStatus(dxfNotice.Text); }
            return;
        }
        var oldStamp=dxfPreparationStamp;var oldPath=preparedDxfPath;var oldSaved=preparedDxfSavedState;
        var oldFactor=preparedDxfFactor;var oldSource=preparedDxfSource;
        if(!await VerifyDxfSourceBeforeApplyAsync(imported,oldSaved))
        {
            if(dxfSourceCheckCancelled)OfferDxfReviewRetry(imported,"Kaynak kontrolü iptal edildi; mevcut çalışma korundu.");
            return;
        }
        if(windowClosed || !IsPreparedDxfRetryCurrent(imported))return;
        string? failure=null;
        DxfImportResult? result=null;
        try { result=await StartInspectedDxfPreparationAsync(oldPath,oldFactor,encoding,expectedHash,imported.SourceByteLength); }
        catch(Exception ex) { failure=ex.Message; }
        if(windowClosed)return;
        if(result is not null)
        {
            preparedDxfFactor=oldFactor;preparedDxfSource=oldSource;preparedDxfEncoding=encoding;
            dxfNotice.Text="Yerleşimler kontrollü okundu. Proje henüz değiştirilmedi; yeni önizlemeyi inceleyin.";
            dxfReview.Visibility=Visibility.Visible;SetStatus(dxfNotice.Text);return;
        }
        if(DxfStampMatches(oldStamp,allowSavedState:oldSaved))
        {
            preparedDxf=imported;dxfPreparationStamp=oldStamp;preparedDxfPath=oldPath;
            preparedDxfSavedState=oldSaved;preparedDxfFactor=oldFactor;preparedDxfSource=oldSource;preparedDxfEncoding=encoding;
            OfferDxfReviewRetry(imported,failure is null ? "Kontrollü okuma tamamlanmadı; mevcut çalışma korundu." : "Kontrollü okuma başarısız; mevcut çalışma korundu: "+failure);
        }
        else { dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Dosyayı yeniden açın.";SetStatus(dxfNotice.Text); }
    }
    internal async Task<bool> VerifyDxfSourceBeforeApplyAsync(DxfImportResult imported,bool allowSavedState)
    {
        if(DxfImportRunning || preparedDxfPath is null || !ReferenceEquals(preparedDxf,imported))return false;
        dxfSourceCheckCancelled=false;
        var path=preparedDxfPath;var before=BeforeDxfSourceCheckForVerification;var after=AfterDxfSourceCheckForVerification;
        using var cancellation=new CancellationTokenSource();dxfImportCancellation=cancellation;DxfImportRunning=true;
        dxfCancel.Content="DXF kaynak kontrolünü iptal et";
        System.Windows.Automation.AutomationProperties.SetName(dxfCancel,"DXF kaynak kontrolünü iptal et");
        dxfCancel.IsEnabled=true;dxfCancel.Visibility=Visibility.Visible;dxfReview.Visibility=Visibility.Collapsed;
        if(dxfImportCommand is not null)dxfImportCommand.IsEnabled=false;if(exportCommand is not null)exportCommand.IsEnabled=false;if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=false;
        dxfNotice.Visibility=Visibility.Visible;dxfNotice.Text="DXF kaynak dosyası kontrol ediliyor; sonucu bekleyin veya iptal isteyin.";SetStatus(dxfNotice.Text);
        try
        {
            var check=await Task.Run(()=> { before?.Invoke();cancellation.Token.ThrowIfCancellationRequested();var result=DxfSourceSnapshotCheck.Check(path,imported,cancellation.Token);after?.Invoke();cancellation.Token.ThrowIfCancellationRequested();return result; });
            cancellation.Token.ThrowIfCancellationRequested();
            if(!IsDxfPreparationCurrent(imported) && !(allowSavedState && IsDxfPreparationCurrentAfterSave(imported)))
            { dxfNotice.Text="Çalışma alanı kontrol sırasında değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text);return false; }
            if(check.Status!=DxfSourceSnapshotCheck.Status.MatchesReadBytes)
            {
                var reason=check.Status switch {
                    DxfSourceSnapshotCheck.Status.Changed=>"Kaynak dosyanın okunan baytları önceki kopyadan farklı.",
                    DxfSourceSnapshotCheck.Status.Missing=>"Kaynak dosya veya klasör bulunamadı.",
                    DxfSourceSnapshotCheck.Status.TooLarge=>"Kaynak dosya karşılaştırma boyut sınırını aşıyor.",
                    DxfSourceSnapshotCheck.Status.Unstable=>"Kaynak dosya kontrol sırasında değişti.",
                    _=>"Kaynak dosya okunamadı." };
                dxfNotice.Text=reason+" Proje değiştirilmedi; dosyayı yeniden içe aktarın.";SetStatus(dxfNotice.Text);return false;
            }
            return true;
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        { dxfSourceCheckCancelled=true;if(!windowClosed) { dxfNotice.Text="DXF kaynak kontrolü iptal edildi; proje değiştirilmedi.";SetStatus(dxfNotice.Text); }return false; }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { if(!windowClosed) { dxfNotice.Text="DXF kaynağı doğrulanamadı; proje değiştirilmedi. Yeniden içe aktarın.";SetStatus(dxfNotice.Text); }return false; }
        finally
        {
            if(ReferenceEquals(dxfImportCancellation,cancellation))dxfImportCancellation=null;
            DxfImportRunning=false;dxfCancel.Visibility=Visibility.Collapsed;dxfCancel.IsEnabled=false;
            dxfCancel.Content="DXF okumayı iptal et";System.Windows.Automation.AutomationProperties.SetName(dxfCancel,"DXF okumayı iptal et");
            if(!windowClosed) { if(dxfImportCommand is not null)dxfImportCommand.IsEnabled=true;if(exportCommand is not null)exportCommand.IsEnabled=Document.Pieces.Count>0 && !ExportRunning;if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=Document.Pieces.Count>0 && !ExportRunning; }
        }
    }
    private bool BlockForDxfImport()
    {
        if(!DxfImportRunning)return false;
        SetStatus("DXF arka planda okunuyor. Açma, kaydetme veya kapatma için işlemin bitmesini bekleyin.");
        return true;
    }
    internal void RequestDxfCancellation()
    {
        if(!DxfImportRunning || dxfImportCancellation is null || dxfImportCancellation.IsCancellationRequested)return;
        dxfCancel.IsEnabled=false;
        dxfNotice.Text="DXF iptali istendi; işlemin durması bekleniyor.";
        SetStatus(dxfNotice.Text);
        dxfImportCancellation.Cancel();
    }
    private DxfPreparationStamp CaptureDxfPreparationStamp()
    {
        if(canvas.HasActiveDragPreview || canvas.LargeVertexValidationRunning || canvas.LargeCompletionRunning)
            throw new InvalidOperationException("Önce devam eden çizim veya sürükleme işlemini tamamlayın ya da iptal edin.");
        return new(Document,Document.Clone(),new Dictionary<Guid,PropertyDraft>(propertyDrafts),
            canvas.CapturePendingDrawing(),SelectedId,session.UndoCount,session.RedoCount,dirty,filePath,fileVersion);
    }
    private bool DxfStampMatches(DxfPreparationStamp stamp, bool allowSavedState=false)
    {
        if(windowClosed || !ReferenceEquals(Document,stamp.Original) || !DocumentSession.ContentEquals(Document,stamp.Snapshot) ||
           SelectedId!=stamp.Selection || session.UndoCount!=stamp.UndoCount || session.RedoCount!=stamp.RedoCount ||
           (!allowSavedState && (dirty!=stamp.Dirty || filePath!=stamp.FilePath || !Equals(fileVersion,stamp.FileVersion))) ||
           (allowSavedState && (dirty || filePath is null || fileVersion is null)) || canvas.HasActiveDragPreview ||
           canvas.LargeVertexValidationRunning || canvas.LargeCompletionRunning || propertyDrafts.Count!=stamp.Drafts.Count ||
           stamp.Drafts.Any(pair=>!propertyDrafts.TryGetValue(pair.Key,out var current) || current!=pair.Value))return false;
        var drawing=canvas.CapturePendingDrawing();
        return stamp.Drawing is null ? drawing is null : drawing is not null && drawing.Tool==stamp.Drawing.Tool && drawing.Points.SequenceEqual(stamp.Drawing.Points);
    }
    internal bool IsDxfPreparationCurrent(DxfImportResult result) => ReferenceEquals(preparedDxf,result) && dxfPreparationStamp is { } stamp && DxfStampMatches(stamp);
    internal bool IsDxfPreparationCurrentAfterSave(DxfImportResult result) => ReferenceEquals(preparedDxf,result) && dxfPreparationStamp is { } stamp && DxfStampMatches(stamp,allowSavedState:true);
    internal async Task<DxfImportResult?> StartInspectedDxfPreparationAsync(string path,double factor,DxfTextEncoding encoding,
        string? expectedSourceSha256=null,long? expectedSourceByteLength=null)
    {
        EnsureNoExport();
        if(DxfImportRunning)throw new InvalidOperationException("DXF okuma sürüyor; işlemin bitmesini bekleyin.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if ((expectedSourceSha256 is null) != (expectedSourceByteLength is null) ||
            (expectedSourceSha256 is not null && (expectedSourceSha256.Length!=64 ||
                !expectedSourceSha256.All(Uri.IsHexDigit) || expectedSourceByteLength is not >=0)))
            throw new ArgumentException("Beklenen DXF kaynak özeti veya boyutu geçersiz.");
        var sourcePath=Path.GetFullPath(path);var stamp=CaptureDxfPreparationStamp();
        var before=BeforeDxfPreparationForVerification;var after=AfterDxfPreparationForVerification;
        using var cancellation=new CancellationTokenSource();dxfImportCancellation=cancellation;
        preparedDxf=null;dxfPreparationStamp=null;preparedDxfPath=null;preparedDxfSavedState=false;dxfSourceCheckCancelled=false;DxfImportRunning=true;
        dxfReview.Visibility=Visibility.Collapsed;dxfNotice.Visibility=Visibility.Visible;
        dxfCancel.Content="DXF okumayı iptal et";System.Windows.Automation.AutomationProperties.SetName(dxfCancel,"DXF okumayı iptal et");
        dxfCancel.IsEnabled=true;dxfCancel.Visibility=Visibility.Visible;
        if(dxfImportCommand is not null)dxfImportCommand.IsEnabled=false;
        if(exportCommand is not null)exportCommand.IsEnabled=false;if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=false;
        dxfNotice.Text="DXF katmanları arka planda inceleniyor; mevcut proje değiştirilmez.";SetStatus(dxfNotice.Text);
        try
        {
            var inspection=await Task.Run(()=>
            {
                before?.Invoke();cancellation.Token.ThrowIfCancellationRequested();
                using var source=new FileStream(sourcePath,FileMode.Open,FileAccess.Read,FileShare.Read);
                var found=DxfFormat.InspectBlockPlacements(source,encoding,cancellation.Token);
                AfterDxfInspectionForVerification?.Invoke();cancellation.Token.ThrowIfCancellationRequested();return found;
            });
            cancellation.Token.ThrowIfCancellationRequested();
            if(expectedSourceSha256 is not null &&
                (!string.Equals(expectedSourceSha256,inspection.SourceSha256,StringComparison.OrdinalIgnoreCase) ||
                 expectedSourceByteLength!=inspection.SourceByteLength))
                throw new InvalidDataException("DXF kaynağı önceki önizlemeden sonra değişti; dosyayı yeniden açın.");
            if(!DxfStampMatches(stamp)) { if(!windowClosed) { dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text); }return null; }
            var selection=new DxfLayerSelectionState();
            bool accepted=ConfirmDxfLayersForVerification is { } confirm ? confirm(inspection,selection) : CreateDxfLayerSelectionWindow(inspection,selection).ShowDialog()==true;
            cancellation.Token.ThrowIfCancellationRequested();
            if(!accepted) { if(!windowClosed) { dxfNotice.Text="Katman seçimi iptal edildi; mevcut çalışma korundu.";SetStatus(dxfNotice.Text); }return null; }
            if(!DxfStampMatches(stamp)) { if(!windowClosed) { dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text); }return null; }
            if(selection.SelectedLayers.Count==0 || !double.IsFinite(selection.ComponentToleranceMm) || selection.ComponentToleranceMm<0 || selection.ComponentToleranceMm>1)
                throw new InvalidDataException("Katman seçimi veya 0–1 mm tolerans geçersiz.");
            dxfNotice.Text="Seçilen katmanlar doğrulanarak DXF okunuyor; mevcut proje değiştirilmez.";SetStatus(dxfNotice.Text);
            var selected=selection.SelectedLayers.ToArray();var tolerance=selection.ComponentToleranceMm;
            var result=await Task.Run(()=>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                using var source=new FileStream(sourcePath,FileMode.Open,FileAccess.Read,FileShare.Read);
                var imported=DxfFormat.ImportWithInspectedBlockPlacements(source,Path.GetFileNameWithoutExtension(sourcePath),factor,tolerance,selected,inspection,cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();Validate(imported.Project);after?.Invoke();cancellation.Token.ThrowIfCancellationRequested();return imported;
            });
            cancellation.Token.ThrowIfCancellationRequested();
            if(!DxfStampMatches(stamp)) { if(!windowClosed) { dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text); }return null; }
            preparedDxf=result;dxfPreparationStamp=stamp;preparedDxfPath=sourcePath;
            preparedDxfFactor=factor;preparedDxfSource=Path.GetFileName(sourcePath);preparedDxfEncoding=encoding;
            SetStatus("DXF okundu; ölçüleri ve aktarım uyarılarını inceleyin. Proje henüz değiştirilmedi.");return result;
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        { if(!windowClosed) { dxfNotice.Text="DXF okuma iptal edildi; mevcut çalışma korundu.";SetStatus(dxfNotice.Text); }return null; }
        finally
        {
            if(ReferenceEquals(dxfImportCancellation,cancellation))dxfImportCancellation=null;
            DxfImportRunning=false;
            if(!windowClosed) { dxfCancel.Visibility=Visibility.Collapsed;if(dxfImportCommand is not null)dxfImportCommand.IsEnabled=true;if(exportCommand is not null)exportCommand.IsEnabled=Document.Pieces.Count>0 && !ExportRunning;if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=Document.Pieces.Count>0 && !ExportRunning; }
        }
    }
    internal Task<DxfImportResult?> StartDxfPreparationAsync(string path,double factor,DxfTextEncoding encoding=DxfTextEncoding.Utf8)
        =>StartDxfPreparationWithBulgeAsync(path,factor,encoding,null);
    internal async Task<DxfImportResult?> StartDxfPreparationWithBulgeAsync(string path,double factor,DxfTextEncoding encoding,double? bulgeToleranceMm)
    {
        EnsureNoExport();
        if(DxfImportRunning)throw new InvalidOperationException("DXF okuma sürüyor; işlemin bitmesini bekleyin.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if(bulgeToleranceMm is double requested && (!double.IsFinite(requested) || requested<=0))throw new ArgumentOutOfRangeException(nameof(bulgeToleranceMm));
        var sourcePath=Path.GetFullPath(path);
        var stamp=CaptureDxfPreparationStamp();
        var before=BeforeDxfPreparationForVerification;
        var after=AfterDxfPreparationForVerification;
        using var cancellation=new CancellationTokenSource();
        dxfImportCancellation=cancellation;
        preparedDxf=null;dxfPreparationStamp=null;preparedDxfPath=null;preparedDxfSavedState=false;dxfSourceCheckCancelled=false;DxfImportRunning=true;
        dxfReview.Visibility=Visibility.Collapsed;
        dxfNotice.Visibility=Visibility.Visible;
        dxfCancel.Content="DXF okumayı iptal et";System.Windows.Automation.AutomationProperties.SetName(dxfCancel,"DXF okumayı iptal et");
        dxfCancel.IsEnabled=true;dxfCancel.Visibility=Visibility.Visible;
        dxfNotice.Text="DXF arka planda okunuyor; dosyanın açıldığı andaki içeriği kullanılacak.";
        if(dxfImportCommand is not null)dxfImportCommand.IsEnabled=false;
        if(exportCommand is not null)exportCommand.IsEnabled=false;if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=false;
        SetStatus("DXF arka planda açılıp okunuyor; dosyanın açıldığı andaki içeriği kullanılacak. İşlemin bitmesini bekleyin.");
        try
        {
            var result=await Task.Run(()=> { before?.Invoke(); cancellation.Token.ThrowIfCancellationRequested(); var imported=bulgeToleranceMm is double tolerance
                ? PrepareDxfBulgeImportCancellable(sourcePath,factor,encoding,tolerance,cancellation.Token)
                : PrepareDxfImportCancellable(sourcePath,factor,cancellation.Token,encoding); after?.Invoke(); return imported; });
            cancellation.Token.ThrowIfCancellationRequested();
            if(!DxfStampMatches(stamp))
            { if(!windowClosed) { dxfNotice.Text="Çalışma alanı değişti; DXF uygulanmadı. Yeniden içe aktarın.";SetStatus(dxfNotice.Text); } return null; }
            preparedDxf=result;dxfPreparationStamp=stamp;preparedDxfPath=sourcePath;
            preparedDxfFactor=factor;preparedDxfSource=Path.GetFileName(sourcePath);preparedDxfEncoding=encoding;
            SetStatus(result.Warnings.Count>0
                ? "DXF okundu; aktarım uyarıları var. Ölçüleri ve uyarıları inceleyin; proje henüz değiştirilmedi."
                : "DXF okundu; proje henüz değiştirilmedi. Ölçüleri inceleyin.");
            return result;
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        {
            if(!windowClosed) { dxfNotice.Text="DXF okuma iptal edildi; mevcut çalışma korundu.";SetStatus(dxfNotice.Text); }
            return null;
        }
        finally
        {
            dxfImportCancellation=null;
            DxfImportRunning=false;
            if(!windowClosed) { dxfCancel.Visibility=Visibility.Collapsed; if(dxfImportCommand is not null)dxfImportCommand.IsEnabled=true; if(exportCommand is not null)exportCommand.IsEnabled=Document.Pieces.Count>0 && !ExportRunning;if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=Document.Pieces.Count>0 && !ExportRunning; }
        }
    }
    // Prepare and validate before the confirmation dialog or any document replacement.
    private DxfImportResult PrepareDxfImport(string path,double factor)
        => PrepareDxfImportCancellable(path,factor,CancellationToken.None);
    private DxfImportResult PrepareDxfImportCancellable(string path,double factor,CancellationToken cancellationToken,DxfTextEncoding encoding=DxfTextEncoding.Utf8)
    {
        var imported = DxfFormat.Import(path,factor,encoding,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(imported.Project);
        cancellationToken.ThrowIfCancellationRequested();
        if(imported.Project.Pieces.Count == 0) throw new InvalidDataException("Desteklenen geometri bulunamadı; mevcut proje korunuyor.");
        return imported;
    }
    private DxfImportResult PrepareDxfBulgeImportCancellable(string path,double factor,DxfTextEncoding encoding,double toleranceMm,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var imported=DxfFormat.ImportWithBulgeApproximation(input,Path.GetFileNameWithoutExtension(path),factor,encoding,toleranceMm,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();Validate(imported.Project);cancellationToken.ThrowIfCancellationRequested();
        if(imported.Project.Pieces.Count==0)throw new InvalidDataException("Desteklenen geometri bulunamadı; mevcut proje korunuyor.");
        return imported;
    }
    private Window BuildImportPreview(ProjectDocument incoming,IReadOnlyList<string> warnings,double factor,string sourceName,DxfUnitInfo? units)
        => BuildImportPreviewCaptured(incoming,warnings,factor,sourceName,units,null,null);

    private Window BuildImportPreviewCaptured(ProjectDocument incoming,IReadOnlyList<string> warnings,double factor,string sourceName,DxfUnitInfo? units,string? sourceSha256,long? sourceByteLength,Action? retryPlacements=null)
        => BuildImportPreviewCapturedWithState(incoming,warnings,factor,sourceName,units,sourceSha256,sourceByteLength,retryPlacements,HasSkippedDxfInsert(warnings));

    private Window BuildImportPreviewCapturedWithState(ProjectDocument incoming,IReadOnlyList<string> warnings,double factor,string sourceName,DxfUnitInfo? units,string? sourceSha256,long? sourceByteLength,Action? retryPlacements,bool? skippedInsertEntities)
    {
        var bounds=Geometry.Bounds(incoming.Pieces.SelectMany(p=>p.Points).ToList());
        var preview=new Window { Owner=this,Title="İçe aktarmadan önce kontrol edin",Width=650,Height=460,MinWidth=500,MinHeight=380,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(preview);
        var panel=new DockPanel { Margin=new Thickness(16) };preview.Content=panel;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);
        var loss=warnings.Any(w=>w.Contains("Atlanan",StringComparison.OrdinalIgnoreCase)
            || w.StartsWith("BLOCK tanım geometrisi",StringComparison.OrdinalIgnoreCase)
            || w.Contains("meta verileri aktarılmaz",StringComparison.OrdinalIgnoreCase));
        var blockDefinitions=warnings.Any(w=>w.StartsWith("BLOCK tanım geometrisi",StringComparison.OrdinalIgnoreCase));
        var insertSkipped=skippedInsertEntities is true;
        var insertStatusUnknown=skippedInsertEntities is null;
        var ambiguousLines=warnings.Any(w=>w.StartsWith("Belirsiz LINE bağlantıları:",StringComparison.Ordinal));
        var layerVisibilityUnknown=warnings.Any(w=>w.StartsWith("DXF katman görünürlüğü bilinmiyor:",StringComparison.Ordinal));
        var nonPlotLayerInfo=warnings.Any(w=>w.StartsWith("DXF plotlanmayan katman bilgisi aktarılmaz",StringComparison.Ordinal));
        var lineTypeUnknown=warnings.Any(w=>w.StartsWith("DXF çizgi tipi mirası doğrulanamadı",StringComparison.Ordinal));
        var colorLoss=warnings.Any(w=>w.StartsWith("DXF renk bilgileri projede korunmaz",StringComparison.Ordinal)
            || w.StartsWith("DXF renkleri projede korunmaz",StringComparison.Ordinal));
        var windows1254=warnings.Any(w=>w.StartsWith("DXF metni kullanıcının seçtiği Windows-1254",StringComparison.Ordinal));
        var windows1252=warnings.Any(w=>w.StartsWith("DXF metni kullanıcının seçtiği Windows-1252",StringComparison.Ordinal));
        var encodingHeaderWarning=warnings.Any(w=>w.StartsWith("DXF sürüm veya kod sayfası başlığı",StringComparison.Ordinal)
            || w.StartsWith("DXF sürüm başlığı",StringComparison.Ordinal) || w.StartsWith("DXF kod sayfası başlığı",StringComparison.Ordinal));
        var unitMismatch=units is { IsReliable:true, MillimetersPerUnit: double declared } && Math.Abs(declared-factor)>Math.Max(1e-12,Math.Abs(declared)*1e-12);
        var unitUnknown=units is not { IsReliable:true, MillimetersPerUnit: not null };
        var cancel=AddButton(actions,"Vazgeç",()=>preview.DialogResult=false);cancel.IsCancel=true;cancel.IsDefault=true;
        if(insertSkipped && retryPlacements is not null)
            AddButton(actions,"Yerleşimleri kontrollü oku…",()=> { retryPlacements();preview.DialogResult=false; });
        var apply=AddButton(actions,"İçe aktar ve projeyi değiştir",()=>preview.DialogResult=true);
        if(insertSkipped || insertStatusUnknown)
        {
            apply.IsEnabled=false;
            apply.ToolTip=insertSkipped ? "Blok yerleşimleri okunmadığı için önce kontrollü okumayı deneyin." : "Blok yerleşimlerinin aktarım durumu doğrulanamadı; dosyayı yeniden içe aktarın.";
        }
        var riskNotices=new StackPanel();
        if(ambiguousLines)
        {
            var lineNotice=new TextBlock { Text="Belirsiz LINE bağlantıları birleştirilmedi; ilgili çizgiler ayrı parçalar olarak aktarıldı. Konturları ve bağlantıları kontrol edin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6) };
            riskNotices.Children.Add(lineNotice);
        }
        if(loss)
        {
            var skipped=warnings.FirstOrDefault(w=>w.StartsWith("Atlanan desteklenmeyen DXF verileri:",StringComparison.OrdinalIgnoreCase));
            var firstSkipped=skipped?.Split(':',2).Last().Split(',')[0].Trim();
            if(firstSkipped?.Length>48) firstSkipped=firstSkipped[..48]+"…";
            var example=string.IsNullOrWhiteSpace(firstSkipped)?"":$" (ör. {firstSkipped})";
            var excluded=new List<string>();
            if(warnings.Any(w=>w.Contains("görünmez DXF nesnesi",StringComparison.OrdinalIgnoreCase)))excluded.Add("görünmez nesneler");
            if(warnings.Any(w=>w.Contains("negatif renk kodlu kapalı DXF nesnesi",StringComparison.OrdinalIgnoreCase)))excluded.Add("negatif renk kodlu kapalı nesneler");
            if(warnings.Any(w=>w.Contains("kapalı veya donmuş DXF katmanı",StringComparison.OrdinalIgnoreCase)))excluded.Add("kapalı veya donmuş katmanlardaki nesneler");
            if(warnings.Any(w=>w.Contains("kağıt alanı",StringComparison.OrdinalIgnoreCase)))excluded.Add("kâğıt alanı verileri");
            if(warnings.Any(w=>w.Contains("model dışı veya belirsiz yerleşim",StringComparison.OrdinalIgnoreCase)))excluded.Add("model dışı veya belirsiz yerleşimler");
            var noticeText=excluded.Count==0 ? $"Dikkat: Bazı DXF verileri{example} aktarılmıyor. Ayrıntılar aktarım notlarında."
                : $"Dikkat: {string.Join(" / ",excluded)} aktarılmıyor. Diğer ayrıntılar aktarım notlarında.";
            var notice=new TextBlock { Text=noticeText,TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6) };
            riskNotices.Children.Add(notice);
        }
        if(layerVisibilityUnknown || nonPlotLayerInfo || lineTypeUnknown || colorLoss)
        {
            var message=(layerVisibilityUnknown ? "Katman görünürlüğü bilinmiyor; görünmeyen çizimler aktarılmış olabilir. " : "")+
                (nonPlotLayerInfo ? "Plotlanmayan katman bilgisi aktarılmıyor; çıktı kapsamını kontrol edin. " : "")+
                (lineTypeUnknown && colorLoss ? "Çizgi tipi doğrulanamadı; renk korunmaz. Kaynağı kontrol edin."
                    : lineTypeUnknown ? "Çizgi tipi doğrulanamadı; düz gösterilen çizgileri kaynakta kontrol edin."
                    : colorLoss ? "Renkler projede korunmaz; kesim/dikiş anlamlarını kaynakta kontrol edin." : "");
            var notice=new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6) };
            riskNotices.Children.Add(notice);
        }
        if(windows1254 || windows1252 || encodingHeaderWarning)
        {
            var encodingText=(windows1254 ? "Windows-1254 seçildi; yanlış kodlama harfleri değiştirebilir. " : windows1252 ? "Windows-1252 seçildi; yanlış kodlama harfleri değiştirebilir. " : "")+
                (encodingHeaderWarning ? "DXF sürüm veya kod sayfası başlığıyla seçimin uyumu doğrulanamadı. " : "")+"Kaynak dosyanın kodlamasını doğrulayın.";
            var encodingNotice=new TextBlock { Text=encodingText,TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6) };
            riskNotices.Children.Add(encodingNotice);
        }
        // Several notices, or one unusually long combined notice, can otherwise
        // consume the entire compact window and hide the piece review.
        var riskTextLength=riskNotices.Children.OfType<TextBlock>().Sum(item=>item.Text.Length);
        var compactRisk=blockDefinitions || insertSkipped || riskNotices.Children.Count>=3 || riskTextLength>170;
        var skippedPolylineWarning=warnings.FirstOrDefault(w=>w.StartsWith("Atlanan çoklu çizgi:",StringComparison.Ordinal));
        if(skippedPolylineWarning is not null)
        {
            var notice=new TextBlock { Text=skippedPolylineWarning,TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Danger,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4) };
            AutomationProperties.SetName(notice,notice.Text);
            DockPanel.SetDock(notice,Dock.Top);panel.Children.Add(notice);
        }
        if(insertStatusUnknown)
        {
            var unknownNotice=new TextBlock { Text="Blok yerleşimlerinin aktarım durumu doğrulanamadı; proje değiştirilemez. Dosyayı yeniden içe aktarın.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4) };
            System.Windows.Automation.AutomationProperties.SetName(unknownNotice,unknownNotice.Text);
            DockPanel.SetDock(unknownNotice,Dock.Top);panel.Children.Add(unknownNotice);
        }
        if(blockDefinitions || insertSkipped)
        {
            var blockMessage=blockDefinitions && insertSkipped
                ? "Blok yerleşimleri (INSERT) uygulanmadı; bu görünüm kaynak yerleşimini yansıtmaz. Aktarmak için yerleşimleri kontrollü okuyun."
                : insertSkipped
                    ? "Blok yerleşimleri (INSERT) aktarılmadı; önizleme eksik olabilir. Aktarmak için yerleşimleri kontrollü okuyun."
                    : "Blok tanımları doğrudan alındı; bu görünüm kaynak yerleşimini doğrulamaz. Ölçüleri kontrol edin.";
            var blockNotice=new TextBlock { Text=blockMessage,TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4) };
            System.Windows.Automation.AutomationProperties.SetName(blockNotice,blockMessage);
            DockPanel.SetDock(blockNotice,Dock.Top);panel.Children.Add(blockNotice);
        }
        if(unitMismatch || unitUnknown)
        {
            var message=unitMismatch
                ? $"Birim uyuşmazlığı: başlık {units!.Description}; seçim {MeasurementInput.Format(factor)} mm/birim."
                : $"Birim doğrulanamadı: {units?.Description ?? "başlık yok"}; seçim {MeasurementInput.Format(factor)} mm/birim.";
            var unitNotice=new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4) };
            AutomationProperties.SetName(unitNotice,unitNotice.Text);
            DockPanel.SetDock(unitNotice,Dock.Top);panel.Children.Add(unitNotice);
        }
        if(compactRisk)
        {
            var summary=new TextBlock { Text=colorLoss ? "Renkler korunmaz; diğer aktarım uyarılarını aşağıda inceleyin." : "Aktarım uyarıları var; tümünü aşağıda kaydırarak inceleyin.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,3) };
            DockPanel.SetDock(summary,Dock.Top);panel.Children.Add(summary);
            var riskScroller=new ScrollViewer { Content=riskNotices,MaxHeight=unitMismatch || unitUnknown ? 20 : 36,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true,Margin=new Thickness(0,0,0,4) };
            KeyboardNavigation.SetIsTabStop(riskScroller,true);
            System.Windows.Automation.AutomationProperties.SetName(riskScroller,"DXF aktarım uyarıları");
            System.Windows.Automation.AutomationProperties.SetHelpText(riskScroller,"Tüm uyarıları okumak için bu alanı yön tuşlarıyla kaydırın. Uyarıların ayrıntıları aktarım notlarında da bulunur.");
            DockPanel.SetDock(riskScroller,Dock.Top);panel.Children.Add(riskScroller);
        }
        else if(riskNotices.Children.Count>0)
        {
            DockPanel.SetDock(riskNotices,Dock.Top);panel.Children.Add(riskNotices);
        }
        var replacement=new TextBlock { Text="İçe aktarma mevcut projenin yerini alacak; Vazgeç korur. İncelenen kopya kaynakla artık eşleşmeyebilir.",TextWrapping=TextWrapping.Wrap,Foreground=StudioTheme.Warning,Margin=new Thickness(0,0,0,6) };
        DockPanel.SetDock(replacement,Dock.Top);panel.Children.Add(replacement);
        var body=new StackPanel();
        var notesViewer=new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true };
        KeyboardNavigation.SetIsTabStop(notesViewer,true);
        System.Windows.Automation.AutomationProperties.SetName(notesViewer,"DXF aktarım bilgileri ve notları");
        System.Windows.Automation.AutomationProperties.SetHelpText(notesViewer,"Dosya ölçülerini ve tüm aktarım notlarını okumak için yön tuşlarıyla kaydırın.");
        var reviewTabs=new TabControl();panel.Children.Add(reviewTabs);
        reviewTabs.Items.Add(new TabItem { Header="Aktarım bilgileri",Content=notesViewer });
        var sourceDetail=new TextBox { Text=SafePieceName(sourceName),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=48,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(sourceDetail,"Tam kaynak DXF dosya adı");
        System.Windows.Automation.AutomationProperties.SetHelpText(sourceDetail,"Tam dosya adını seçip kopyalayabilir, uzun adı kaydırabilirsiniz. Görünmeyen karakterler Unicode koduyla gösterilir.");
        body.Children.Add(sourceDetail);
        var snapshotDetails=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=82,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,6,0,0),
            Text=sourceSha256 is not null && sourceSha256.Length==64 && sourceSha256.All(Uri.IsHexDigit) && sourceByteLength is >=0
                ? $"Daha önce okunan DXF kopyası: {sourceByteLength.Value} bayt\nSHA-256: {sourceSha256}\nKaynak dosya sonradan değişmiş olabilir; şu anki sürümle eşleşme kontrol edilmedi."
                : "Daha önce okunan DXF kopyasının bayt sayısı veya özeti yok. Kaynak dosya sonradan değişmiş olabilir; şu anki sürümle eşleşme kontrol edilmedi." };
        System.Windows.Automation.AutomationProperties.SetName(snapshotDetails,"Okunan DXF kopyasının özeti ve kaynak durumu");
        System.Windows.Automation.AutomationProperties.SetHelpText(snapshotDetails,"Okunmuş kopyanın bayt sayısını ve SHA-256 özetini seçip kopyalayabilirsiniz; güncel dosya durumu doğrulanmaz.");
        body.Children.Add(snapshotDetails);
        body.Children.Add(new TextBlock { Text=$"Seçilen metin kodlaması: {(windows1254 ? "Türkçe Windows-1254" : windows1252 ? "Batı Avrupa Windows-1252" : "UTF-8")}. Kodlama otomatik tahmin edilmez; kaynak dosya değiştirilmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0) });
        body.Children.Add(new TextBlock { Text=$"{incoming.Pieces.Count} parça · {incoming.Pieces.Sum(p=>p.Points.Count)} nokta\nToplam genişlik × yükseklik: {MeasurementInput.Format(bounds.Width)} × {MeasurementInput.Format(bounds.Height)} mm\nDosyada 1 birim = {MeasurementInput.Format(factor)} mm",Margin=new Thickness(0,12,0,12),TextWrapping=TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text=$"DXF birim başlığı: {units?.Description ?? "Bilinmiyor"}. Başlık ölçünün doğruluğunu garanti etmez; seçilen ölçek uygulanır. Yukarıdaki veya parça görünümündeki ölçüleri bilinen bir ölçüyle karşılaştırın.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) });
        body.Children.Add(new TextBlock { Text="Aktarım notları",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6) });
        if(warnings.Count==0)body.Children.Add(new TextBlock { Text="Desteklenen geometri için aktarım uyarısı yok.",TextWrapping=TextWrapping.Wrap });
        foreach(var warning in warnings)body.Children.Add(new TextBlock { Text="• "+warning,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,6) });
        var namesPanel=new DockPanel();
        var namesNotice=new TextBlock { Text=$"{incoming.Pieces.Count} parça. Adları kodlamanın doğruluğunu anlamak için inceleyin; bu liste doğruluğu garanti etmez.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,4) };
        DockPanel.SetDock(namesNotice,Dock.Top);namesPanel.Children.Add(namesNotice);
        var selectedName=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=40,MaxHeight=72,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,4,0,0) };
        System.Windows.Automation.AutomationProperties.SetName(selectedName,"Seçili içe alınacak parçanın tam adı ve kimliği");
        System.Windows.Automation.AutomationProperties.SetHelpText(selectedName,"Tam adı ve kimliği seçip kopyalayın. Görünmeyen karakterler Unicode koduyla gösterilir.");
        DockPanel.SetDock(selectedName,Dock.Bottom);namesPanel.Children.Add(selectedName);
        var showPiece=new Button { Content="Parçayı gör",IsEnabled=false,Padding=new Thickness(10,5,10,5),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,4,0,4) };
        System.Windows.Automation.AutomationProperties.SetName(showPiece,"Seçili içe alınacak parçayı gör");
        DockPanel.SetDock(showPiece,Dock.Bottom);namesPanel.Children.Add(showPiece);
        var nameList=new ListBox { ItemsSource=incoming.Pieces,HorizontalContentAlignment=HorizontalAlignment.Stretch };
        var nameTemplate=new FrameworkElementFactory(typeof(TextBlock));
        nameTemplate.SetBinding(TextBlock.TextProperty,new Binding { Converter=new DxfPreviewNameConverter() });
        nameTemplate.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
        nameList.ItemTemplate=new DataTemplate { VisualTree=nameTemplate };
        VirtualizingPanel.SetIsVirtualizing(nameList,true);VirtualizingPanel.SetVirtualizationMode(nameList,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(nameList,true);
        System.Windows.Automation.AutomationProperties.SetName(nameList,"İçe alınacak tüm parça adları");
        System.Windows.Automation.AutomationProperties.SetHelpText(nameList,"Parça seçerek aşağıdaki tam adı ve kimliği inceleyin. Bu adlar kaynak kodlamasına göre çözülmüştür.");
        var geometryPanel=new DockPanel();
        var geometrySummary=new TextBlock { Text="Parça seçin.",TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4) };
        System.Windows.Automation.AutomationProperties.SetName(geometrySummary,"Seçili DXF parçasının görünür ölçü özeti");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(geometrySummary,System.Windows.Automation.AutomationLiveSetting.Polite);
        DockPanel.SetDock(geometrySummary,Dock.Top);geometryPanel.Children.Add(geometrySummary);
        var geometryStatus=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=compactRisk ? 24 : 45,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            Text="Önce parça adları sekmesinden bir parça seçip Parçayı gör düğmesine basın." };
        // The shared TextBox style uses MinHeight=34 and seven-DIP vertical
        // padding. Fit an actual line of text into the 24-DIP compact row.
        if(compactRisk) { geometryStatus.MinHeight=24;geometryStatus.Padding=new Thickness(4,2,4,2); }
        System.Windows.Automation.AutomationProperties.SetName(geometryStatus,"İçe alınacak parça geometrisi ve ölçüleri");
        System.Windows.Automation.AutomationProperties.SetHelpText(geometryStatus,"Tam parça adını, kimliğini ve milimetre ölçülerini seçip kopyalayın. Görünüm fiziksel 1:1 onayı değildir.");
        DockPanel.SetDock(geometryStatus,Dock.Top);geometryPanel.Children.Add(geometryStatus);
        var piecePreview=new DxfPiecePreview { MinHeight=compactRisk ? 40 : 70 };geometryPanel.Children.Add(piecePreview);
        var geometryTab=new TabItem { Header="Parça görünümü",Content=geometryPanel };
        nameList.SelectionChanged+=(_,_)=>
        {
            selectedName.Text=nameList.SelectedItem is PatternPiece piece ? $"{SafePieceName(piece.Name)}\nKimlik: {piece.Id:D}" : "";
            showPiece.IsEnabled=nameList.SelectedItem is PatternPiece;
            piecePreview.Clear();geometrySummary.Text=piecePreview.VisibleSummary;geometryStatus.Text="Önce parça adları sekmesinden bir parça seçip Parçayı gör düğmesine basın.";
        };
        showPiece.Click+=(_,_)=>
        {
            if(nameList.SelectedItem is not PatternPiece piece)return;
            var description=piecePreview.ShowPiece(piece);
            geometrySummary.Text=piecePreview.VisibleSummary;
            geometryStatus.Text=$"{SafePieceName(piece.Name)}\n{description}";
            reviewTabs.SelectedItem=geometryTab;
        };
        namesPanel.Children.Add(nameList);
        reviewTabs.Items.Add(new TabItem { Header="İçe alınacak adlar",Content=namesPanel });
        reviewTabs.Items.Add(geometryTab);
        return preview;
    }
    internal string? OutputLossNotice()
    {
        var notes=new List<string>();
        if(Document.Pieces.Any(p=>p.Metadata is not null))
            notes.Add("Parça rolü, beden, malzeme ve adet çıktı dosyasına aktarılmaz; rol ve adet bu işlemde uygulanmaz.");
        if(Document.Pieces.Any(p=>p.Grainline is not null))
            notes.Add("SVG iplik yönünü görsel çizgi olarak içerir. Başka programlar bu çizgiyi kesim yolu sanabilir veya işaret bilgisini yok sayabilir; otomatik kesimde kullanmadan önce kaynak ve makineyle bağımsız doğrulayın. Fiziksel ölçü uyumu doğrulanmadı. İplik yönü içeren parçalar için DXF, HPGL ve makine simülasyonu bu sürümde desteklenmiyor.");
        if(Document.GradingRules.Count>0)
            notes.Add("Serileme kuralları çıktı dosyasına aktarılmaz; oluşturulmuş parçalar geometri olarak alınır.");
        if(Document.NestingSnapshot is not null)
            notes.Add("Pastal geçmişindeki kumaş ölçüleri, boşluk ve kenar payı ile kaynak–kopya eşleşmesi çıktı dosyasına aktarılmaz.");
        return notes.Count==0 ? null : string.Join("\n",notes);
    }
    private void EnsurePropertyDraftsApplied()
    {
        EnsureNoActiveDrag();
        EnsureNoLargeVertexValidation();
        if(canvas.HasPendingDrawing)
            throw new InvalidOperationException("Tamamlanmamış çizim henüz projeye ve çıktıya dahil değildir. Devam etmeden önce çizimi tamamlayın veya çalışma alanında Esc tuşuyla iptal edin.");
        if(propertyDrafts.Count>0)
            throw new InvalidOperationException($"{propertyDrafts.Count} parçanın uygulanmamış ad veya konum taslağı var. İlgili parçaları seçip ‘Konumu ve adı uygula’ düğmesini kullanın veya Kaydet ile tüm taslakları doğrulayıp uygulayın. Geçersiz değerleri düzeltin; istenmeyen taslakları ‘Alan taslaklarını temizle’ ile kaldırabilirsiniz.");
    }
    private void EnsureNoLargeVertexValidation()
    {
        if(canvas.LargeCompletionRunning)
            throw new InvalidOperationException("Büyük çizim doğrulanıyor ve henüz projeye veya kurtarma kaydına dahil değil. Sonucu bekleyin veya kalıp alanında Esc ile iptal edin.");
        if(canvas.LargeVertexValidationRunning)
            throw new InvalidOperationException("Büyük parçanın nokta değişikliği doğrulanıyor. Önce işlemin bitmesini bekleyin veya çalışma alanında Esc ile iptal edin; o sırada kayıtlı geometri gösterilir.");
    }
    private const string ActiveDragNotice="Sürükleme henüz projeye uygulanmadı. Önce fareyi bırakıp işlemi tamamlayın veya Esc ile iptal edin.";
    private bool BlockForActiveDrag()
    {
        if(canvas?.HasActiveDragPreview != true)return false;
        SetStatus(ActiveDragNotice);return true;
    }
    private void EnsureNoActiveDrag()
    {
        if(canvas?.HasActiveDragPreview == true)throw new InvalidOperationException(ActiveDragNotice);
    }
    private bool BlockForLargeVertexValidation()
    {
        if(canvas?.LargeCompletionRunning == true)
        { SetStatus("Büyük çizim doğrulanıyor ve henüz projeye veya kurtarma kaydına dahil değil. Sonucu bekleyin veya kalıp alanında Esc ile iptal edin.");return true; }
        if(canvas?.LargeVertexValidationRunning != true)return false;
        SetStatus("Nokta değişikliği doğrulanıyor ve henüz projeye veya kurtarma kaydına dahil değil. Sonucu bekleyin veya kalıp alanında Esc ile iptal edin.");
        return true;
    }
    private void OpenTextileTools()
    {
        if(BlockForDxfImport())return;
        EnsurePropertyDraftsApplied();
        new TextileToolsWindow(this).ShowDialog();
    }
    internal Func<string,bool>? ConfirmMachineLossForVerification { get; set; }
    internal Action<SimulationQueue>? OpenMachineCenterForVerification { get; set; }
    private void OpenMachineCenter()
    {
        if(BlockForDxfImport())return;
        EnsurePropertyDraftsApplied();
        if(machineQueue is null)
        {
            _=BeginMachineQueueLoadAsync();
            return;
        }
        // The confirmation dialog pumps the dispatcher. Tie approval to the
        // exact document and pending-work state for which the notice was made.
        var document=Document;var snapshot=document.Clone();var epoch=projectEpoch;
        var selection=SelectedId;var queue=machineQueue;
        var warning=OutputLossNotice();
        if(warning is not null)
        {
            var approved=ConfirmMachineLossForVerification is { } confirm
                ? confirm(warning)
                : MessageBox.Show(this,warning+"\n\nDevam edilsin mi?", "Makine dosyası bilgisi", MessageBoxButton.YesNo, MessageBoxImage.Information)==MessageBoxResult.Yes;
            if(!approved)return;
        }
        if(windowClosed || recoveryClosing || recoveryRestoreIncomplete || HasPendingCanvasOrPropertyWork ||
           projectEpoch!=epoch || !ReferenceEquals(Document,document) || !DocumentSession.ContentEquals(snapshot,Document) ||
           SelectedId!=selection || !ReferenceEquals(machineQueue,queue) || BlockForDxfImport())
        {
            SetStatus("Proje, seçim veya bekleyen çalışma onay sırasında değişti; makine simülatörü açılmadı. Güncel durumu kontrol edip yeniden deneyin.");
            return;
        }
        // Reopening must re-read the on-disk queue. A different process may have
        // changed a manifest while this modal window was open.
        try
        {
            if(OpenMachineCenterForVerification is { } open)open(queue);
            else new MachineCenterWindow(this,queue).ShowDialog();
        }
        finally
        {
            machineQueue=null;
            if(machineQueueCommand is not null)machineQueueCommand.Content="Makine simülatörü…";
            machineQueueMessage.Text="";machineQueueMessage.Visibility=Visibility.Collapsed;
        }
    }
    internal async Task BeginMachineQueueLoadAsync()
    {
        if(machineQueue is not null || MachineQueueLoading || windowClosed || recoveryClosing)return;
        MachineQueueLoading=true;
        if(machineQueueCommand is not null) { machineQueueCommand.Content="Simülatör yükleniyor…";machineQueueCommand.IsEnabled=false; }
        machineQueueMessage.Text="İş geçmişi yükleniyor. Çalışma alanını kullanmaya devam edebilirsiniz.";machineQueueMessage.Visibility=Visibility.Visible;
        SetStatus("Simülasyon iş geçmişi arka planda yükleniyor; çalışma alanını kullanmaya devam edebilirsiniz.");
        try
        {
            var factory=MachineQueueFactory;
            var loaded=await Task.Run(factory);
            if(windowClosed)return;
            machineQueue=loaded ?? throw new InvalidOperationException("Simülasyon kuyruğu oluşturulamadı.");
            if(machineQueueCommand is not null)machineQueueCommand.Content="Simülatörü aç";
            machineQueueMessage.Text="İş geçmişi hazır. Simülatörü aç düğmesine basın.";
            SetStatus("Simülasyon iş geçmişi hazır. Makine simülatörünü açmak için düğmeye yeniden basın.");
        }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            if(!windowClosed)
            {
                if(machineQueueCommand is not null)machineQueueCommand.Content="Simülatörü yeniden dene";
                machineQueueMessage.Text="İş geçmişi yüklenemedi. Yeniden deneyin.\nAyrıntı: "+ex.Message;
                SetStatus("Simülasyon iş geçmişi yüklenemedi. Yeniden denemek için simülatör düğmesine basın.");
            }
        }
        finally
        {
            MachineQueueLoading=false;
            if(!windowClosed && machineQueueCommand is not null)machineQueueCommand.IsEnabled=true;
        }
    }
    private void ExportGrainlineReview()
    {
        EnsureNoExport();EnsurePropertyDraftsApplied();
        if(HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce çizimi ve alan taslaklarını tamamlayın veya iptal edin.");
        var stamp=CaptureDxfPreparationStamp();
        var dialog=new SaveFileDialog { Title="Yalnız inceleme için yeni DXF dosyası",Filter="DXF (*.dxf)|*.dxf",
            FileName=SafeFileName(Document.Name)+"-isaret-inceleme.dxf",AddExtension=true,OverwritePrompt=false,InitialDirectory=AppPaths.PrepareProjectsDirectory() };
        if(dialog.ShowDialog(this)!=true)return;
        if(!DxfStampMatches(stamp) || HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Proje veya seçim değişti; inceleme dosyasını yeniden başlatın.");
        ExportDestination.ValidateExtension(dialog.FileName,1);EnsureWritableDestination(dialog.FileName);EnsureExportDoesNotReplaceOpenProject(dialog.FileName);
        var expected=ProjectStorage.CaptureVersion(dialog.FileName);
        if(expected.Sha256 is not null)throw new InvalidOperationException("Bu inceleme için mevcut bir dosya değiştirilemez; yeni bir dosya adı seçin.");
        _=StartDxfGrainlineReviewExportAsync(dialog.FileName,expected,stamp);
    }
    internal Task StartDxfGrainlineReviewExportAsync(string path,ProjectFileVersion expected)
    {
        EnsureNoExport();
        if(HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Önce çizimi ve alan taslaklarını tamamlayın veya iptal edin.");
        return StartDxfGrainlineReviewExportAsync(path,expected,CaptureDxfPreparationStamp());
    }
    private Task StartDxfGrainlineReviewExportAsync(string path,ProjectFileVersion expected,DxfPreparationStamp stamp)
    {
        EnsureNoExport();ArgumentNullException.ThrowIfNull(expected);
        ExportDestination.ValidateExtension(path,1);EnsureWritableDestination(path);EnsureExportDoesNotReplaceOpenProject(path);
        if(expected.Sha256 is not null)throw new InvalidOperationException("Bu inceleme için yeni bir dosya adı seçin.");
        if(!DxfStampMatches(stamp) || HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("Proje veya seçim değişti; incelemeyi yeniden başlatın.");
        ExportRunning=true;ExportError=null;ExportWasCancelled=false;
        var cancellation=new CancellationTokenSource();exportCancellation=cancellation;
        exportCancel.Content="DXF inceleme aktarımını iptal et";
        AutomationProperties.SetName(exportCancel,"DXF inceleme aktarımını iptal et");
        exportCancel.Visibility=Visibility.Visible;exportCancel.IsEnabled=true;
        if(exportCommand is not null)exportCommand.IsEnabled=false;if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=false;
        exportNotice.Foreground=StudioTheme.Muted;exportNotice.Text="DXF inceleme dosyası hazırlanıyor; iptal isteyebilirsiniz.";exportNotice.ToolTip=path;exportNotice.Visibility=Visibility.Visible;SetStatus(exportNotice.Text);
        return ExportTask=RunDxfGrainlineReviewExportAsync(path,expected,stamp,cancellation);
    }
    private async Task RunDxfGrainlineReviewExportAsync(string path,ProjectFileVersion expected,DxfPreparationStamp stamp,CancellationTokenSource cancellation)
    {
        var token=cancellation.Token;
        try
        {
            var before=BeforeGrainlineExportPreparationForVerification;
            var plan=await Task.Run(()=> { before?.Invoke();token.ThrowIfCancellationRequested();return DxfGrainlineReviewExport.Prepare(stamp.Snapshot,token); });
            token.ThrowIfCancellationRequested();
            if(!DxfStampMatches(stamp) || HasPendingCanvasOrPropertyWork || !plan.Matches(Document))
                throw new InvalidOperationException("Proje veya seçim hazırlık sırasında değişti; dosya yazılmadı.");
            var approved=ConfirmDxfGrainlineExportForVerification is { } confirm
                ? confirm(plan) : new DxfGrainlineExportReviewWindow(this,plan).ShowDialog()==true;
            token.ThrowIfCancellationRequested();
            if(!approved)
            {
                ExportWasCancelled=true;exportNotice.Foreground=StudioTheme.Warning;exportNotice.Text="DXF incelemesi iptal edildi; dosya yazılmadı.";SetStatus(exportNotice.Text);return;
            }
            if(!DxfStampMatches(stamp) || HasPendingCanvasOrPropertyWork || !plan.Matches(Document))
                throw new InvalidOperationException("Proje veya seçim inceleme sırasında değişti; dosya yazılmadı.");
            EnsureWritableDestination(path);EnsureExportDoesNotReplaceOpenProject(path);token.ThrowIfCancellationRequested();
            exportCancel.IsEnabled=false;exportNotice.Text="İnceleme dosyası kaydediliyor; sonucu bekleyin.";SetStatus(exportNotice.Text);
            var wasEnabled=IsEnabled;IsEnabled=false;
            try { await Task.Run(()=>DxfGrainlineReviewExport.Write(plan,path,expected,token)); }
            finally { if(!windowClosed)IsEnabled=wasEnabled; }
            exportNotice.Foreground=StudioTheme.Warning;exportNotice.Text="DXF inceleme dosyası yazıldı; işaretler kesim yolu olarak algılanabilir. Hedefte kontrol edin.";
            exportNotice.ToolTip=path;SetStatus(exportNotice.Text+" "+path);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested)
        { ExportWasCancelled=true;exportNotice.Foreground=StudioTheme.Warning;exportNotice.Text="DXF inceleme aktarımı iptal edildi; hedef dosya yazılmadı.";SetStatus(exportNotice.Text); }
        catch(ProjectConflictException)
        { ExportError="Hedef dosya değişti veya oluşturuldu; inceleme dosyası yazılmadı. Yeni bir ad seçin.";exportNotice.Foreground=StudioTheme.Danger;exportNotice.Text=ExportError;SetStatus(ExportError); }
        catch(Exception ex)
        { ExportError=ex.Message;exportNotice.Foreground=StudioTheme.Danger;exportNotice.Text="DXF inceleme aktarımı tamamlanamadı: "+ex.Message+" Hedefin durumunu kontrol edin.";SetStatus(exportNotice.Text); }
        finally
        {
            ExportRunning=false;if(ReferenceEquals(exportCancellation,cancellation))exportCancellation=null;cancellation.Dispose();
            exportCancel.IsEnabled=false;exportCancel.Visibility=Visibility.Collapsed;exportCancel.Content="HPGL aktarımını iptal et";
            AutomationProperties.SetName(exportCancel,"HPGL dışa aktarımını iptal et");
            if(exportCommand is not null)exportCommand.IsEnabled=Document.Pieces.Count>0 && !DxfImportRunning;
            if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=Document.Pieces.Count>0 && !DxfImportRunning;
        }
    }
    private void Export()
    {
        EnsureNoExport();
        EnsurePropertyDraftsApplied();
        var snapshot=Document.Clone();
        var warning=OutputLossNotice();
        if(warning is not null && MessageBox.Show(this,warning+"\n\nDevam edilsin mi?", "Dışa aktarım bilgisi", MessageBoxButton.YesNo, MessageBoxImage.Information)!=MessageBoxResult.Yes)return;
        if(!DocumentSession.ContentEquals(snapshot,Document)) throw new InvalidOperationException("Proje inceleme sırasında değişti; dışa aktarımı yeniden başlatın.");
        var dlg = new SaveFileDialog { Title=$"Tüm proje dışa aktarılacak · {Document.Pieces.Count} parça", Filter="DXF (*.dxf)|*.dxf|SVG (*.svg)|*.svg|HPGL / PLT (*.plt;*.hpgl)|*.plt;*.hpgl|PDF inceleme (*.pdf)|*.pdf", FileName=SafeFileName(Document.Name), AddExtension=true, InitialDirectory=AppPaths.PrepareProjectsDirectory() };
        if(dlg.ShowDialog(this)!=true)return;
        ExportDestination.ValidateExtension(dlg.FileName,dlg.FilterIndex);
        if(!DocumentSession.ContentEquals(snapshot,Document)) throw new InvalidOperationException("Proje dosya seçimi sırasında değişti; dışa aktarımı yeniden başlatın.");
        EnsureWritableDestination(dlg.FileName); Validate(snapshot);
        EnsureExportDoesNotReplaceOpenProject(dlg.FileName);
        var targetVersion=ProjectStorage.CaptureVersion(dlg.FileName);
        // The native dialog's overwrite confirmation precedes the version capture.
        // Confirm the captured version; noncooperating writers can still race the final replacement.
        if(targetVersion.Sha256 is not null && MessageBox.Show(this,
            "Seçilen dosya dışa aktarılan dosyayla değiştirilsin mi? Aktarım tamamlanana kadar aynı dosyayı başka uygulamalarda değiştirmeyin. Dosyayı korumak için farklı bir ad seçebilirsiniz.",
            "Mevcut dosyayı değiştir",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        if(!DocumentSession.ContentEquals(snapshot,Document)) throw new InvalidOperationException("Proje onay sırasında değişti; dışa aktarımı yeniden başlatın.");
        EnsureWritableDestination(dlg.FileName);
        EnsureExportDoesNotReplaceOpenProject(dlg.FileName);
        if(dlg.FilterIndex==1)
        {
            var layers=DxfFormat.PlanLayers(snapshot);
            if(layers.Any(layer=>layer.Changed) && new DxfLayerReviewWindow(this,layers).ShowDialog()!=true)return;
            if(!DocumentSession.ContentEquals(snapshot,Document)) throw new InvalidOperationException("Proje inceleme sırasında değişti; DXF dışa aktarımını yeniden başlatın.");
        }
        EnsureExportDoesNotReplaceOpenProject(dlg.FileName);
        _ = StartExportAfterPreflight(snapshot,dlg.FileName,dlg.FilterIndex,targetVersion,
            message=>MessageBox.Show(this,message,"Dışa aktarım bilgisi",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes);
    }
    internal const string HpglPreflightMessage = "Bu aktarım tüm proje parçalarını 40 birim/mm ile bir HPGL dosyasına yazar. "+
        "Dosya koordinat adımı 0,025 mm'dir; koordinatlar tam sayıya yuvarlanır. Bu değer fiziksel makine hassasiyeti değildir.\n\n"+
        "Küçük çentikler veya yön değişimleri yuvarlama sırasında düzleşebilir. Dosyayı üretimden önce kontrol edin.\n\n"+
        "Fiziksel makine uyumluluğu ve kalibrasyon doğrulanmadı. Farklı birim/mm değerlerini fiziksel bağlantı kurmayan Makine simülatöründe deneyebilirsiniz.\n\nDosyayı hazırlamak istiyor musunuz?";
    internal Task? StartExportAfterPreflight(ProjectDocument snapshot,string path,int format,ProjectFileVersion expected,Func<string,bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(confirm);
        if(format==3 && !confirm(HpglPreflightMessage)) { SetStatus("HPGL dışa aktarımı başlatılmadı; dosya değiştirilmedi.");return null; }
        if(format==4 && !confirm("PDF tek özel boyutlu sayfada inceleme içindir. Çok sayfalı yazdırma yoktur. Sayısal yuvarlama küçük kontur ayrıntılarını değiştirebilir. Yazıcı ve PDF uygulaması sayfayı ölçekleyebilir; fiziksel ölçüyü bağımsız kontrol edin. İplik yönü içeren parçalar desteklenmez. Devam edilsin mi?")) { SetStatus("PDF dışa aktarımı başlatılmadı; dosya değiştirilmedi.");return null; }
        // Dialogs pump the dispatcher; a drawing, drag or property draft can
        // appear without changing the committed document snapshot.
        EnsurePropertyDraftsApplied();
        if(!DocumentSession.ContentEquals(snapshot,Document))throw new InvalidOperationException("Proje inceleme sırasında değişti; dışa aktarımı yeniden başlatın.");
        return StartExportAsync(snapshot,path,format,expected);
    }
    private bool BlockForExport()
    {
        if (!ExportRunning) return false;
        SetStatus("Dışa aktarım sürüyor. Başka proje açmak, kaydetmek veya pencereyi kapatmak için işlem bitene kadar bekleyin.");
        return true;
    }
    private void EnsureNoExport()
    {
        if (ExportRunning) throw new InvalidOperationException("Dışa aktarım sürüyor. Yeni bir kayıt veya aktarım başlatmadan önce işlemin bitmesini bekleyin.");
        if (DxfImportRunning) throw new InvalidOperationException("DXF okuma sürüyor. Yeni bir kayıt veya aktarım başlatmadan önce işlemin bitmesini bekleyin.");
    }
    internal Task StartExportAsync(ProjectDocument snapshot, string path, int format, ProjectFileVersion expected)
    {
        EnsureNoExport();
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(expected);
        ExportDestination.ValidateExtension(path,format);
        EnsureWritableDestination(path); EnsureExportDoesNotReplaceOpenProject(path);
        var copy=snapshot.Clone(); Validate(copy);
        DxfPreparationStamp? hpglStamp=null;
        if(format==3)
        {
            if(HasPendingCanvasOrPropertyWork)throw new InvalidOperationException("HPGL incelemesinden önce çizimi ve alan taslaklarını tamamlayın veya iptal edin.");
            hpglStamp=CaptureDxfPreparationStamp();
            if(!DocumentSession.ContentEquals(copy,hpglStamp.Snapshot))throw new InvalidOperationException("Proje değişti; HPGL dışa aktarımını yeniden başlatın.");
        }
        ExportRunning=true; ExportError=null; ExportWasCancelled=false;
        var cancellation=format==3 ? new CancellationTokenSource() : null;
        exportCancellation=cancellation;
        exportCancel.Visibility=cancellation is null ? Visibility.Collapsed : Visibility.Visible;
        exportCancel.IsEnabled=cancellation is not null;
        if(exportCommand is not null)exportCommand.IsEnabled=false;if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=false;
        exportNotice.Foreground=StudioTheme.Muted; exportNotice.Text="Dışa aktarım sürüyor; sonraki düzenlemeler dosyaya dahil değil."; exportNotice.ToolTip=path; exportNotice.Visibility=Visibility.Visible;
        SetStatus(cancellation is null ? "Dosya proje kopyasından hazırlanıyor. Bu aktarım iptal edilemez; başka proje açmak, kaydetmek veya pencereyi kapatmak için bekleyin." : "HPGL proje kopyasından hazırlanıyor. İsterseniz iptal isteği gönderebilirsiniz; sonuç bildirilene kadar bekleyin.");
        return ExportTask=RunExportAsync(copy,path,format,expected,cancellation,hpglStamp);
    }
    private void RequestExportCancellation()
    {
        if (!ExportRunning || exportCancellation is null || exportCancellation.IsCancellationRequested) return;
        exportCancellation.Cancel();
        exportCancel.IsEnabled=false;
        exportNotice.Foreground=StudioTheme.Warning;
        exportNotice.Text="İptal istendi; sonuç bekleniyor. Dosya değiştirme işlemi başlamışsa aktarım tamamlanabilir.";
        SetStatus(exportNotice.Text);
    }
    private async Task RunExportAsync(ProjectDocument snapshot,string path,int format,ProjectFileVersion expected,CancellationTokenSource? cancellation,DxfPreparationStamp? hpglStamp)
    {
        try
        {
            if(format==3)
            {
                var token=cancellation!.Token;
                var before=BeforeHpglPreparationForVerification;var after=AfterHpglPreparationForVerification;
                var prepared=await Task.Run(()=> { before?.Invoke();var value=HpglFormat.Prepare(snapshot,40,token);after?.Invoke();return value; });
                token.ThrowIfCancellationRequested();
                if(hpglStamp is null || !DxfStampMatches(hpglStamp) || HasPendingCanvasOrPropertyWork)
                    throw new InvalidOperationException("Proje veya seçim hazırlık sırasında değişti; HPGL dosyası yazılmadı. Yeniden deneyin.");
                var approved=ConfirmHpglReviewForVerification is { } review
                    ? review(prepared,snapshot.Pieces)
                    : new HpglReviewWindow(this,prepared,snapshot.Pieces).ShowDialog()==true;
                token.ThrowIfCancellationRequested();
                if(!approved)
                {
                    ExportWasCancelled=true;exportNotice.Foreground=StudioTheme.Warning;
                    exportNotice.Text="HPGL incelemesi iptal edildi; hedef dosya değiştirilmedi.";exportNotice.ToolTip=path;SetStatus(exportNotice.Text);return;
                }
                if(!DxfStampMatches(hpglStamp) || HasPendingCanvasOrPropertyWork)
                    throw new InvalidOperationException("Proje veya seçim inceleme sırasında değişti; HPGL dosyası yazılmadı. Yeniden deneyin.");
                EnsureExportDoesNotReplaceOpenProject(path);
                token.ThrowIfCancellationRequested();
                // Keep user editing disabled only during the final conditional
                // write, so a later edit cannot silently invalidate this review.
                exportCancel.IsEnabled=false;
                exportNotice.Foreground=StudioTheme.Muted;
                exportNotice.Text="HPGL dosyası kaydediliyor; lütfen sonucu bekleyin.";
                SetStatus(exportNotice.Text);
                var previouslyEnabled=IsEnabled;IsEnabled=false;
                try { await Task.Run(()=>HpglFormat.WritePrepared(prepared,path,expected,token)); }
                finally { if(!windowClosed)IsEnabled=previouslyEnabled; }
            }
            else await Task.Run(()=> { switch(format) { case 1: DxfFormat.Export(snapshot,path,expected); break; case 2: SvgFormat.Export(snapshot,path,expected); break; case 4: PdfFormat.Export(snapshot,path,expected); break; } });
            var stale=!DocumentSession.ContentEquals(snapshot,Document) || propertyDrafts.Count>0 || canvas.HasPendingDrawing || canvas.LargeVertexValidationRunning || canvas.HasActiveDragPreview;
            exportNotice.Foreground=stale?StudioTheme.Warning:StudioTheme.Accent;
            exportNotice.Text=stale?"Dosya hazırlandı; proje değiştiği için önceki kopya kullanıldı.":"Dosya dışa aktarıldı.";
            exportNotice.ToolTip=path+(format==3?" · 40 birim/mm; fiziksel makine doğrulanmadı":"");
            SetStatus("Dosya dışa aktarıldı: "+path+(format==3?" · 40 birim/mm; fiziksel makine doğrulanmadı":"")+(stale?" · Proje işlem sırasında değişti; dosya önceki proje kopyasından hazırlandı.":""));
        }
        catch(OperationCanceledException) when(cancellation?.IsCancellationRequested == true)
        {
            ExportWasCancelled=true;
            exportNotice.Foreground=StudioTheme.Warning; exportNotice.Text="HPGL aktarımı iptal edildi; hedef dosya değiştirilmedi."; exportNotice.ToolTip=path; SetStatus(exportNotice.Text);
        }
        catch(ProjectConflictException)
        {
            ExportError="Dışa aktarım dosyası başka bir işlem tarafından değiştirildi veya silindi. Aktarım yapılmadı; açık projeniz korunuyor. Dışa aktarımı yeniden başlatıp farklı bir dosya adı seçebilirsiniz.";
            exportNotice.Foreground=StudioTheme.Danger;exportNotice.Text=ExportError;exportNotice.ToolTip=path;SetStatus(ExportError);
        }
        catch(Exception ex)
        { ExportError=ex.Message; exportNotice.Foreground=StudioTheme.Danger;exportNotice.Text="Dışa aktarım tamamlanamadı: "+ex.Message;exportNotice.ToolTip=ex.Message; SetStatus(exportNotice.Text); }
        finally
        {
            ExportRunning=false;
            if(ReferenceEquals(exportCancellation,cancellation))exportCancellation=null;
            cancellation?.Dispose();
            exportCancel.IsEnabled=false; exportCancel.Visibility=Visibility.Collapsed;
            if(exportCommand is not null)exportCommand.IsEnabled=Document.Pieces.Count>0;
            if(grainReviewExportCommand is not null)grainReviewExportCommand.IsEnabled=Document.Pieces.Count>0;
        }
    }
    private void EnsureExportDoesNotReplaceOpenProject(string target)
    {
        if(filePath is null)return;
        if(string.Equals(Path.GetFullPath(target),Path.GetFullPath(filePath),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Açık proje dosyasının üzerine dışa aktarım yapılamaz; farklı bir dosya seçin.");
        // With no source identity, an existing destination could be an alias
        // of the removed project. Only an absent target is safe to create.
        bool same;
        try { same=FileIdentity.SameExistingFile(filePath,target); }
        catch(FileNotFoundException) { same=ExistingDestinationWhenSourceMissing(); }
        catch(DirectoryNotFoundException) { same=ExistingDestinationWhenSourceMissing(); }
        if(same) throw new InvalidOperationException("Açık proje dosyasının üzerine dışa aktarım yapılamaz; farklı bir dosya seçin.");
        bool ExistingDestinationWhenSourceMissing()
        {
            try
            {
                using var handle=File.OpenHandle(Path.GetFullPath(target),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                throw new InvalidOperationException("Açık proje dosyası bulunamadı; var olan hedefin aynı dosya olmadığı doğrulanamıyor. Yeni bir dosya adı seçin.");
            }
            catch(FileNotFoundException) { return false; }
            catch(DirectoryNotFoundException) { return false; }
        }
    }
    internal void EnsureSeparateLayoutDestination(string target)
    {
        EnsureNoExport();
        var recoveryDirectory=Path.TrimEndingDirectorySeparator(AppPaths.RecoveryDirectory)+Path.DirectorySeparatorChar;
        if(Path.GetFullPath(target).StartsWith(recoveryDirectory,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pastalı yönetilen kurtarma klasörü dışında kaydedin.");
        EnsureWritableDestination(target);
        EnsureExportDoesNotReplaceOpenProject(target);
    }
    internal string? DxfLayerRenameNotice()
    {
        var changes=DxfFormat.PlanLayers(Document).Where(layer=>layer.Changed).ToArray();
        if(changes.Length==0)return null;
        static string Short(string text)
        {
            // Render control and direction-format characters visibly so the review
            // cannot be mistaken for a different layer mapping or multiple rows.
            var result = new System.Text.StringBuilder();
            foreach (var rune in text.EnumerateRunes())
            {
                var category = System.Text.Rune.GetUnicodeCategory(rune);
                var token = category is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator
                    ? $"\\u{rune.Value:X4}" : rune.ToString();
                if (result.Length + token.Length > 77) { result.Append('…'); break; }
                result.Append(token);
            }
            return result.ToString();
        }
        var examples=changes.Take(5).Select(layer=>$"• {Short(layer.OriginalName)} → {Short(layer.LayerName)}");
        return $"DXF katman kuralları nedeniyle {changes.Length} parça adı değişecek. Projedeki adlar değişmez.\n"+
            string.Join("\n",examples)+(changes.Length>5?$"\n… ve {changes.Length-5} parça daha.":"");
    }
    private void ArchiveRecoveredSource()
    {
        if(recoveredSource is null || recoveryStore is null)return;
        if(recoveryRestoreIncomplete)return;
        if(!recoveredSource.CanArchive) { recoveredSource=null;recoveredDocumentSaved=false;return; } // Older versions have unknown liveness; preserve their original file.
        try
        {
            recoveryStore.ArchiveRecovered(recoveredSource);recoveredSource=null;recoveredDocumentSaved=false;
            ClearGeneralRecoveryWarning("Projeniz kaydedildi; önceki kurtarma kaydı korundu.");
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ShowRecoveryWarning("Projeniz kaydedildi; önceki kurtarma kaydı korundu.",ex.Message); }
    }
    private void ClearRecovery()
    {
        _ = ObserveRecoveryClearAsync();
    }
    private async Task ObserveRecoveryClearAsync()
    {
        var clear=ClearRecoveryAsync();
        var request=recoveryClearRequest; var writer=recoveryWriter;
        try { await clear; }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { if(!windowClosed && request==recoveryClearRequest && ReferenceEquals(writer,recoveryWriter)) { ShowRecoveryWarning("Kurtarma kaydı temizlenemedi; özgün kayıt korunuyor.",ex.Message,RecoveryWarningState.Kind.Clear); UpdateSaveState(); } }
    }
    private async Task ClearRecoveryAsync()
    {
        recoveryGeneration++; recoveryMayExist=false; lastRecoveryAt=null;
        var request=++recoveryClearRequest;
        var writer=recoveryWriter;
        if(writer is null)return;
        try
        {
            await writer.ClearAsync();
            if(ReferenceEquals(recoveryWriter,writer) && request>=recoveryClearReported)
            {
                recoveryClearReported=request; recoveryClearFailed=false;
                if(request==recoveryClearRequest && recoveryClearWarning)
                { ClearRecoveryWarning(RecoveryWarningState.Kind.Clear); UpdateSaveState(); }
            }
        }
        catch
        {
            // Keep the failure distinct from recoveryMayExist: Refresh must not
            // enqueue an unbounded retry storm. Explicit save/load/close retries.
            if(ReferenceEquals(recoveryWriter,writer) && request>=recoveryClearReported) { recoveryClearReported=request; recoveryClearFailed=true; }
            throw;
        }
    }
    private async Task SaveRecoveryAsync()
    {
        if(!HasUnsavedWork || recoveryWriter is null || recoveryClosing)return;
        var generation=recoveryGeneration;
        var request=++recoveryRequest;
        var capturedAt=DateTime.Now;
        try
        {
            var drafts=propertyDrafts.Select(pair => new RecoveryPropertyDraft(pair.Key,pair.Value.Name,pair.Value.X,pair.Value.Y,pair.Value.BaseName,pair.Value.BaseX,pair.Value.BaseY)).ToArray();
            recoveryMayExist=true;
            var written=await recoveryWriter.SaveAsync(Document,drafts,canvas.CapturePendingDrawing());
            if(!written || generation!=recoveryGeneration || request<recoveryReported || windowClosed || recoveryClosing)return;
            recoveryReported=request;
            lastRecoveryAt=capturedAt;
            if(recoverySaveWarning) ClearRecoveryWarning(RecoveryWarningState.Kind.Save);
            UpdateSaveState();
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            if(generation!=recoveryGeneration || request<recoveryReported || windowClosed)return;
            recoveryReported=request;
            ShowRecoveryWarning("Otomatik kurtarma güncellenemedi. Son değişikliklerinizi Kaydet ile koruyun.",ex.Message,RecoveryWarningState.Kind.Save);
        }
    }
    internal async Task SetRecoveryForVerificationAsync(RecoveryStore? store, RecoveryWriteCoordinator? coordinator)
    {
        recoveryGeneration++;
        if(recoveryWriter is not null)await recoveryWriter.DisposeAsync();
        recoveryStore=store; recoveryWriter=coordinator; recoveryMayExist=false; recoveryClearFailed=false; lastRecoveryAt=null;
    }
    internal Task TriggerRecoveryForVerificationAsync() => SaveRecoveryAsync();
    internal Task ClearRecoveryForVerificationAsync() => ClearRecoveryAsync();
    // Scan errors are separate from autosave/cleanup warnings and belong only to
    // the current scan/page. Clearing them must not clear other recovery warnings.
    private void SetRecoveryScanDetail(string? detail)
    {
        recoveryScanNotice.ToolTip=detail;
        recoveryScanDetails.Visibility=detail is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private async Task BeginRecoveryScan()
    {
        if(recoveryStore is null || windowClosed || recoveryScanRunning)return;
        recoveryCursor?.Dispose();
        recoveryCursor=null; recoveryScan=null; recoveryHasMore=false;
        recoveryPages.Clear(); recoveryPageIndex=-1; recoveryFirstPageNumber=1;
        recoveryOpen.Visibility=Visibility.Collapsed;
        SetRecoveryScanDetail(null);
        var directory=Path.GetDirectoryName(recoveryStore.RecoveryPath)!;
        try { recoveryCursor=new RecoveryScanCursor(directory,recoveryStore.DraftRecoveryPath); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            recoveryScanNotice.Text="Kurtarma taraması başlatılamadı. Dosyaları silmeden saklayın.";
            SetRecoveryScanDetail(ex.Message); recoveryScanNotice.Visibility=Visibility.Visible;
            return;
        }
        await ContinueRecoveryScan();
    }
    private async Task ContinueRecoveryScan()
    {
        if(recoveryStore is null || recoveryCursor is null || windowClosed || recoveryScanRunning)return;
        recoveryScanRunning=true;
        if(recoveryRescan is not null)recoveryRescan.IsEnabled=false;
        // The single worker owns the cursor until it returns. Close cancels first,
        // and the finally block disposes it only after worker completion.
        var cursor=recoveryCursor;
        var directory=Path.GetDirectoryName(recoveryStore.RecoveryPath)!;
        recoveryScanNotice.Text="Kurtarma kayıtları arka planda taranıyor.";
        recoveryScanNotice.Visibility=Visibility.Visible;
        SetRecoveryScanDetail(null);
        recoveryOpen.Visibility=Visibility.Collapsed;
        try
        {
            var token=recoveryScanCancellation.Token;
            var page=await Task.Run(()=>cursor.ReadNextPage(token),token);
            if(windowClosed)return;
            recoveryPages.Add(page);
            if(recoveryPages.Count>RecoveryPageHistoryLimit) { recoveryPages.RemoveAt(0); recoveryFirstPageNumber++; }
            recoveryPageIndex=recoveryPages.Count-1;
            ShowRecoveryPage();
        }
        catch(OperationCanceledException) when(recoveryScanCancellation.IsCancellationRequested) { }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            if(windowClosed)return;
            recoveryScan=new RecoveryScanResult([], [new(directory,ex.Message)]);
            recoveryScanNotice.Text="Kurtarma taraması tamamlanamadı; kayıtlar listelenmemiş olabilir. Dosyaları silmeden saklayın.";
            SetRecoveryScanDetail(ex.Message);
            recoveryOpen.Content="Kurtarma taraması ayrıntıları";
            recoveryOpen.Visibility=Visibility.Visible;
        }
        finally
        {
            recoveryScanRunning=false;
            if(windowClosed) { cursor.Dispose(); if(ReferenceEquals(recoveryCursor,cursor))recoveryCursor=null; }
            if(!windowClosed && recoveryRescan is not null)recoveryRescan.IsEnabled=true;
        }
    }
    private void ShowRecoveryPage()
    {
            var page=recoveryPages[recoveryPageIndex];
            var scan=new RecoveryScanResult(page.Candidates,page.Errors);
            recoveryScan=scan; recoveryHasMore=page.HasMore;
            var pageNumber=recoveryFirstPageNumber+recoveryPageIndex;
            var historyNote=recoveryFirstPageNumber>1 ? " Eski sayfalar için Kurtarma tara ile yeniden başlayın." : "";
            SetRecoveryScanDetail(null);
            if(scan.Errors.Count>0)
            {
                recoveryScanNotice.Text=$"Sayfa {pageNumber}: tarama tamamlanmamış veya bazı kayıtlar okunamamış olabilir. Dosyaları silmeden saklayın."+historyNote;
                SetRecoveryScanDetail(string.Join("\n",scan.Errors.Select(e=>e.Message)));
                // The details action is separate from the autosave error channel.
                recoveryOpen.Content="Kurtarma taraması ve kayıtları";
                recoveryOpen.Visibility=Visibility.Visible;
            }
            else if(scan.Candidates.Count>0)
            {
                recoveryScanNotice.Text=$"Sayfa {pageNumber}: {scan.Candidates.Count} kurtarma kaydı. İncelemek için düğmeyi kullanın."+historyNote;
                recoveryOpen.Content="Kurtarma kayıtlarını incele";
                recoveryOpen.Visibility=Visibility.Visible;
            }
            else if(page.HasMore || recoveryPageIndex>0) { recoveryScanNotice.Text=$"Sayfa {pageNumber}: kayıt yok; diğer sayfaları inceleyebilirsiniz."+historyNote; recoveryOpen.Content="Kurtarma kayıtlarını incele"; recoveryOpen.Visibility=Visibility.Visible; }
            else { recoveryScanNotice.Text="Bu sayfada kurtarılabilir kayıt bulunamadı."; recoveryOpen.Visibility=Visibility.Collapsed; }
    }
    private async void OfferRecovery()
    {
        if(BlockForDxfImport())return;
        if(recoveryStore is null || recoveryScan is not { } scan)return;
        if(scan.Errors.Count>0)
            MessageBox.Show(this,"Bazı kurtarma kayıtları taranmadı veya okunamadı. Dosyaları silmeden saklayın.\n\n"+string.Join("\n",scan.Errors.Select(e=>e.Message)),"Kurtarma taraması",MessageBoxButton.OK,MessageBoxImage.Warning);
        if(scan.Candidates.Count==0 && !recoveryHasMore && recoveryPageIndex<=0)return;
        var state=new RecoveryDialogState();
        var prompt=CreateRecoverySelectionWindow(scan.Candidates,recoveryFirstPageNumber+recoveryPageIndex,recoveryFirstPageNumber>1,
            recoveryPageIndex>0,recoveryPageIndex+1<recoveryPages.Count || recoveryHasMore,state);
        var accepted=prompt.ShowDialog();
        if(state.PreviousPage) { recoveryPageIndex--;ShowRecoveryPage();await Task.Yield();if(!windowClosed)OfferRecovery();return; }
        if(state.NextPage) { if(recoveryPageIndex+1<recoveryPages.Count) { recoveryPageIndex++;ShowRecoveryPage();await Task.Yield(); } else await ContinueRecoveryScan(); if(!windowClosed)OfferRecovery();return; }
        if(accepted!=true || state.ChosenPath is not { } chosenPath || !CanDiscard())return;
        // Reload under the candidate lease after the user's decision: the scan is
        // a stale listing, not a promise that the file is unchanged or still present.
        try { RestoreRecovery(recoveryStore.LoadCandidate(chosenPath)); ClearGeneralRecoveryWarning("Kurtarma kaydı açılamadı; geçerli çalışmanız korundu."); }
        catch(RecoveryRestoreAppliedException ex)
        { ShowRecoveryWarning(recoveryRestoreIncomplete
            ? "Kurtarma projesi açıldı ancak çizim önizlemesi tamamlanamadı. Çizim alanı kapalı; özgün kurtarma kaydını yeniden açın."
            : "Kurtarma kaydı açıldı, ancak görünüm yenilenemedi. Özgün kurtarma kaydı korundu; çalışmayı kontrol edin.",ex.Message); }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { ShowRecoveryWarning("Kurtarma kaydı açılamadı; geçerli çalışmanız korundu.",ex.Message); }
    }

    internal sealed class RecoveryDialogState
    {
        public bool PreviousPage { get; set; }
        public bool NextPage { get; set; }
        public string? ChosenPath { get; set; }
    }

    internal Window CreateRecoverySelectionWindow(IReadOnlyList<RecoveryCandidate> candidates,int pageNumber,bool earlierPagesForgotten,
        bool canGoPrevious,bool canGoNext,RecoveryDialogState state)
    {
        var prompt=new Window { Owner=this,Title="Çalışmanızı kurtarın",Width=620,Height=520,MinWidth=500,MinHeight=420,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        StudioTheme.Apply(prompt);var panel=new DockPanel { Margin=new Thickness(24) };prompt.Content=panel;
        var intro=new TextBlock { Text=$"Sayfa {pageNumber}. Yalnız bu sayfadaki kayıtlar gösterilir. Dizin değişirse kayıtlar eksik veya tekrarlı görünebilir; güncellemek için yeniden tarayın. Açık oturumlar listelenmez."+(earlierPagesForgotten ? " Eski sayfalar için Kurtarma tara ile yeniden başlayın." : ""),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14) };
        DockPanel.SetDock(intro,Dock.Top);panel.Children.Add(intro);
        var commands=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) };
        DockPanel.SetDock(commands,Dock.Bottom);panel.Children.Add(commands);
        var details=new TextBox { IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=74,MaxHeight=110,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,8,0,0) };
        System.Windows.Automation.AutomationProperties.SetName(details,"Seçili kurtarma kaydı ayrıntıları");
        DockPanel.SetDock(details,Dock.Bottom);panel.Children.Add(details);
        var list=new ListBox();System.Windows.Automation.AutomationProperties.SetName(list,"Kurtarılabilir proje kayıtları");panel.Children.Add(list);
        foreach(var candidate in candidates)
        {
            var label=(candidate.ProjectName??"Okunamayan kayıt")+"  ·  "+candidate.LastWriteTimeUtc.ToLocalTime().ToString("G",CultureInfo.CurrentCulture);
            if(candidate.IsLegacy)label+="\nEski sürüm · Özgün kayıt korunacak; oturum durumu bilinmiyor";
            if(candidate.Error is not null)label+="\n"+candidate.Error;
            list.Items.Add(new ListBoxItem { Content=new TextBlock { Text=label,TextWrapping=TextWrapping.Wrap },Tag=candidate,ToolTip=candidate.Path,Padding=new Thickness(9) });
        }
        AddButton(commands,"Şimdilik atla",()=>prompt.DialogResult=false);
        if(canGoPrevious)AddButton(commands,"Önceki sayfa",()=> { state.PreviousPage=true;prompt.DialogResult=false; });
        if(canGoNext)AddButton(commands,"Sonraki sayfa",()=> { state.NextPage=true;prompt.DialogResult=false; });
        var open=AddButton(commands,"Seçili kaydı aç",()=> { if(list.SelectedItem is ListBoxItem { Tag: RecoveryCandidate { IsRecoverable:true } c }) { state.ChosenPath=c.Path;prompt.DialogResult=true; } });
        open.IsEnabled=false;open.IsDefault=true;
        list.SelectionChanged+=(_,_)=>
        {
            var candidate=(list.SelectedItem as ListBoxItem)?.Tag as RecoveryCandidate;
            open.IsEnabled=candidate?.IsRecoverable==true;
            details.Text=candidate is null ? "" :
                $"Dosya: {candidate.Path}\nSon değiştirme: {candidate.LastWriteTimeUtc.ToLocalTime().ToString("G",CultureInfo.CurrentCulture)}\nDurum: {(candidate.Error ?? "Kurtarılabilir")}";
        };
        return prompt;
    }
    private sealed class RecoveryRestoreAppliedException : InvalidOperationException
    {
        internal RecoveryRestoreAppliedException(string message,Exception inner) : base(message,inner) { }
    }
    private bool BlockForIncompleteRecovery()
    {
        if(!recoveryRestoreIncomplete)return false;
        SetStatus("Kurtarılan çizim tam açılamadı · Düzenlemek için özgün kurtarma kaydını yeniden açın.");
        return true;
    }
    internal Action? BeforeRecoveredDrawingRestoreForVerification { get; set; }
    private void RestoreRecovery(RecoveredDocument chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        EnsureNoExport();
        if(recoveryStore is not null)
        {
            var chosenPath=Path.GetFullPath(chosen.SourcePath);
            if(string.Equals(chosenPath,Path.GetFullPath(recoveryStore.RecoveryPath),StringComparison.OrdinalIgnoreCase) ||
               string.Equals(chosenPath,Path.GetFullPath(recoveryStore.DraftRecoveryPath),StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Açık oturumun kendi kurtarma kaydı bu işlemle açılamaz; özgün kayıt korundu.");
        }
        // All independently checkable input and duplicate draft failures occur
        // before replacing the live session. Never alter the source recovery file.
        var document=chosen.Document.Clone();
        Validate(document);
        var drafts=new Dictionary<Guid,PropertyDraft>();
        foreach(var draft in chosen.Drafts)
            drafts.Add(draft.PieceId,new PropertyDraft(draft.Name,draft.X,draft.Y,draft.BaseName,draft.BaseX,draft.BaseY));
        RecoveryPendingDrawing? pending=null;
        if(chosen.PendingDrawing is { } sourceDrawing)
        {
            CadCanvas.ValidatePendingDrawing(sourceDrawing);
            pending=new RecoveryPendingDrawing(sourceDrawing.Tool,sourceDrawing.Points.ToArray());
        }
        var preparedDrawing=CadCanvas.PreparePendingDrawing(pending);
        EnsureNoLargeVertexValidation();
        // Load clones and validates before publishing. Record the recovery
        // identity and unsaved state immediately, before any UI work can throw.
        session.Load(document,Validate);
        mainMutationReviewRequired=false;
        // Publish the validated drawing state by field assignment before any
        // event, view refresh or other fallible UI work. Old preview points can
        // therefore never remain attached to the recovered project.
        canvas.InstallPreparedPendingDrawing(preparedDrawing);
        if(pending is not null)ActiveTool=pending.Tool;
        projectEpoch=Guid.NewGuid();session.MarkUnsaved();dirty=true;
        filePath=null;fileVersion=null;savedFileHasGradingRules=false;savedFileHasMetadata=false;savedFileHasNestingSnapshot=false;savedFileHasGrainline=false;savedFileHasImportProvenance=false;
        SelectedId=null;recoveredSource=chosen;recoveredDocumentSaved=false;recoveryRestoreIncomplete=true;
        propertyDrafts.Clear();
        foreach(var (id,draft) in drafts)propertyDrafts.Add(id,draft);
        // Invalidate earlier session writes with a serialized clear barrier;
        // queue this fully staged snapshot behind it. The coordinator owns only
        // the current session record, never the chosen recovery source.
        ClearRecovery();
        _=SaveRecoveryAsync();
        try
        {
            if(pending is not null)BeforeRecoveredDrawingRestoreForVerification?.Invoke();
            canvas.ReleaseMouseCapture();canvas.InvalidateVisual();
            RefreshToolPresentation(ActiveTool);
            recoveryRestoreIncomplete=false;
            canvas.IsEnabled=true;
            Refresh();canvas.Fit();
            SetStatus("Kurtarma kaydı açıldı. Kaydet ile kalıcı bir proje dosyası oluşturun.");
        }
        catch(Exception ex)
        {
            // The recovered preview is installed, but presentation may not
            // reflect its active tool. Prevent edits until it is reopened.
            if(recoveryRestoreIncomplete)canvas.IsEnabled=false;
            throw new RecoveryRestoreAppliedException("Kurtarma projesi açıldı ve kaydedilmemiş olarak işaretlendi; görünüm veya çizim önizlemesi tam yenilenemedi. Kaynak kurtarma kaydı korundu. Çizim alanı kapalıysa kurtarma kaydını yeniden açın.",ex);
        }
    }
    private void DeleteSelectionFromKeyboard()
    {
        // The selection can remain active while another tool is drawing.
        if (canvas.LargeVertexValidationRunning || canvas.LargeCompletionRunning)
        { SetStatus("Parça silinmedi · Doğrulamanın bitmesini bekleyin veya Esc ile iptal edin."); return; }
        if (canvas.HasActiveDragPreview)
        { SetStatus("Parça silinmedi · Sürüklemeyi tamamlayın veya Esc ile iptal edin."); return; }
        if (canvas.HasPendingDrawing)
        { SetStatus("Parça silinmedi · Önce çizimi tamamlayın veya Esc ile iptal edin; sonra Seç aracını kullanın."); return; }
        if (ActiveTool != "Seç")
        { SetStatus("Parça silinmedi · Silmek için önce Seç aracını kullanın."); return; }
        DeleteSelection();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var editing = Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox;
        try
        {
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
            { SaveAs(); e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.S) Save();
                else if (e.Key == Key.O) Open();
                else if (e.Key == Key.N) NewDocument();
                else if (!editing && e.Key == Key.Z) Undo();
                else if (!editing && e.Key == Key.Y) Redo();
                else return;
                e.Handled = true; return;
            }
            if (editing || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled=HandleCanvasShortcut(e.Key,canvas.IsKeyboardFocused);
        }
        catch (ProjectConflictException) { e.Handled = true; ShowProjectSaveConflict(this); }
        catch (Exception ex) { e.Handled = true; MessageBox.Show(this, ex.Message, "İşlem tamamlanamadı", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    // The caller must supply direct canvas focus, never focus-within. This prevents
    // unrelated controls from triggering drawing commands or deleting a piece.
    internal bool HandleCanvasShortcut(Key key,bool canvasFocused)
    {
        if(!canvasFocused)return false;
        if(recoveryRestoreIncomplete && key is (Key.V or Key.L or Key.P or Key.R or Key.B or Key.A or Key.F or Key.G or Key.Escape or Key.Enter or Key.Delete or Key.Back))
        { BlockForIncompleteRecovery();return true; }
        switch(key)
        {
            case Key.V: SetTool("Seç"); break;
            case Key.L: SetTool("Çizgi"); break;
            case Key.P: SetTool("Çoklu çizgi"); break;
            case Key.R: SetTool("Dikdörtgen"); break;
            case Key.B: SetTool("Bézier"); break;
            case Key.A: SetTool("Yay"); break;
            case Key.F: canvas.Fit(); break;
            case Key.G: canvas.FitSelectedPiece(); break;
            case Key.Escape: canvas.CancelDrawing(); ClearCanvasError(); break;
            case Key.Enter: canvas.FinishPolyline(); break;
            case Key.Back: canvas.RemoveLastPendingPoint(); break;
            case Key.Delete: DeleteSelectionFromKeyboard(); break;
            default: return false;
        }
        return true;
    }
    private static ProjectDocument Sample() => new() { Name="İlk kalıp çalışması", Pieces=[new PatternPiece { Name="Ön beden · örnek", Points=[new(0,0),new(65,18),new(105,0),new(180,35),new(210,140),new(190,200),new(205,470),new(0,470)] },new PatternPiece { Name="Arka beden · örnek", Points=[new(270,10),new(350,20),new(395,10),new(455,45),new(485,145),new(460,205),new(475,470),new(270,470)] }] };
}








