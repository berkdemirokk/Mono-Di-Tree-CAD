using TekstilCAD.Core;
using Geometry = TekstilCAD.Core.Geometry;

namespace TekstilCAD.Desktop;

/// <summary>One orthogonal piece transform. The tool-rail buttons and the command executor read the same table.</summary>
/// <param name="Id">Stable identifier for commands and checks.</param>
/// <param name="Label">Button text (also the lookup string the headless verification uses).</param>
/// <param name="Tooltip">Button tooltip.</param>
/// <param name="AccessibleName">Automation name, or null when the button never had one.</param>
/// <param name="Apply">Maps the contour about the centre of its bounding box.</param>
/// <param name="Grading">The matching grading-rule transform.</param>
internal sealed record OrthogonalOp(string Id,string Label,string Tooltip,string? AccessibleName,
    Func<IReadOnlyList<Point2>,Point2,List<Point2>> Apply,OrthogonalGradingTransform Grading);

internal static class OrthogonalOps
{
    // "90° sola" is the counter-clockwise turn on screen (-90) and "90° sağa" the clockwise one (+90).
    // Their grading enum members are named the other way round; the pairing below is the long-standing one.
    internal static readonly OrthogonalOp RotateCounterclockwise=new("rotate-ccw","90° sola",
        "Parçayı merkezinden saat yönünün tersine döndür","Parçayı saat yönünün tersine 90 derece döndür",
        (points,center)=>Geometry.Rotate(points,-90,center),OrthogonalGradingTransform.RotateRight);
    internal static readonly OrthogonalOp RotateClockwise=new("rotate-cw","90° sağa",
        "Parçayı merkezinden saat yönünde 90 derece döndür","Parçayı saat yönünde 90 derece döndür",
        (points,center)=>Geometry.Rotate(points,90,center),OrthogonalGradingTransform.RotateLeft);
    internal static readonly OrthogonalOp MirrorVertical=new("mirror-vertical","Aynala",
        "Parçayı merkezinden geçen dikey eksene göre aynala",null,
        (points,center)=>Geometry.Mirror(points,true,center.X),OrthogonalGradingTransform.MirrorVertical);
    internal static readonly OrthogonalOp MirrorHorizontal=new("mirror-horizontal","Yatay aynala",
        "Parçayı merkezinden geçen yatay eksene göre aynala","Parçayı yatay eksene göre aynala",
        (points,center)=>Geometry.Mirror(points,false,center.Y),OrthogonalGradingTransform.MirrorHorizontal);
    internal static readonly OrthogonalOp RotateHalf=new("rotate-half","180° döndür",
        "Parçayı merkezinden 180 derece tek işlemde döndür","Parçayı merkezinden 180 derece döndür",
        (points,center)=>Geometry.Rotate(points,180,center),OrthogonalGradingTransform.RotateHalf);
    internal static IReadOnlyList<OrthogonalOp> All { get; }=[RotateCounterclockwise,RotateClockwise,MirrorVertical,MirrorHorizontal,RotateHalf];
}

internal enum CommandResult
{
    /// <summary>The project changed (one undo step). <see cref="CommandOutcome.RefreshFailed"/> tells whether the screen refresh failed afterwards.</summary>
    Applied,
    /// <summary>The edit ran but left the project content unchanged; nothing was recorded for undo.</summary>
    NoChange,
    /// <summary>A guard or the validator refused; the project is unchanged.</summary>
    Refused,
    /// <summary>The project or selection changed after the preview; nothing was applied.</summary>
    Stale,
}

/// <summary>What the command executor tells its caller. <c>Message</c> is shown to the user as is (Turkish).</summary>
/// <param name="Guard">Name of the guard that refused (see <see cref="CommandGuards"/>), or null.</param>
/// <param name="RefreshFailed">True when the project changed but refreshing the screen failed afterwards.</param>
internal sealed record CommandOutcome(CommandResult Result,string Message,string? Guard=null,bool RefreshFailed=false)
{
    internal bool Changed=>Result==CommandResult.Applied;
}

