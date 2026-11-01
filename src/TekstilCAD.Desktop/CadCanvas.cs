using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;
using Geometry = TekstilCAD.Core.Geometry;

namespace TekstilCAD.Desktop;

public sealed class CadCanvas : FrameworkElement
{
    private readonly MainWindow owner;
    // Distinguish a committed document change from a later UI refresh failure.
    private long observedCommits;
    private double scale=1.15;
    private Vector offset=new(80,65);
    private List<Point2> drawing=[];
    private Point2 cursor;
    private Point? panStart;
    private Guid? dragId;
    internal bool HasActiveDragPreview => dragId is not null;
    private int dragVertex = -1;
    private Point2 dragOrigin;
    private Point? dragScreenOrigin;
    private List<Point2>? dragOriginal, dragPreview;
    private Grainline? dragOriginalGrainline;
    private Grainline? dragPreviewGrainline;
    private Point2 dragTranslation;
    private PatternPiece? dragSourceSnapshot;
    private CancellationTokenSource? largeCompletionCancellation;
    private CancellationTokenSource? largeVertexCancellation;
    internal Task? LargeVertexValidationTask { get; private set; }
    internal Action? BeforeLargeVertexValidationForVerification { get; set; }
    internal bool LargeVertexValidationRunning => largeVertexCancellation is not null;
    internal Task? LargeCompletionTask { get; private set; }
    internal bool LargeCompletionRunning => largeCompletionCancellation is not null;
    /// <summary>True while an unfinished drawing has points not yet committed to the document.</summary>
    internal bool HasPendingDrawing => drawing.Count > 0;
    internal int PendingDrawingPointCount => drawing.Count;
    internal Point2? LastPendingDrawingPoint => drawing.Count == 0 ? null : drawing[^1];

    /// <summary>Describes the next actual drawing action; sampled curves are not parametric.</summary>
    internal string ToolGuidance(string tool) => tool switch
    {
        "Seç" => "Bir parçaya tıklayın; taşımak için parçayı veya noktasını sürükleyin.",
        "Çizgi" => drawing.Count == 0 ? "Çizginin başlangıcına tıklayın." : "Çizginin bitişine tıklayın; Esc iptal eder.",
        "Dikdörtgen" => drawing.Count == 0 ? "Dikdörtgenin ilk köşesine tıklayın." : "Karşı köşeye tıklayın; Esc iptal eder.",
        "Bézier" => drawing.Count switch
        {
            0 => "Eğrinin başlangıcına tıklayın. Sonuç örneklenmiş çizgidir.",
            1 => "Birinci kontrol noktasına tıklayın; Esc iptal eder.",
            2 => "İkinci kontrol noktasına tıklayın; Esc iptal eder.",
            _ => "Eğrinin bitişine tıklayın; Esc iptal eder."
        },
        "Yay" => drawing.Count switch
        {
            0 => "Yayın merkezine tıklayın. Sonuç örneklenmiş çizgidir.",
            1 => "Başlangıca tıklayın; merkezden uzaklık yarıçapı belirler. Esc iptal eder.",
            _ => "Saat yönünde bitiş yönüne tıklayın; yarıçap değişmez. Esc iptal eder."
        },
        "Çoklu çizgi" => $"Noktaları tıklayın ({drawing.Count} nokta); açık bitirmek için en az 2 nokta ve Shift+Enter veya Açık çizgiyi bitir düğmesi, kapatmak için en az 3 nokta ve Enter kullanın. Esc iptal eder.",
        _ => "Başlamak için çizim alanına tıklayın."
    };

    /// <summary>Returns an independent copy; no project geometry is committed.</summary>
    internal RecoveryPendingDrawing? CapturePendingDrawing() => drawing.Count == 0
        ? null : new RecoveryPendingDrawing(owner.ActiveTool, drawing.ToArray());

    /// <summary>Checks the complete snapshot before any active tool or canvas state changes.</summary>
    internal static void ValidatePendingDrawing(RecoveryPendingDrawing snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var maximum = snapshot.Tool switch
        {
            "Çizgi" or "Dikdörtgen" => 1,
            "Bézier" => 3,
            "Yay" => 2,
            "Çoklu çizgi" => 10_000,
            _ => throw new ArgumentException("Kurtarılan çizimin aracı desteklenmiyor.")
        };
        if (snapshot.Points is null || snapshot.Points.Count < 1 || snapshot.Points.Count > maximum)
            throw new ArgumentException("Kurtarılan çizimin nokta sayısı geçersiz.");
        foreach (var point in snapshot.Points) Geometry.Validate(point);
    }

    // Prepare before the document is replaced: validation and allocation may fail.
    internal static List<Point2> PreparePendingDrawing(RecoveryPendingDrawing? snapshot)
    {
        if (snapshot is null) return [];
        ValidatePendingDrawing(snapshot);
        var points = snapshot.Points.ToList();
        ValidatePendingDrawing(new RecoveryPendingDrawing(snapshot.Tool, points));
        return points;
    }

    // Recovery calls this immediately after committing the document. No UI events,
    // cancellation callbacks, validation, or allocation may intervene here.
    internal void InstallPreparedPendingDrawing(List<Point2> points)
    {
        drawing = points;
        cursor = points.Count == 0 ? default : points[^1];
        ClearDrag();
        panStart = null;
    }