/// <summary>Guard names, in the order <see cref="MainWindow.TryExecute"/> evaluates them.</summary>
internal static class CommandGuards
{
    internal const string Closed="closed";
    internal const string MutationReview="mutation-review";
    internal const string Recovery="recovery";
    internal const string DxfImport="dxf-import";
    internal const string Export="export";
    internal const string Drag="drag";
    internal const string LargeValidation="large-validation";
    internal const string PendingWork="pending-work";
    internal const string NoSelection="no-selection";
    internal const string Locked="locked";
    internal const string NoLayer="no-layer";
    internal const string Stamp="stamp";
    internal const string Commit="commit";
    internal const string Planner="planner";
}

/// <summary>
/// What a preview looked at. The edit is applied only while all of it is still true: same document object,
/// same project epoch, same selection and equal content.
/// </summary>
internal sealed record CommandStamp(ProjectDocument Document,Guid Epoch,Guid? SelectedId,ProjectDocument Snapshot);

/// <summary>One edit for <see cref="MainWindow.TryExecute"/>.</summary>
/// <param name="Description">Status text after a successful commit (also the undo step name).</param>
/// <param name="ActionName">Noun used in refusal texts, e.g. "Taşıma".</param>
/// <param name="Apply">Mutates the live document inside the single commit; the session rolls everything back if it throws.</param>
/// <param name="NeedsSelection">The edit targets the selected piece: it must exist and sit on an unlocked layer.</param>
/// <param name="AddsPiece">The edit adds a piece: a visible, unlocked layer must be available to receive it.</param>
/// <param name="Stamp">The preview this edit was made from, or null when there is none.</param>
internal sealed record CommandEdit(string Description,string ActionName,Action<ProjectDocument> Apply,
    bool NeedsSelection=true,bool AddsPiece=false,CommandStamp? Stamp=null);

public partial class MainWindow
{
    // A tool-rail button for one table entry. Clicking it runs the same TransformWithGrading call as always.
    private System.Windows.Controls.Button AddOrthogonalButton(System.Windows.Controls.Panel parent,OrthogonalOp op)
    {
        var button=AddButton(parent,op.Label,()=>TransformWithGrading(op.Apply,op.Grading),op.Tooltip);
        if(op.AccessibleName is not null)System.Windows.Automation.AutomationProperties.SetName(button,op.AccessibleName);
        return button;
    }
    internal CommandStamp CaptureCommandStamp()=>new(Document,projectEpoch,SelectedId,Document.Clone());
    private bool CommandStampMatches(CommandStamp stamp)
        =>ReferenceEquals(Document,stamp.Document) && projectEpoch==stamp.Epoch && SelectedId==stamp.SelectedId
            && DocumentSession.ContentEquals(Document,stamp.Snapshot);

    /// <summary>The layer a new piece would land on (same rule as <see cref="AddPiece(List{Point2},bool)"/>), or null when none can take it.</summary>
    internal Layer? NewPieceLayer()
    {
        var target=SelectedLayer;
        if(target is not { Visible: true, Locked: false })target=LayerTools.DefaultLayer(Document);
        return target is { Visible: true, Locked: false } ? target : null;
    }

    /// <summary>Text to turn into an edit. The planner that parses it is not wired yet; this never changes anything.</summary>
    internal CommandOutcome PreviewCommand(string text)
        =>new(CommandResult.Refused,string.IsNullOrWhiteSpace(text)
            ? "Bir komut yazın."
            : "Komut çözümleyici henüz bağlı değil; komut önizlenemedi.",CommandGuards.Planner);

    /// <summary>Applies one edit through the guard ladder. The planner's plan will be turned into a <see cref="CommandEdit"/> here.</summary>
    internal CommandOutcome ApplyCommand(CommandEdit edit)=>TryExecute(edit);

    /// <summary>
    /// The only door for command edits. Guards run in this order and the first one that refuses ends the call
    /// without touching the project; none of them opens a window or a message box:
    /// closing window (closed, or its recovery record is being cleared), mutation review, incomplete recovery, DXF import, export, active drag, large validation,
    /// pending drawing/drafts, selection, layer lock, new-piece layer, stamp; then exactly one
    /// <see cref="CommitObserved"/>. A change that was committed is reported as applied even when the
    /// screen refresh fails afterwards.
    /// </summary>
    internal CommandOutcome TryExecute(CommandEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        CommandOutcome Refuse(string guard)=>new(CommandResult.Refused,status.Text,guard);
        if(windowClosed || recoveryClosing)
        { SetStatus($"{edit.ActionName} yapılmadı · Pencere kapanıyor."); return Refuse(CommandGuards.Closed); }
        // EnsureMainMutationReady refreshes once after an earlier refresh failure and then asks for a new command.
        try { if(!EnsureMainMutationReady())return Refuse(CommandGuards.MutationReview); }
        catch(InvalidOperationException ex)
        { SetStatus(ex.Message);return new(CommandResult.Refused,ex.Message,CommandGuards.MutationReview); }
        if(BlockForIncompleteRecovery())return Refuse(CommandGuards.Recovery);
        if(BlockForDxfImport())return Refuse(CommandGuards.DxfImport);
        if(BlockForExport())return Refuse(CommandGuards.Export);
        if(BlockForActiveDrag())return Refuse(CommandGuards.Drag);
        if(BlockForLargeVertexValidation())return Refuse(CommandGuards.LargeValidation);
        if(HasPendingCanvasOrPropertyWork)
        {
            SetStatus($"{edit.ActionName} yapılmadı · Önce tamamlanmamış çizimi, sürüklemeyi, doğrulamayı veya alan taslaklarını tamamlayın ya da iptal edin.");
            return Refuse(CommandGuards.PendingWork);
        }
        if(edit.NeedsSelection)
        {
            if(Selected is null)
            {
                SetStatus($"{edit.ActionName} yapılmadı · Önce bir parça seçin.");
                return Refuse(CommandGuards.NoSelection);
            }
            if(BlockIfSelectionLocked(edit.ActionName))return Refuse(CommandGuards.Locked);
        }
        if(edit.AddsPiece && NewPieceLayer() is null)
        {
            SetStatus($"{edit.ActionName} yapılmadı · Seçili ve varsayılan katman gizli veya kilitli. Önce bir katmanı görünür ve kilitsiz yapın.");
            return Refuse(CommandGuards.NoLayer);
        }
        if(edit.Stamp is { } stamp && !CommandStampMatches(stamp))
        {
            SetStatus($"{edit.ActionName} yapılmadı · Önizlemeden sonra proje veya seçim değişti. Komutu yeniden önizleyin.");
            return new(CommandResult.Stale,status.Text,CommandGuards.Stamp);
        }
        var committed=false;
        try { CommitObserved(edit.Description,()=>edit.Apply(Document),()=>committed=true); }
        catch(Exception ex) when(committed && ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            // The project holds the change but the screen could not be refreshed. The next edit is refused once
            // (EnsureMainMutationReady) so nobody repeats the command blindly.
            mainMutationReviewRequired=true;
            var message=$"{edit.ActionName} projeye uygulandı ancak görünüm yenilenemedi. Sonucu ve geri alma geçmişini kontrol edin; komutu hemen tekrarlamayın. Ayrıntı: {ex.Message}";
            SetStatus(message);
            return new(CommandResult.Applied,message,null,true);
        }
        catch(Exception ex) when(!committed && ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            // The session has already rolled the document back.
            var message=$"{edit.ActionName} uygulanmadı · {ex.Message}";
            SetStatus(message);
            return new(CommandResult.Refused,message,CommandGuards.Commit);
        }
        return new(committed?CommandResult.Applied:CommandResult.NoChange,status.Text);
    }
}