    /// <summary>Restores an unfinished preview only; the caller selects its validated tool first.</summary>
    internal void RestorePendingDrawing(RecoveryPendingDrawing snapshot)
    {
        ValidatePendingDrawing(snapshot);
        if (owner.ActiveTool != snapshot.Tool)
            throw new InvalidOperationException("Kurtarılan çizimin aracı seçili değil.");
        var points = snapshot.Points.ToList();
        // All checks precede mutation so invalid input leaves any existing preview intact.
        CancelLargeCompletion();
        drawing = points;
        cursor = points[^1];
        ClearDrag(); panStart = null; ReleaseMouseCapture(); InvalidateVisual();
        owner.PendingDrawingChanged();
    }
    private const string KeyboardHelp = "Kalıp alanı. Page Up ve Page Down ile yakınlaştırın veya uzaklaştırın; Alt ve oklarla görünümü kaydırın. Seçili parçayı oklarla 1 mm, Shift ve oklarla 10 mm, Ctrl ve oklarla 0,1 mm taşıyın. Backspace tamamlanmamış çizimin son noktasını kaldırır. Escape çizim veya sürükleme önizlemesini iptal eder. Proje işlemlerini geri almak için Ctrl+Z kullanın.";
    private static Brush B(string s)=>(Brush)new BrushConverter().ConvertFromString(s)!;
    public CadCanvas(MainWindow owner)
    {
        this.owner=owner; Focusable=true; ClipToBounds=true;
        AutomationProperties.SetName(this,"Kalıp çalışma alanı");
        AutomationProperties.SetHelpText(this,KeyboardHelp);
        ToolTip=KeyboardHelp;
        GotKeyboardFocus+=(_,_)=> { InvalidateVisual();if(!LargeVertexValidationRunning && !LargeCompletionRunning)owner.SetStatus(ToolGuidance(owner.ActiveTool)+" · Oklar: 1 mm · Shift+ok: 10 mm · Ctrl+ok: 0,1 mm"); };
        LostKeyboardFocus+=(_,_)=>InvalidateVisual();
        MouseDown+=(s,e)=>Safe(()=>Down(s,e), restoreDrawingOnFailure:true); MouseMove+=(s,e)=>Safe(()=>Move(s,e));
        MouseUp+=(s,e)=>Safe(()=>Up(e)); MouseWheel+=(s,e)=>Safe(()=>Wheel(s,e));
        LostMouseCapture+=(_,_)=> { panStart=null; ClearDrag(); InvalidateVisual(); };
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new CanvasAutomationPeer(this);
    private sealed class CanvasAutomationPeer(CadCanvas canvas) : FrameworkElementAutomationPeer(canvas)
    {
        protected override string GetClassNameCore() => nameof(CadCanvas);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // A text box, list or other control owns its arrow keys; never use IsKeyboardFocusWithin here.
        if(e.Handled || !IsKeyboardFocused)return;
        var key=e.Key==Key.System ? e.SystemKey : e.Key;
        var modifiers=Keyboard.Modifiers;
        if(modifiers==ModifierKeys.None && key is Key.PageUp or Key.PageDown)
        { e.Handled=true;ZoomView(key==Key.PageUp);return; }
        if(modifiers==ModifierKeys.Alt && IsMovementKey(key))
        {
            e.Handled=true;
            PanView(key==Key.Left ? 80 : key==Key.Right ? -80 : 0,
                    key==Key.Up ? 80 : key==Key.Down ? -80 : 0);
            return;
        }
        if(e.Key==Key.Escape)
        {
            e.Handled=true;CancelDrawing();owner.ClearCanvasError();owner.SetStatus("Önizleme iptal edildi · Proje geometrisi değişmedi");return;
        }
        if(!IsMovementKey(e.Key) || !IsMovementModifier(modifiers))return;
        e.Handled=true;Safe(()=>MoveSelectionByKeyboard(e.Key,modifiers));
    }
    private static bool IsMovementKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down;
    private static bool IsMovementModifier(ModifierKeys modifiers) => modifiers is ModifierKeys.None or ModifierKeys.Shift or ModifierKeys.Control;
    // Kept separate from the focus gate for deterministic command verification without showing a window.
    private void MoveSelectionByKeyboard(Key key,ModifierKeys modifiers)
    {
        if(!IsMovementKey(key) || !IsMovementModifier(modifiers))return;
        if(LargeVertexValidationRunning || LargeCompletionRunning)
        { owner.SetStatus("Doğrulama sürüyor; taşımak için bitmesini bekleyin veya Esc ile iptal edin.");return; }
        if(drawing.Count>0 || dragId is not null || panStart is not null)
        { owner.SetStatus("Önce çizimi tamamlayın veya Esc ile önizlemeyi iptal edin; ardından oklarla taşıyın.");return; }
        var piece=owner.Selected;
        if(piece is null)
        { owner.SetStatus("Taşımak için önce proje parçaları listesinden veya kalıp alanından bir parça seçin.");return; }
        if(owner.HasPropertyDraftFor(piece.Id))
        { owner.SetStatus("Parça taşınmadı · Önce bu parçanın ad ve konum taslağını uygulayın veya temizleyin.");return; }
        var step=modifiers==ModifierKeys.Control ? .1 : modifiers==ModifierKeys.Shift ? 10 : 1;
        var dx=key==Key.Left ? -step : key==Key.Right ? step : 0;
        var dy=key==Key.Up ? -step : key==Key.Down ? step : 0;
        var id=piece.Id;
        var (translated,translatedNotches)=MarkedPieceTranslation.Translate(piece,dx,dy);
        var translatedGrain=GrainlineTransform.Translate(piece.Grainline,piece.Points[0],translated[0],dx,dy);
        var rule=owner.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==id);
        var translatedRule=rule?.TranslatedFor(piece,translated,dx,dy);
        owner.CommitObserved($"Parça klavyeyle taşındı · X {dx.ToString("0.#",CultureInfo.GetCultureInfo("tr-TR"))} mm · Y {dy.ToString("0.#",CultureInfo.GetCultureInfo("tr-TR"))} mm",()=>
        {
            var target=owner.Document.Pieces.FirstOrDefault(p=>p.Id==id)
                ?? throw new InvalidOperationException("Seçili parça değişti. Yeniden seçerek taşıyın.");
            target.Points=translated;target.Notches=translatedNotches;
            target.Grainline=translatedGrain;
            if(translatedRule is not null)
                owner.Document.GradingRules[owner.Document.GradingRules.IndexOf(rule!)]=translatedRule;
        },()=>observedCommits++);
        if(rule is not null && translatedRule is null)
            owner.ShowCanvasError("Parça taşındı; kayıtlı serileme kuralı önceki kontura ait. Serilemeden önce kuralı gözden geçirin.");
        else owner.ClearCanvasError();
    }
    private void Safe(Action action, bool restoreDrawingOnFailure = false, Action<string>? reportError = null)
    {
        // A failed final click can append a point before validation. Restore the
        // earlier preview rather than losing the user's entire unfinished drawing.
        var previousDrawing = restoreDrawingOnFailure ? drawing.ToList() : null;
        var previousDocument=owner.Document;
        var commitsBefore=observedCommits;
        try { action(); if(restoreDrawingOnFailure && previousDrawing is not null && (!previousDrawing.SequenceEqual(drawing) || !ReferenceEquals(previousDocument,owner.Document)))owner.ClearCanvasError(); }
        catch (Exception ex) when ((observedCommits != commitsBefore && IsNonFatal(ex)) ||
            ex is ArgumentException or InvalidOperationException or ArithmeticException or System.IO.InvalidDataException or OperationCanceledException)
        {
            var committed=observedCommits!=commitsBefore;
            if (!committed && previousDrawing is not null) drawing = previousDrawing;
            ClearDrag(); panStart=null; ReleaseMouseCapture(); InvalidateVisual();
            var message=committed
                ? "İşlem projeye uygulandı, ancak görünüm yenilenemedi. Ana çalışma alanını ve geri alma geçmişini kontrol edin: " + ex.Message
                : "İşlem uygulanmadı: " + ex.Message + (drawing.Count > 0 ? " · Önceki çizim noktaları korundu." : "");
            owner.ShowCanvasError(message);
            reportError?.Invoke(message);
        }
        finally
        {
            // Notify only after the action (or rollback) has completed. This avoids
            // exposing an invalid intermediate final click to recovery/UI state.
            if (previousDrawing is not null && !previousDrawing.SequenceEqual(drawing))
            {
                CancelLargeCompletion();
                owner.PendingDrawingChanged();
            }
        }
    }
    // Once a mutation has committed, unexpected ordinary UI failures must not
    // escape as if the edit did not happen. Keep catastrophic CLR failures out.
    private static bool IsNonFatal(Exception ex) => ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    private void ClearDrag() { dragId=null;dragVertex=-1;dragOriginal=null;dragPreview=null;dragScreenOrigin=null;dragOriginalGrainline=null;dragPreviewGrainline=null;dragSourceSnapshot=null;dragTranslation=default; }
    internal void CancelLargeCompletion() => largeCompletionCancellation?.Cancel();
    internal void CancelLargeVertexValidation() => largeVertexCancellation?.Cancel();
    public void CancelDrawing() { CancelLargeCompletion();CancelLargeVertexValidation();var changed=drawing.Count>0;drawing.Clear();ClearDrag();panStart=null;ReleaseMouseCapture();InvalidateVisual();if(changed)owner.PendingDrawingChanged(); }
    /// <summary>Removes one uncommitted drawing point without altering project history.</summary>
    internal void RemoveLastPendingPoint()
    {
        if(LargeVertexValidationRunning || LargeCompletionRunning)
        { owner.SetStatus("Doğrulama sürüyor; son noktayı kaldırmak için bitmesini bekleyin veya Esc ile iptal edin.");return; }
        if(dragId is not null || panStart is not null)
        { owner.SetStatus("Önce sürüklemeyi tamamlayın veya Esc ile iptal edin.");return; }
        if(drawing.Count==0 || owner.ActiveTool is not ("Çizgi" or "Dikdörtgen" or "Bézier" or "Yay" or "Çoklu çizgi"))return;
        drawing.RemoveAt(drawing.Count-1);
        owner.PendingDrawingChanged();
        owner.ClearCanvasError();
        InvalidateVisual();
        owner.SetStatus($"Son nokta kaldırıldı · {drawing.Count} nokta kaldı. {ToolGuidance(owner.ActiveTool)}");
    }
    // Coordinate entry has no live canvas pointer; after removing a point,
    // keep the rubber-band preview at the last retained vertex.
    internal void AnchorPreviewToLastPendingPoint()
    {
        cursor=drawing.Count>0 ? drawing[^1] : default;
        InvalidateVisual();
    }
    private void AddValidated(List<Point2> points,bool closed)
    {
        foreach(var point in points) Geometry.Validate(point);
        if(closed) Geometry.ValidateContour(points);
        else
        {
            // Drawing must obey the same open-path rules as later point edits
            // and joins. A sampled curve can double back or intersect itself.
            try { LargeOpenPathValidator.Validate(points); }
            catch (ArgumentException ex)
            { throw new ArgumentException("Açık çizgi sıfır uzunluklu, üst üste binen veya kesişen kenar içeriyor; çizim tamamlanmadı.", ex); }
        }
        owner.AddPiece(points,closed,()=> { observedCommits++;drawing.Clear(); });
    }
    public void FinishPolyline()
        => FinishPolylineCore(true);
    internal void FinishOpenPolyline()
        => FinishPolylineCore(false);
    private void FinishPolylineCore(bool closed)
    {
        if(owner.ActiveTool!="Çoklu çizgi" || drawing.Count<(closed ? 3 : 2))
        { owner.SetStatus(closed ? "Kapalı kontur için Çoklu çizgi aracında en az 3 nokta gerekir." : "Açık çizgi için Çoklu çizgi aracında en az 2 nokta gerekir.");return; }
        if(LargeCompletionRunning || LargeVertexValidationRunning) { owner.SetStatus("Çizim veya nokta doğrulaması sürüyor; lütfen bekleyin.");return; }
        if(drawing.Count<=Geometry.MaxValidatedContourVertices)
        { Safe(()=> { AddValidated([..drawing],closed);InvalidateVisual(); }, restoreDrawingOnFailure:true);return; }
        var points=drawing.ToArray();
        var document=owner.Document;
        var cancellation=new CancellationTokenSource();
        largeCompletionCancellation=cancellation;
        LargeCompletionTask=FinishLargePolylineAsync(points,document,cancellation,closed);
    }
    private async Task FinishLargePolylineAsync(Point2[] points, ProjectDocument document, CancellationTokenSource cancellation,bool closed)
    {
        owner.SetStatus("Büyük çizim arka planda doğrulanıyor. Dikiş payı ve serileme 4096; pastal 2000 noktayla sınırlıdır. HPGL iş yükü sınırı nedeniyle büyük veya çok parçalı iş reddedilebilir.");
        var committed=false;
        try
        {
            await Task.Run(()=> { if(closed)LargeContourValidator.Validate(points,cancellation.Token);else LargeOpenPathValidator.Validate(points,cancellation.Token); },cancellation.Token);
            // No await occurs between checking the snapshot and committing it.
            if(cancellation.IsCancellationRequested || owner.IsWindowClosed || owner.ActiveTool!="Çoklu çizgi" || !drawing.SequenceEqual(points))return;
            if(!ReferenceEquals(owner.Document,document))
            { owner.SetStatus("Doğrulama sırasında proje değişti; çizim korunuyor. Tamamlamak için yeniden deneyin.");return; }
            owner.AddPiece(points.ToList(),closed,()=> { committed=true;drawing.Clear(); });owner.ClearCanvasError();
            InvalidateVisual();
            owner.SetStatus("Büyük çizim tamamlandı. Dikiş payı ve serileme 4096; pastal 2000 noktayla sınırlıdır. HPGL iş yükü sınırı nedeniyle büyük veya çok parçalı iş reddedilebilir.");
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested && !committed) { }
        catch(OperationCanceledException ex) when(committed)
        { if(!owner.IsWindowClosed)owner.ShowCanvasError("Büyük çizim projeye eklendi, ancak görünüm yenilenemedi. Ana çalışma alanını ve geri alma geçmişini kontrol edin: "+ex.Message); }
        catch(Exception ex) when((committed && IsNonFatal(ex)) ||
            ex is ArgumentException or InvalidOperationException or ArithmeticException or System.IO.InvalidDataException)
        {
            if(!owner.IsWindowClosed && (committed || !cancellation.IsCancellationRequested))
                owner.ShowCanvasError(committed
                    ? "Büyük çizim projeye eklendi, ancak görünüm yenilenemedi. Ana çalışma alanını ve geri alma geçmişini kontrol edin: "+ex.Message
                    : "Büyük çizim tamamlanamadı: "+ex.Message+" · Önizleme ve kurtarma kaydı korundu.");
        }
        finally
        {
            if(ReferenceEquals(largeCompletionCancellation,cancellation))largeCompletionCancellation=null;
            cancellation.Dispose();
            if(committed && !owner.IsWindowClosed)owner.PendingDrawingChanged();
        }
    }
    public void Fit()
    {
        if(LargeVertexValidationRunning || LargeCompletionRunning)
        { owner.SetStatus("Doğrulama sürüyor; görünümü değiştirmek için bitmesini bekleyin.");return; }
        if(dragId is not null || panStart is not null)
        { owner.SetStatus("Önce sürüklemeyi tamamlayın veya Esc ile iptal edin.");return; }
        var width=ActualWidth;var height=ActualHeight;
        if(!double.IsFinite(width) || !double.IsFinite(height) || width<1 || height<1)
        { owner.SetStatus("Çalışma alanı henüz hazır değil; görünümü yeniden deneyin.");return; }
        double minX=double.PositiveInfinity,minY=double.PositiveInfinity,maxX=double.NegativeInfinity,maxY=double.NegativeInfinity;
        var found=false;
        foreach(var piece in owner.Document.Pieces)
        foreach(var point in piece.Points)
        {
            if(!point.IsFinite)
            { owner.SetStatus("Proje koordinatları geçersiz; görünüm değiştirilmedi.");return; }
            found=true;
            minX=Math.Min(minX,point.X);minY=Math.Min(minY,point.Y);
            maxX=Math.Max(maxX,point.X);maxY=Math.Max(maxY,point.Y);
        }
        // Grainlines can lie outside a contour, particularly when an edit
        // marked them for review. They must remain visible in an all-fit view.
        foreach(var piece in owner.Document.Pieces)
        {
            if(piece.Points.Count==0 || piece.Grainline is not { } grain)continue;
            foreach(var point in new[] { grain.Start,grain.End })
            {
                if(!point.IsFinite)
                { owner.SetStatus("İplik yönü koordinatları geçersiz; görünüm değiştirilmedi.");return; }
                minX=Math.Min(minX,point.X);minY=Math.Min(minY,point.Y);
                maxX=Math.Max(maxX,point.X);maxY=Math.Max(maxY,point.Y);
            }
        }
        // Coordinate entry can put an unfinished contour far from existing
        // pieces, or be the first drawing in an empty document. Include its
        // actual points without completing or changing the draft.
        foreach(var point in drawing)
        {
            if(!point.IsFinite)
            { owner.SetStatus("Çizim koordinatları geçersiz; görünüm değiştirilmedi.");return; }
            found=true;
            minX=Math.Min(minX,point.X);minY=Math.Min(minY,point.Y);
            maxX=Math.Max(maxX,point.X);maxY=Math.Max(maxY,point.Y);
        }
        if(!found) { owner.SetStatus("Gösterilecek parça veya çizim yok.");return; }
        var extentX=maxX-minX;var extentY=maxY-minY;
        if(!double.IsFinite(extentX) || !double.IsFinite(extentY))
        { owner.SetStatus("Proje sınırları hesaplanamadı; görünüm değiştirilmedi.");return; }
        var padding=Math.Min(55,Math.Min(width,height)*0.1);
        var availableWidth=width-2*padding;var availableHeight=height-2*padding;
        var fitX=extentX==0 ? double.PositiveInfinity : availableWidth/extentX;
        var fitY=extentY==0 ? double.PositiveInfinity : availableHeight/extentY;
        var requested=Math.Min(fitX,fitY);
        var newScale=double.IsPositiveInfinity(requested) ? 30 : Math.Clamp(requested,.02,30);
        var centerX=minX+extentX/2;var centerY=minY+extentY/2;
        var newOffset=new Vector(width/2-centerX*newScale,height/2-centerY*newScale);
        if(!double.IsFinite(newScale) || !double.IsFinite(newOffset.X) || !double.IsFinite(newOffset.Y))
        { owner.SetStatus("Proje görünümü hesaplanamadı; görünüm değiştirilmedi.");return; }
        scale=newScale;offset=newOffset;InvalidateVisual();
        owner.SetStatus(extentX*newScale>availableWidth || extentY*newScale>availableHeight
            ? "Proje ortalandı; en düşük yakınlık sınırı nedeniyle tamamı görünmeyebilir."
            : drawing.Count>0 ? "Parçalar ve tamamlanmamış çizim çalışma alanında gösteriliyor · Proje geometrisi değişmedi."
            : "Tüm parçalar çalışma alanında gösteriliyor · Proje geometrisi değişmedi.");
    }
    /// <summary>Frames the current selection without changing project geometry or history.</summary>
    internal void FitSelectedPiece()
    {
        if(LargeVertexValidationRunning || LargeCompletionRunning)
        { owner.SetStatus("Doğrulama sürüyor; görünümü değiştirmek için bitmesini bekleyin.");return; }
        if(dragId is not null || panStart is not null)
        { owner.SetStatus("Önce sürüklemeyi tamamlayın veya Esc ile iptal edin.");return; }
        var selected=owner.Selected;
        if(selected?.Points is not { Count: >0 } points)
        { owner.SetStatus("Göstermek için önce bir parça seçin.");return; }
        var width=ActualWidth;var height=ActualHeight;
        if(!double.IsFinite(width) || !double.IsFinite(height) || width<1 || height<1)
        { owner.SetStatus("Çalışma alanı henüz hazır değil; görünümü yeniden deneyin.");return; }
        double minX=double.PositiveInfinity,minY=double.PositiveInfinity,maxX=double.NegativeInfinity,maxY=double.NegativeInfinity;
        foreach(var point in points)
        {
            if(!point.IsFinite)
            { owner.SetStatus("Seçili parçanın koordinatları geçersiz; görünüm değiştirilmedi.");return; }
            minX=Math.Min(minX,point.X);minY=Math.Min(minY,point.Y);
            maxX=Math.Max(maxX,point.X);maxY=Math.Max(maxY,point.Y);
        }
        if(selected.Grainline is { } grain)
        {
            foreach(var point in new[] { grain.Start,grain.End })
            {
                if(!point.IsFinite)
                { owner.SetStatus("İplik yönü koordinatları geçersiz; görünüm değiştirilmedi.");return; }
                minX=Math.Min(minX,point.X);minY=Math.Min(minY,point.Y);
                maxX=Math.Max(maxX,point.X);maxY=Math.Max(maxY,point.Y);
            }
        }
        var extentX=maxX-minX;var extentY=maxY-minY;
        if(!double.IsFinite(extentX) || !double.IsFinite(extentY))
        { owner.SetStatus("Seçili parçanın sınırları hesaplanamadı; görünüm değiştirilmedi.");return; }
        var padding=Math.Min(55,Math.Min(width,height)*0.1);
        var availableWidth=width-2*padding;var availableHeight=height-2*padding;
        var fitX=extentX==0 ? double.PositiveInfinity : availableWidth/extentX;
        var fitY=extentY==0 ? double.PositiveInfinity : availableHeight/extentY;
        var requested=Math.Min(fitX,fitY);
        var newScale=double.IsPositiveInfinity(requested) ? 30 : Math.Clamp(requested,.02,30);
        var centerX=minX+extentX/2;var centerY=minY+extentY/2;
        var newOffset=new Vector(width/2-centerX*newScale,height/2-centerY*newScale);
        if(!double.IsFinite(newScale) || !double.IsFinite(newOffset.X) || !double.IsFinite(newOffset.Y))
        { owner.SetStatus("Seçili parçanın görünümü hesaplanamadı; görünüm değiştirilmedi.");return; }
        scale=newScale;offset=newOffset;InvalidateVisual();
        owner.SetStatus(extentX*newScale>availableWidth || extentY*newScale>availableHeight
            ? "Seçili parça ortalandı; en düşük yakınlık sınırı nedeniyle tamamı görünmeyebilir."
            : "Seçili parça çalışma alanında gösteriliyor · Proje geometrisi değişmedi.");
    }
    // View navigation changes only the display transform, never document geometry.
    internal void ZoomView(bool zoomIn)
    {
        if(dragId is not null || panStart is not null)
        { owner.SetStatus("Önce sürüklemeyi tamamlayın veya Esc ile iptal edin.");return; }
        if(ActualWidth<1 || ActualHeight<1)return;
        var center=new Point(ActualWidth/2,ActualHeight/2);
        var world=World(center,false);
        scale=Math.Clamp(scale*(zoomIn ? 1.15 : 1/1.15),.02,30);
        offset=new(center.X-world.X*scale,center.Y-world.Y*scale);
        InvalidateVisual();
        owner.SetStatus($"Görünüm yakınlığı: {scale*100:0}% · Parça geometrisi değişmedi");
    }
    internal void PanView(double dx,double dy)
    {
        if(dragId is not null || panStart is not null)
        { owner.SetStatus("Önce sürüklemeyi tamamlayın veya Esc ile iptal edin.");return; }
        offset=new Vector(Math.Clamp(offset.X+dx,-1e12,1e12),Math.Clamp(offset.Y+dy,-1e12,1e12));
        InvalidateVisual();
        owner.SetStatus("Görünüm kaydırıldı · Parça geometrisi değişmedi");
    }
    private Point Screen(Point2 p)=>new(p.X*scale+offset.X,p.Y*scale+offset.Y);
    private Point2 World(Point p,bool snap=true) { var w=new Point2((p.X-offset.X)/scale,(p.Y-offset.Y)/scale); return snap && owner.Snap ? new(Math.Round(w.X),Math.Round(w.Y)) : w; }
    private void Down(object sender,MouseButtonEventArgs e)
    {
        if(LargeVertexValidationRunning || LargeCompletionRunning) { owner.SetStatus("Doğrulama sürüyor; bekleyin veya Esc ile iptal edin.");return; }
        Focus();
        if(e.ChangedButton==MouseButton.Middle)
        {
            if(dragId is not null) { owner.SetStatus("Önce sürüklemeyi tamamlayın veya Esc ile iptal edin.");return; }
            if(panStart is null) { panStart=e.GetPosition(this);CaptureMouse(); }
            return;
        }
        if(e.ChangedButton!=MouseButton.Left)return;
        if(panStart is not null || dragId is not null)
        { owner.SetStatus("Önce geçerli fare hareketini tamamlayın veya Esc ile iptal edin.");return; }
        var screenPosition=e.GetPosition(this);
        // Selection is based on what is visibly under the pointer. Grid snapping
        // applies to editing, not hit testing of fractional imported coordinates.
        var p=World(screenPosition,owner.ActiveTool!="Seç");
        Geometry.Validate(p);
        if(owner.ActiveTool=="Seç")
        {
            var (piece,vertex,ambiguous)=ResolvePointerSelection(p);
            if(ambiguous)
            {
                owner.ShowCanvasError("Birden fazla nokta aynı uzaklıkta olabilir veya noktanın üzeri başka bir parçayla örtülüdür. Yanlış noktayı taşımamak için yakınlaştırın veya farklı bir konumdan seçin.");
                return;
            }
            owner.Select(piece?.Id);
            if(piece is not null && owner.HasPropertyDraftFor(piece.Id))
            { owner.SetStatus("Parça taşınmadı · Önce bu parçanın ad ve konum taslağını uygulayın veya temizleyin.");return; }
            if(vertex>=0 && piece?.Notches is not null)
            {
                if(!CanDragMarkedVertex(piece,vertex))return;
            }
            if(piece is not null) { dragSourceSnapshot=piece.Clone();dragId=piece.Id;dragVertex=vertex;dragOrigin=World(screenPosition);dragScreenOrigin=screenPosition;dragOriginal=[..piece.Points];dragPreview=[..piece.Points];dragOriginalGrainline=piece.Grainline?.Clone();dragPreviewGrainline=dragOriginalGrainline?.Clone();dragTranslation=default;CaptureMouse(); }
            return;
        }
        AddDrawingPointCore(p);
    }
    /// <summary>Adds an explicit millimetre coordinate without grid snapping. Returns null on success.</summary>
    internal string? AddDrawingPointAt(Point2 point)
    {
        if(LargeVertexValidationRunning || LargeCompletionRunning)
            return "Doğrulama sürüyor; bitmesini bekleyin veya Esc ile iptal edin.";
        if(dragId is not null || panStart is not null)
            return "Önce sürüklemeyi tamamlayın veya Esc ile iptal edin.";
        if(owner.ActiveTool is not ("Çizgi" or "Dikdörtgen" or "Bézier" or "Yay" or "Çoklu çizgi"))
            return "Nokta eklemek için önce bir çizim aracı seçin.";
        var before=drawing.ToArray();
        var beforeDocument=owner.Document;
        var beforeCommits=observedCommits;
        string? error=null;
        try
        {
            Safe(()=> { Geometry.Validate(point); AddDrawingPointCore(point); },restoreDrawingOnFailure:true,reportError:message=>error=message);
        }
        catch(Exception ex) when(IsNonFatal(ex))
        {
            // Safe's final recovery notification can itself fail after the
            // point or completed piece changed. Never invite a blind retry.
            if(observedCommits!=beforeCommits || !ReferenceEquals(beforeDocument,owner.Document))
                return "Çizim projeye uygulanmış olabilir, ancak durum güncellenemedi. Yeniden denemeden önce çalışma alanını ve geri alma geçmişini kontrol edin: " + ex.Message;
            if(!before.SequenceEqual(drawing))
                return "Çizim önizlemesi değişmiş olabilir, ancak durum güncellenemedi. Yeniden denemeden önce çizim noktalarını kontrol edin: " + ex.Message;
            return "Nokta işleminin durumu doğrulanamadı. Yeniden denemeden önce çalışma alanını kontrol edin: " + ex.Message;
        }
        if(error is not null)return error;
        if(beforeCommits==observedCommits && ReferenceEquals(beforeDocument,owner.Document) && before.SequenceEqual(drawing))
            return "Nokta eklenmedi. Önceki noktadan farklı ve seçilen araç için geçerli bir konum girin.";
        // The mouse may be elsewhere while a coordinate dialog adds a point.
        // Anchor the live preview to the accepted point until the next mouse move.
        if(drawing.Count>0) { cursor=drawing[^1];InvalidateVisual(); }
        return null;
    }
    // Shared with mouse input. The caller handles the source's coordinate
    // conversion and wraps this in Safe(..., restoreDrawingOnFailure: true).
    private void AddDrawingPointCore(Point2 p)
    {
        if(owner.ActiveTool=="Bézier")
        {
            drawing.Add(p);
            if(drawing.Count==4) AddValidated(Geometry.SampleCubicBezier(drawing[0],drawing[1],drawing[2],drawing[3],64),false);
            else owner.SetStatus(ToolGuidance(owner.ActiveTool));
            InvalidateVisual();return;
        }
        if(owner.ActiveTool=="Yay")
        {
            drawing.Add(p);
            if(drawing.Count==3) AddValidated(Arc(drawing[0],drawing[1],drawing[2]),false);
            else owner.SetStatus(ToolGuidance(owner.ActiveTool));
            InvalidateVisual();return;
        }
        if(owner.ActiveTool=="Çoklu çizgi")
        {
            if(drawing.Count==0 || Geometry.Distance(drawing[^1],p)>.001)
            {
                // FinishPolyline uses the bounded synchronous validator through
                // 4096 points and the background large-contour validator beyond
                // that. Keep input aligned with its actual stored-point limit.
                if(drawing.Count>=LargeContourValidator.MaximumStoredPoints)
                    throw new System.IO.InvalidDataException($"Çoklu çizgi en fazla {LargeContourValidator.MaximumStoredPoints:N0} nokta içerebilir. Çizimi tamamlayın veya iptal edin.");
                drawing.Add(p);
            }
        }
        else if(drawing.Count==0)drawing.Add(p);
        else { var first=drawing[0]; if(Geometry.Distance(first,p)<.001)return;
            if(owner.ActiveTool=="Dikdörtgen") { if(Math.Abs(first.X-p.X)<.001 || Math.Abs(first.Y-p.Y)<.001)return; AddValidated([first,new(p.X,first.Y),p,new(first.X,p.Y)],true); }
            else AddValidated([first,p],false); }
        if(drawing.Count>0)owner.SetStatus(ToolGuidance(owner.ActiveTool));
        InvalidateVisual();
    }
    // Match paint order: a later piece covers the selected piece's handles.
    // Only consider ambiguity among selected handles when no later piece is hit.
    internal (PatternPiece? Piece,int Vertex,bool Ambiguous) ResolvePointerSelection(Point2 pointer)
    {
        var pieces=owner.Document.Pieces;
        var selected=owner.Selected;
        if(selected is not null)
        {
            var selectedIndex=pieces.FindIndex(item=>item.Id==selected.Id);
            if(selectedIndex>=0)
            {
                for(var i=pieces.Count-1;i>selectedIndex;i--)
                    if(HitVisibleOverlay(pieces[i],pointer))return (pieces[i],-1,false);
            }
            var (vertex,ambiguous)=FindNearestVertex(selected,pointer,scale);
            if(ambiguous)return (null,-1,true);
            if(vertex>=0)
            {
                // The pointer may lie outside an upper shape while still falling
                // within the eight-DIP picking radius of a hidden lower handle.
                // Be conservative if the handle centre is covered: do not infer
                // that an obscured vertex was the user's intended target.
                if(selectedIndex>=0)
                    for(var i=pieces.Count-1;i>selectedIndex;i--)
                        if(HitVisibleOverlay(pieces[i],selected.Points[vertex]))return (null,-1,true);
                return (selected,vertex,false);
            }
        }
        return (pieces.LastOrDefault(item=>Hit(item,pointer)),-1,false);
    }
    // Mouse picking is screen-based: choose the closest visible vertex, not
    // the first vertex in storage order. A duplicate final closing vertex is
    // the same logical vertex; any other equal-distance tie remains ambiguous.
    private static (int Index,bool Ambiguous) FindNearestVertex(PatternPiece piece,Point2 pointer,double scale)
    {
        var count=piece.Points.Count;
        if(piece.Closed && count>1 && piece.Points[0]==piece.Points[^1])count--;
        var best=-1;var bestDistance=double.PositiveInfinity;var ambiguous=false;
        const double tiePixels=1e-7;
        for(var i=0;i<count;i++)
        {
            var distance=Geometry.Distance(piece.Points[i],pointer)*scale;
            if(distance>8)continue;
            if(distance<bestDistance-tiePixels) { best=i;bestDistance=distance;ambiguous=false; }
            else if(Math.Abs(distance-bestDistance)<=tiePixels)ambiguous=true;
        }
        return (best,ambiguous);
    }
    private void Move(object sender,MouseEventArgs e)
    {
        var pos=e.GetPosition(this);var panned=false;
        if(panStart is {} start) { panned=pos!=start;offset+=pos-start;panStart=pos; }
        var previousCursor=cursor;cursor=World(pos);
        var previewChanged=false;
        if(dragOriginal is not null && e.LeftButton==MouseButtonState.Pressed)
        {
            var previousPreview=dragPreview;
            UpdateDragPreview(pos);
            previewChanged=previousPreview is null ? dragPreview is not null : dragPreview is null || !previousPreview.SequenceEqual(dragPreview);
        }
        if(!LargeVertexValidationRunning && !LargeCompletionRunning) owner.SetStatus($"{ToolGuidance(owner.ActiveTool)}  ·  X {cursor.X:0.##} mm  Y {cursor.Y:0.##} mm  ·  Yakınlaşma {scale*100:0}%");
        if(ShouldInvalidatePointerMove(panned,previewChanged,drawing.Count>0,previousCursor,cursor))InvalidateVisual();
    }
    // A free pointer is not drawn; its coordinate text updates without rebuilding all contours.
    internal static bool ShouldInvalidatePointerMove(bool panned,bool dragPreviewChanged,bool drawingActive,Point2 previousCursor,Point2 cursor)
        => panned || dragPreviewChanged || (drawingActive && previousCursor!=cursor);
    // Shared by the mouse path and background verification. Coordinates are
    // screen-space DIPs, as are the system drag thresholds.
    internal void UpdateDragPreview(Point pos)
    {
        if(dragOriginal is null)return;
        var dragPosition=World(pos);
        // Normal mouse jitter during a click must not snap an imported
        // fractional vertex to the grid. Returning inside this same
        // rectangle restores the original preview, even after dragging.
        if(dragScreenOrigin is { } startPosition &&
            Math.Abs(pos.X-startPosition.X)<SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y-startPosition.Y)<SystemParameters.MinimumVerticalDragDistance)
        { dragPreview=[..dragOriginal];dragPreviewGrainline=dragOriginalGrainline?.Clone();dragTranslation=default; }
        else if(dragVertex>=0)
        {
            var piece=owner.Document.Pieces.FirstOrDefault(p=>p.Id==dragId);
            // A handle can be picked up to eight DIPs from its centre. Preserve
            // that grab offset instead of jumping the vertex to the pointer.
            var vertexPosition=dragScreenOrigin is { } origin
                ? new Point2(dragOriginal[dragVertex].X+(pos.X-origin.X)/scale,dragOriginal[dragVertex].Y+(pos.Y-origin.Y)/scale)
                : dragPosition;
            if(owner.Snap)vertexPosition=new Point2(Math.Round(vertexPosition.X),Math.Round(vertexPosition.Y));
            dragPreview=PreviewVertexDrag(dragOriginal,dragVertex,vertexPosition,piece?.Closed == true);
            dragPreviewGrainline=dragOriginalGrainline?.Clone();
            if(dragPreviewGrainline is not null && !dragPreview.SequenceEqual(dragOriginal))dragPreviewGrainline.NeedsReview=true;
        }
        else
        {
            dragTranslation=new(dragPosition.X-dragOrigin.X,dragPosition.Y-dragOrigin.Y);
            dragPreview=dragSourceSnapshot is null ? Geometry.Translate(dragOriginal,dragTranslation.X,dragTranslation.Y)
                : MarkedPieceTranslation.Translate(dragSourceSnapshot,dragTranslation.X,dragTranslation.Y).Points;
            dragPreviewGrainline=GrainlineTransform.Translate(dragOriginalGrainline,dragOriginal[0],dragPreview[0],dragTranslation.X,dragTranslation.Y);
        }
    }
    private static bool SamePointBits(IReadOnlyList<Point2> first,IReadOnlyList<Point2> second) =>
        first.Count==second.Count && first.Zip(second).All(pair=>BitConverter.DoubleToInt64Bits(pair.First.X)==BitConverter.DoubleToInt64Bits(pair.Second.X) && BitConverter.DoubleToInt64Bits(pair.First.Y)==BitConverter.DoubleToInt64Bits(pair.Second.Y));
    internal static bool CanDragMarkedVertex(PatternPiece piece,int vertex)
    {
        if(vertex<0 || vertex>=piece.Points.Count)return false;
        return true;
    }
    private void Up(MouseButtonEventArgs e)
    {
        if(e.ChangedButton==MouseButton.Middle)
        {
            // A middle-button release must not release a left-drag capture.
            if(panStart is not null) { panStart=null;ReleaseMouseCapture(); }
            return;
        }
        if(e.ChangedButton!=MouseButton.Left || dragId is null)return;
        var piece=owner.Document.Pieces.FirstOrDefault(p=>p.Id==dragId);
        var result=dragPreview;
        if(piece is not null && dragOriginal is not null && (!piece.Points.SequenceEqual(dragOriginal) || !GrainlineTransform.Same(piece.Grainline,dragOriginalGrainline)))
            throw new InvalidOperationException("Sürükleme sırasında parça değişti. İşlemi yeniden başlatın.");
        if(piece is not null && result is not null && dragOriginal is not null && !result.SequenceEqual(dragOriginal) && piece.Notches is not null && (dragSourceSnapshot is null || (dragVertex>=0 && !CanDragMarkedVertex(piece,dragVertex))))
        { ClearDrag();ReleaseMouseCapture();InvalidateVisual();owner.ShowCanvasError("İşaretli parça değiştirilemez; işlem uygulanmadı.");return; }
        if(piece is not null && result is not null && dragOriginal is not null && !result.SequenceEqual(dragOriginal))
        {
            if(owner.HasPropertyDraftFor(piece.Id))
            {
                ClearDrag();ReleaseMouseCapture();InvalidateVisual();
                owner.SetStatus("Sürükleme uygulanmadı · Bu parçaya ait ad veya konum taslağı sürükleme sırasında oluşturuldu. Taslak ve önceki geometri korundu.");
                return;
            }
            foreach(var p in result) Geometry.Validate(p);
            if(dragVertex>=0) ValidateClosingEdgePreserved(dragOriginal,result,piece.Closed);
            if(dragVertex>=0 && result.Count>Geometry.MaxValidatedContourVertices)
            {
                if(dragSourceSnapshot is not null && !DocumentSession.ContentEquals(new ProjectDocument { Pieces=[piece] },new ProjectDocument { Pieces=[dragSourceSnapshot] }))
                    throw new InvalidOperationException("Sürükleme sırasında kaynak veya işaretler değişti; işlem uygulanmadı.");
                StartLargeVertexValidation(piece.Id,piece.Closed,dragOriginal,result,dragVertex);
                ClearDrag();ReleaseMouseCapture();InvalidateVisual();return;
            }
            List<NotchMark>? verifiedVertexMarks=null;
            if(dragVertex>=0)
            {
                if(piece.Notches is not null)
                {
                    var verified=PatternTools.MovePoint(piece,dragVertex,result[dragVertex],allowMarkedVertexMove:true);
                    if(!SamePointBits(verified.Points,result))throw new InvalidOperationException("Nokta önizlemesi doğrulanan konturla eşleşmiyor.");
                    verifiedVertexMarks=verified.Notches;
                }
                else ValidateDraggedVertex(result,piece.Closed);
            }
            if(dragSourceSnapshot is not null && !OpenPathSourceSnapshot.Matches(piece,dragSourceSnapshot))
                throw new InvalidOperationException("Sürükleme sırasında kaynak veya işaretler değişti; işlem uygulanmadı.");
            var snapshot=result.ToList();
            List<NotchMark>? movedNotches=null;
            if(dragVertex<0)
            {
                var verified=MarkedPieceTranslation.Translate(piece,dragTranslation.X,dragTranslation.Y);
                if(!verified.Points.SequenceEqual(snapshot))throw new InvalidOperationException("Sürükleme önizlemesi doğrulanan ötelemeyle eşleşmiyor.");
                movedNotches=verified.Notches;
            }
            var newGrain=dragVertex>=0 ? dragOriginalGrainline?.Clone() : GrainlineTransform.Translate(dragOriginalGrainline,dragOriginal[0],snapshot[0],dragTranslation.X,dragTranslation.Y);
            if(dragVertex>=0 && newGrain is not null)newGrain.NeedsReview=true;
            var rule=owner.Document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==piece.Id);
            var translatedRule=dragVertex<0 ? rule?.TranslatedFor(piece,snapshot,dragTranslation.X,dragTranslation.Y) : null;
            owner.CommitObserved(dragVertex>=0 ? "Kontur noktası taşındı" : "Parça taşındı",()=> {
                piece.Points=snapshot;piece.Grainline=newGrain;if(dragVertex<0)piece.Notches=movedNotches;else if(verifiedVertexMarks is not null)piece.Notches=verifiedVertexMarks;
                if(translatedRule is not null)owner.Document.GradingRules[owner.Document.GradingRules.IndexOf(rule!)]=translatedRule;
            },()=>observedCommits++);
            if(rule is not null && translatedRule is null)
                owner.ShowCanvasError("Kontur değiştirildi; kayıtlı serileme kuralı önceki kontura ait. Serilemeden önce kuralı gözden geçirin.");
            else owner.ClearCanvasError();
        }
        ClearDrag();ReleaseMouseCapture();InvalidateVisual();
    }

    /// <summary>Validates a detached large vertex proposal; no live geometry changes until the worker succeeds.</summary>
    internal void StartLargeVertexValidation(Guid id, bool closed, IReadOnlyList<Point2> original, IReadOnlyList<Point2> proposed,int vertexIndex=-1)
    {
        if(LargeVertexValidationRunning) { owner.SetStatus("Nokta değişikliği zaten doğrulanıyor; lütfen bekleyin.");return; }
        var piece=owner.Document.Pieces.FirstOrDefault(p=>p.Id==id);
        if(piece is null || piece.Closed!=closed || !piece.Points.SequenceEqual(original) || owner.SelectedId!=id || owner.ActiveTool!="Seç")
            throw new InvalidOperationException("Parça veya seçim değişti; nokta taşımasını yeniden başlatın.");
        if(owner.HasPropertyDraftFor(id))
            throw new InvalidOperationException("Önce bu parçanın ad ve konum taslağını uygulayın veya temizleyin.");
        if(piece.Notches is not null && (vertexIndex<0 || vertexIndex>=piece.Points.Count || !CanDragMarkedVertex(piece,vertexIndex)))
            throw new InvalidOperationException("İşaret koordinat bitleri kaynak köşeyle eşleşmiyor; işlem uygulanmadı.");
        if(proposed.Count!=original.Count || proposed.Count<=Geometry.MaxValidatedContourVertices || proposed.Count>LargeOpenPathValidator.MaximumStoredPoints)
            throw new ArgumentException("Büyük nokta değişikliği için nokta sayısı geçersiz.");
        ValidateClosingEdgePreserved(original,proposed,closed);
        var source=original.ToArray();var candidate=proposed.ToArray();var document=owner.Document;var originalGrain=piece.Grainline?.Clone();var markedSource=piece.Notches is null ? null : piece.Clone();
        var cancellation=new CancellationTokenSource();largeVertexCancellation=cancellation;
        LargeVertexValidationTask=ValidateLargeVertexAsync(id,closed,source,candidate,document,originalGrain,markedSource,vertexIndex,cancellation);
    }

    private async Task ValidateLargeVertexAsync(Guid id,bool closed,Point2[] source,Point2[] candidate,ProjectDocument document,Grainline? originalGrain,PatternPiece? markedSource,int vertexIndex,CancellationTokenSource cancellation)
    {
        owner.SetStatus("Büyük parçanın nokta değişikliği doğrulanıyor; ekranda kayıtlı geometri var. Bekleyin veya Esc ile iptal edin.");
        var committed=false;
        try
        {
            var before=BeforeLargeVertexValidationForVerification;
            var verifiedMarks=await Task.Run(()=> { before?.Invoke();cancellation.Token.ThrowIfCancellationRequested();if(markedSource is not null) { var verified=PatternTools.MovePoint(markedSource,vertexIndex,candidate[vertexIndex],cancellation.Token,allowMarkedVertexMove:true);if(!SamePointBits(verified.Points,candidate))throw new InvalidOperationException("Nokta önizlemesi doğrulanan konturla eşleşmiyor.");return verified.Notches; } if(closed)LargeContourValidator.Validate(candidate,cancellation.Token);else LargeOpenPathValidator.Validate(candidate,cancellation.Token);return null; },cancellation.Token);
            if(cancellation.IsCancellationRequested || owner.IsWindowClosed)return;
            if(owner.ActiveTool!="Seç" || owner.SelectedId!=id || !ReferenceEquals(owner.Document,document))
            { owner.SetStatus("Proje veya seçim değişti; doğrulanan nokta önerisi uygulanmadı. Önceki geometri korundu.");return; }
            if(owner.HasPropertyDraftFor(id))
            { owner.SetStatus("Nokta değişikliği uygulanmadı · Bu parçaya ait ad veya konum taslağı doğrulama sırasında oluşturuldu. Taslak ve önceki geometri korundu.");return; }
            var current=owner.Document.Pieces.FirstOrDefault(p=>p.Id==id);
            if(current is null || (markedSource is null ? current.Notches is not null : !DocumentSession.ContentEquals(new ProjectDocument { Pieces=[current] },new ProjectDocument { Pieces=[markedSource] })) || current.Closed!=closed || !current.Points.SequenceEqual(source) || !GrainlineTransform.Same(current.Grainline,originalGrain))
            { owner.SetStatus("Parça değişti; doğrulanan nokta önerisi uygulanmadı. Önceki geometri korundu.");return; }
            // Check and commit without yielding to avoid applying a stale worker result.
            owner.CommitObserved("Büyük parçanın noktası taşındı",()=>
            {
                var target=owner.Document.Pieces.First(p=>p.Id==id);
                target.Points=candidate.ToList();if(verifiedMarks is not null)target.Notches=verifiedMarks;
                if(target.Grainline is not null)target.Grainline.NeedsReview=true;
            },()=>committed=true);
            if(owner.Document.GradingRules.Any(rule=>rule.SourcePieceId==id))
                owner.ShowCanvasError("Büyük parçanın noktası taşındı; kayıtlı serileme kuralı önceki kontura ait. Serilemeden önce kuralı gözden geçirin.");
            else
            {
                owner.ClearCanvasError();
                owner.SetStatus("Büyük parçanın noktası taşındı · Geri al ile önceki duruma dönebilirsiniz.");
            }
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested && !committed) { }
        catch(OperationCanceledException ex) when(committed)
        { if(!owner.IsWindowClosed)owner.ShowCanvasError("Nokta değişikliği projeye uygulandı, ancak görünüm yenilenemedi. Ana çalışma alanını ve geri alma geçmişini kontrol edin: "+ex.Message); }
        catch(Exception ex) when((committed && IsNonFatal(ex)) ||
            ex is ArgumentException or InvalidOperationException or ArithmeticException or System.IO.InvalidDataException)
        { if(!owner.IsWindowClosed && (committed || !cancellation.IsCancellationRequested))owner.ShowCanvasError(committed
            ? "Nokta değişikliği projeye uygulandı, ancak görünüm yenilenemedi. Ana çalışma alanını ve geri alma geçmişini kontrol edin: "+ex.Message
            : "Nokta değişikliği uygulanmadı: "+ex.Message+" · Önceki geometri korundu."); }
        finally
        {
            if(ReferenceEquals(largeVertexCancellation,cancellation))largeVertexCancellation=null;
            cancellation.Dispose();InvalidateVisual();
        }
    }
    private static void ValidateClosingEdgePreserved(IReadOnlyList<Point2> original,IReadOnlyList<Point2> proposed,bool closed)
    {
        if(closed && original.Count>1 && proposed.Count==original.Count && original[0]!=original[^1] && proposed[0]==proposed[^1])
            throw new ArgumentException("Nokta taşıması kapanış kenarını sıfıra indiriyor; kontur değiştirilmedi.");
    }
    // An explicit closing point is the same geometric vertex as the first point.
    internal static List<Point2> PreviewVertexDrag(IReadOnlyList<Point2> original,int vertex,Point2 position,bool closed)
    {
        Geometry.Validate(position);
        if(vertex<0 || vertex>=original.Count)throw new ArgumentOutOfRangeException(nameof(vertex));
        var result=original.ToList();result[vertex]=position;
        if(closed && original.Count>1 && original[0]==original[^1] && (vertex==0 || vertex==original.Count-1))
        { result[0]=position;result[^1]=position; }
        return result;
    }
    // Persistence accepts draft geometry, so vertex editing must enforce the
    // same bounded open-path integrity checks as the pattern editing tools.
    internal static void ValidateDraggedVertex(IReadOnlyList<Point2> points,bool closed)
    {
        if(closed) { Geometry.ValidateContour(points); return; }
        if(points.Count<2 || points.Count>Geometry.MaxValidatedContourVertices)
            throw new ArgumentException($"Açık çizgi düzenlemesi 2–{Geometry.MaxValidatedContourVertices} nokta destekler.");
        if(Geometry.HasSelfIntersections(points,false))
            throw new ArgumentException("Nokta taşınamadı: açık çizgi sıfır uzunluklu, üst üste binen veya kesişen bir kenar oluşturuyor.");
    }
    private static List<Point2> Arc(Point2 center,Point2 start,Point2 end)
    {
        var radius=Geometry.Distance(center,start);
        if(radius<.001 || Geometry.Distance(center,end)<.001)throw new ArgumentException("Yay yarıçapı ve bitiş yönü sıfır olamaz.");
        var a=Math.Atan2(start.Y-center.Y,start.X-center.X);var b=Math.Atan2(end.Y-center.Y,end.X-center.X);
        var sweep=(b-a+2*Math.PI)%(2*Math.PI);
        if(sweep<1e-6)throw new ArgumentException("Yayın başlangıç ve bitiş yönleri farklı olmalıdır.");
        var points=Enumerable.Range(0,65).Select(i=>new Point2(center.X+radius*Math.Cos(a+sweep*i/64),center.Y+radius*Math.Sin(a+sweep*i/64))).ToList();
        // Reconstructing the first point through trigonometry can move the
        // selected start by an ulp, separating it from an adjoining contour.
        points[0]=start;
        // The last click specifies a direction, not necessarily a point on
        // the circle. Preserve it only when its radius is exactly the same in
        // the represented coordinates; rounded double distances can coincide
        // even when the actual radii differ.
        static System.Numerics.BigInteger Units(double value)
        {
            var bits=BitConverter.DoubleToInt64Bits(value);var exponent=(int)((bits>>52)&0x7ff);
            var fraction=bits&0x000f_ffff_ffff_ffffL;
            var mantissa=exponent==0 ? fraction : fraction|(1L<<52);
            var units=new System.Numerics.BigInteger(mantissa) << (exponent==0 ? 0 : exponent-1);
            return bits<0 ? -units : units;
        }
        var cx=Units(center.X);var cy=Units(center.Y);
        var sx=Units(start.X)-cx;var sy=Units(start.Y)-cy;
        var ex=Units(end.X)-cx;var ey=Units(end.Y)-cy;
        if(sx*sx+sy*sy==ex*ex+ey*ey)points[^1]=end;
        return points;
    }
    private void Wheel(object sender,MouseWheelEventArgs e) { ZoomAt(e.GetPosition(this),e.Delta);e.Handled=true; }
    internal void ZoomAt(Point p,int delta)
    {
        if(dragId is not null || panStart is not null)
        { owner.SetStatus("Önce sürüklemeyi tamamlayın veya Esc ile iptal edin.");return; }
        var w=World(p,false);scale=Math.Clamp(scale*Math.Pow(1.15,delta/120.0),.02,30);offset=new(p.X-w.X*scale,p.Y-w.Y*scale);InvalidateVisual();
    }
    // Later pieces are drawn unselected, with opaque closed fills and 1.5-DIP
    // strokes. Use that footprint (plus half a DIP for antialiasing) only when
    // deciding whether a visible selected handle is covered. The ordinary
    // eight-DIP selection tolerance remains in Hit below.
    private bool HitVisibleOverlay(PatternPiece piece,Point2 point)
    {
        if(piece.Points.Count==0)return false;
        if(piece.Closed && Geometry.PointInPolygon(point,piece.Points))return true;
        var tolerance=1.25/scale;
        for(var i=0;i<piece.Points.Count-(piece.Closed?0:1);i++)
        {
            var a=piece.Points[i];var b=piece.Points[(i+1)%piece.Points.Count];
            var dx=b.X-a.X;var dy=b.Y-a.Y;var length=dx*dx+dy*dy;
            var t=length==0?0:Math.Clamp(((point.X-a.X)*dx+(point.Y-a.Y)*dy)/length,0,1);
            if(Geometry.Distance(point,new(a.X+t*dx,a.Y+t*dy))<=tolerance)return true;
        }
        return false;
    }
    private bool Hit(PatternPiece p,Point2 point) { if(p.Points.Count==0)return false; if(p.Closed && Geometry.PointInPolygon(point,p.Points))return true; for(var i=0;i<p.Points.Count-(p.Closed?0:1);i++) { var a=p.Points[i];var b=p.Points[(i+1)%p.Points.Count];var dx=b.X-a.X;var dy=b.Y-a.Y;var length=dx*dx+dy*dy;var t=length==0?0:Math.Clamp(((point.X-a.X)*dx+(point.Y-a.Y)*dy)/length,0,1);if(Geometry.Distance(point,new(a.X+t*dx,a.Y+t*dy))<8/scale)return true; } return false; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(B("#0D1827"),null,new Rect(RenderSize));
        var step=10.0;while(step*scale<18)step*=5;while(step*scale>120)step/=2;
        var gridPen=new Pen(B("#203047"),.5);var origin=World(new Point(0,0),false);var end=World(new Point(ActualWidth,ActualHeight),false);
        for(var gx=Math.Floor(origin.X/step)*step;gx<=end.X;gx+=step) {var sx=Screen(new(gx,0)).X;dc.DrawLine(gridPen,new(sx,0),new(sx,ActualHeight));}
        for(var gy=Math.Floor(origin.Y/step)*step;gy<=end.Y;gy+=step) {var sy=Screen(new(0,gy)).Y;dc.DrawLine(gridPen,new(0,sy),new(ActualWidth,sy));}
        var zero=Screen(new(0,0));var axes=new Pen(B("#43617B"),1);dc.DrawLine(axes,new(zero.X,0),new(zero.X,ActualHeight));dc.DrawLine(axes,new(0,zero.Y),new(ActualWidth,zero.Y));
        Brush? selectedBrush=null,normalStroke=null,selectedFill=null,normalFill=null,normalLabel=null;
        Pen? selectedPen=null,normalPen=null;
        foreach(var p in owner.Document.Pieces)
        {
            if(p.Points.Count==0)continue;var selected=p.Id==owner.SelectedId;
            var pen=selected ? (selectedPen ??= new Pen(selectedBrush ??= B("#FFCB70"),2.3))
                : (normalPen ??= new Pen(normalStroke ??= B("#4DE1C1"),1.5));
            var visible=p.Id==dragId && dragPreview is not null ? dragPreview : p.Points;
            var fill=p.Closed ? selected ? (selectedFill ??= B("#304947")) : (normalFill ??= B("#163D40")) : null;
            DrawContour(dc,visible,p.Closed,fill,pen);
            if(p.Notches is not null)
            {
                var notchBrush=B("#FF8FAF");var notchPen=new Pen(notchBrush,2);
                foreach(var notch in p.Notches)
                {
                    var notchPosition=p.Id==dragId && dragPreview is not null && notch.VertexIndex>=0 && notch.VertexIndex<visible.Count && (dragVertex<0 || notch.VertexIndex==dragVertex || (p.Closed && dragVertex==p.Points.Count-1 && notch.VertexIndex==0 && p.Points[0]==p.Points[^1])) ? visible[notch.VertexIndex] : notch.Position;
                    var point=Screen(notchPosition);
                    if(!MarkerCanAffectViewport(point,RenderSize))continue;
                    dc.DrawEllipse(null,notchPen,point,7,7);
                    if(selected)Text(dc,$"İşaret {notch.VertexIndex}",new Point(point.X+8,point.Y-15),notchBrush,11);
                }
            }
            var grain=p.Id==dragId && dragPreview is not null ? dragPreviewGrainline : p.Grainline;
            if(grain is not null)
            {
                var grainBrush=grain.NeedsReview ? B("#FFC468") : B("#65CFFF");
                var grainPen=new Pen(grainBrush,2) { DashStyle=grain.NeedsReview ? DashStyles.Dash : DashStyles.Solid };
                var a=Screen(grain.Start);var b=Screen(grain.End);dc.DrawLine(grainPen,a,b);
                dc.DrawEllipse(grainBrush,null,a,3,3);dc.DrawEllipse(grainBrush,null,b,3,3);
                if(selected)Text(dc,grain.NeedsReview ? "İplik yönü · incele" : "İplik yönü",new Point(a.X+5,a.Y+5),grainBrush,11);
            }
            var bounds=Geometry.Bounds(visible);var label=Screen(new(bounds.MinX,bounds.MinY)); Text(dc,CanvasPieceLabel(p.Name),new Point(label.X+7,label.Y-23),selected ? (selectedBrush ??= B("#FFCB70")) : (normalLabel ??= B("#B3D2D5")),12);
            if(selected)
            {
                var handleBrush=selectedBrush ??= B("#FFCB70");
                foreach(var v in visible)dc.DrawEllipse(handleBrush,null,Screen(v),3.4,3.4);
            }
        }
        if(drawing.Count>0) { List<Point2> preview=owner.ActiveTool=="Dikdörtgen" ? [drawing[0],new(cursor.X,drawing[0].Y),cursor,new(drawing[0].X,cursor.Y)] : [..drawing,cursor]; DrawContour(dc,preview,owner.ActiveTool=="Dikdörtgen",null,new Pen(B("#FFCB70"),1.8) { DashStyle=DashStyles.Dash }); }
        try
        {
            if(owner.ActiveTool=="Bézier" && drawing.Count==3)DrawContour(dc,Geometry.SampleCubicBezier(drawing[0],drawing[1],drawing[2],cursor,64),false,null,new Pen(B("#4DE1C1"),2));
            if(owner.ActiveTool=="Yay" && drawing.Count==2)DrawContour(dc,Arc(drawing[0],drawing[1],cursor),false,null,new Pen(B("#4DE1C1"),2));
        }
        catch(ArgumentException) { /* Incomplete preview is allowed; final input is validated. */ }
        dc.DrawRectangle(B("#172234"),null,new Rect(12,12,185,31));Text(dc,$"mm  /  Izgara {step:0.##}  /  {scale*100:0}%",new(22,19),B("#A4B9CC"),11);
        if(IsKeyboardFocused && ActualWidth>4 && ActualHeight>4)
            dc.DrawRectangle(null,new Pen(B("#4DE1C1"),2),new Rect(2,2,ActualWidth-4,ActualHeight-4));
    }
    // The ring is 14 DIP across and the label starts 8 DIP to the right.
    // Leave ample room for the complete indexed label and font overhang so a
    // partially visible mark is never omitted while panning or zooming.
    internal static bool MarkerCanAffectViewport(Point point,Size viewport)
    {
        const double margin=256;
        return point.X>=-margin && point.X<=viewport.Width+margin &&
               point.Y>=-margin && point.Y<=viewport.Height+margin;
    }
    // Display only: preserve the project name while keeping canvas labels bounded
    // and preventing invisible direction controls or line breaks from misleading.
    internal static string CanvasPieceLabel(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if(name.Length==0)return "(boş)";
        const int maxLength=80;
        var result=new StringBuilder(Math.Min(name.Length,maxLength));
        for(var i=0;i<name.Length;)
        {
            var first=name[i];
            var pair=char.IsHighSurrogate(first) && i+1<name.Length && char.IsLowSurrogate(name[i+1]);
            var count=pair ? 2 : 1;
            var category=pair ? CharUnicodeInfo.GetUnicodeCategory(name,i) : char.GetUnicodeCategory(first);
            var escaped=char.IsSurrogate(first) && !pair || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
            var token=escaped ? $"\\u{(int)first:X4}"+(pair ? $"\\u{(int)name[i+1]:X4}" : "") : name.Substring(i,count);
            if(result.Length+token.Length>maxLength || (i+count<name.Length && result.Length+token.Length==maxLength))
            { result.Append('…');return result.ToString(); }
            result.Append(token);i+=count;
        }
        return result.ToString();
    }
    private void DrawContour(DrawingContext dc,IReadOnlyList<Point2> points,bool closed,Brush? fill,Pen pen)
    { if(points.Count==0)return;var geometry=new StreamGeometry();using(var ctx=geometry.Open()) {ctx.BeginFigure(Screen(points[0]),fill is not null,closed);ctx.PolyLineTo(points.Skip(1).Select(Screen).ToList(),true,false);}geometry.Freeze();dc.DrawGeometry(fill,pen,geometry); }
    private void Text(DrawingContext dc,string value,Point pos,Brush brush,int size) => dc.DrawText(new FormattedText(value,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),pos);
}
