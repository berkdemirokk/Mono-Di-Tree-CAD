using System.Globalization;
using System.IO;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using TekstilCAD.FileFormats;
using Geometry = TekstilCAD.Core.Geometry;

namespace TekstilCAD.Desktop;

/// <summary>What the command box learns about the text in it. Nothing in the project changes while a preview is made.</summary>
internal enum CommandPreviewKind
{
    /// <summary>The box is empty.</summary>
    Empty,
    /// <summary>The text could still grow into a command; the box stays quiet until Enter.</summary>
    Typing,
    /// <summary>Understood; <see cref="CommandPreview.Plan"/> holds the plan.</summary>
    Plan,
    /// <summary>A question the user answers with a button (the unit).</summary>
    Question,
    /// <summary>Refused with a reason and examples.</summary>
    Refusal,
}

/// <summary>The one-line answer to the text in the box.</summary>
internal sealed record CommandPreview(CommandPreviewKind Kind,string Typed,Plan? Plan=null,Refusal? Notice=null,bool UnitAssumed=false)
{
    /// <summary>The sentence the preview row shows (preview, question or refusal text).</summary>
    internal string Text=>Plan?.Preview ?? Notice?.Message ?? "";
}

public partial class MainWindow
{
    /// <summary>The planner's read-only picture of the window: document, epoch and the facts a preview sentence states.</summary>
    internal DocumentView CommandView()=>new(Document,projectEpoch)
    {
        DrawingInProgress=canvas is not null && canvas.HasPendingDrawing,
        DrawingPointCount=canvas?.PendingDrawingPointCount ?? 0,
        HasUnappliedDrafts=propertyDrafts.Count>0,
        RecoveryIncomplete=recoveryRestoreIncomplete,
        FileOperationInProgress=DxfImportRunning || ExportRunning || projectOpenRunning,
        UndoCount=session.UndoCount,
        RedoCount=session.RedoCount,
        NewPieceLayerId=NewPieceLayer()?.Id,
        FileName=filePath is null ? null : Path.GetFileName(filePath),
        HasUnsavedChanges=dirty,
    };
    internal Selection CommandSelection()=>new(selection.ToList());

    /// <summary>
    /// Reads the text, plans it against the current project and selection, and returns the one-line answer. Never changes
    /// the project, the selection or the undo history. <paramref name="onEnter"/> is true when the user pressed Enter: only
    /// then is a refusal for a still-incomplete text shown (while typing it stays <see cref="CommandPreviewKind.Typing"/>).
    /// </summary>
    internal CommandPreview PreviewCommand(string? text,bool onEnter=false)
    {
        var typed=text ?? "";
        if(string.IsNullOrWhiteSpace(typed))return new(CommandPreviewKind.Empty,typed);
        try
        {
            var parsed=CommandParser.Parse(typed,new ParserContext(Document.Pieces.Select(p=>p.Name ?? "").ToList(),canvas is not null && canvas.HasPendingDrawing));
            if(parsed.IsIncomplete && !onEnter)return new(CommandPreviewKind.Typing,typed);
            var planned=CommandPlanner.Plan(parsed,CommandView(),CommandSelection());
            if(planned.Plan is { } plan)return new(CommandPreviewKind.Plan,typed,plan,null,parsed.UnitAssumed);
            var notice=planned.Refusal!;
            return new(notice.IsQuestion ? CommandPreviewKind.Question : CommandPreviewKind.Refusal,typed,null,notice);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            // A timer tick or key press must never carry an exception out of the box.
            return new(CommandPreviewKind.Refusal,typed,null,Refusals.Failed("Komut hesaplanamadı"));
        }
    }

    /// <summary>
    /// Applies a plan the user has seen. Project edits go through <see cref="TryExecute"/> (guards, stamp, ONE commit); a plan that
    /// needs an explicit button (delete) is refused unless <paramref name="confirmed"/> is true. Nothing here opens a window or a message
    /// box except the file and window hand-offs, which are the existing dialogs by design.
    /// </summary>
    internal CommandOutcome ApplyCommand(Plan plan,bool confirmed=false)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if(plan.RequiresConfirmation && !confirmed)
        {
            const string text="Bu işlem için açık onay gerekir; önizlemedeki onay düğmesine basın.";
            SetStatus(text);
            return new(CommandResult.Refused,text,CommandGuards.Confirm);
        }
        var selectionBefore=selection.ToList();var primaryBefore=SelectedId;
        CommandOutcome outcome;
        try
        {
            // A delete is a "dangerous" plan that still edits the project in Core, so it takes the project route too.
            outcome=plan.IsProjectEdit || plan.AppliesInCore || plan.Operation is not null ? ApplyProjectPlan(plan) : ApplyOtherPlan(plan);
        }
        catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            var message=$"{plan.ActionName} yapılamadı · {ex.Message}";
            SetStatus(message);
            outcome=new(CommandResult.Refused,message,CommandGuards.Commit);
        }
        // A refused edit must not leave the selection it set inside the (rolled back) commit.
        if(outcome.Result is CommandResult.Refused or CommandResult.Stale && (plan.AppliesInCore || plan.Operation is not null) && !selection.SequenceEqual(selectionBefore))
        {
            SetSelectionCore(selectionBefore,primaryBefore);
            try { Refresh(); } catch(Exception ex) when(ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException) { /* the refusal text already tells what happened */ }
        }
        return outcome with { Caveats=plan.Caveats.Count>0 ? plan.Caveats : null };
    }

    private CommandOutcome ApplyProjectPlan(Plan plan)
    {
        var needsReview=false;
        Action<ProjectDocument> apply=plan.Operation switch
        {
            MovePieceOperation move=>doc=>MovePieceInDocument(doc,move.PieceId,move.DxMm,move.DyMm,null),
            MovePieceToOperation to=>doc=>MovePieceInDocument(doc,to.PieceId,to.DxMm,to.DyMm,(to.XMm,to.YMm)),
            RotatePieceOperation turn=>doc=>needsReview=TransformPieceInDocument(doc,turn.PieceId,OrthogonalOps.All.Single(op=>op.Id==turn.OperationId)),
            MirrorPieceOperation flip=>doc=>needsReview=TransformPieceInDocument(doc,flip.PieceId,OrthogonalOps.All.Single(op=>op.Id==flip.OperationId)),
            _=>doc=>
            {
                plan.Apply(doc);
                if(plan.CreatedPieceId is { } created)SetSelectionCore([created],created);
                else if(plan.Kind==PlanKind.DeletePiece)SetSelectionCore([],null);
            },
        };
        var edit=new CommandEdit(plan.ResultText.Length>0 ? plan.ResultText : plan.Preview,
            plan.ActionName.Length>0 ? plan.ActionName : "Komut",apply,
            NeedsSelection:plan.NeedsSelectedPiece,AddsPiece:plan.AddsPiece,PlanStamp:plan.Stamp,
            TargetPieceId:plan.NeedsSelectedPiece ? null : plan.TargetPieceId);
        var outcome=TryExecute(edit);
        if(outcome.Result==CommandResult.Applied && needsReview)
        {
            const string notice="Dönüşüm uygulandı; kayıtlı serileme kuralı önceki kontura ait. Serilemeden önce kuralı gözden geçirin.";
            try { ShowCanvasError(notice); } catch(Exception ex) when(ex is not OutOfMemoryException) { /* the caveat of the plan still carries it */ }
        }
        return outcome.Result==CommandResult.Applied && !outcome.RefreshFailed ? outcome with { Message=edit.Description } : outcome;
    }

    /// <summary>The same translation the position boxes apply: contour, notch marks, grainline and the grading rule move together.</summary>
    private static void MovePieceInDocument(ProjectDocument document,Guid pieceId,double dx,double dy,(double X,double Y)? requestedCorner)
    {
        var piece=document.Pieces.Single(p=>p.Id==pieceId);
        var translation=TranslatePiece(piece,dx,dy);
        if(translation.Points is null)return;
        var (rule,movedRule)=TranslateGradingRule(document,piece,translation.Points,dx,dy);
        if(requestedCorner is { } corner)
        {
            var bounds=Geometry.Bounds(translation.Points);
            if(!TargetPositionMatches(bounds.MinX,corner.X) || !TargetPositionMatches(bounds.MinY,corner.Y))
                throw new InvalidOperationException("Hedef konum sayısal olarak tam karşılanamadı; daha küçük bir değer yazın.");
        }
        piece.Points=translation.Points;piece.Notches=translation.Notches;piece.Grainline=translation.Grainline;
        if(movedRule is not null)document.GradingRules[document.GradingRules.IndexOf(rule!)]=movedRule;
    }

    /// <summary>
    /// The result of a rotation or mirror button for one piece of a document (same arithmetic as <c>TransformCore</c>).
    /// Returns true when the saved grading rule no longer fits the new contour.
    /// </summary>
    private static bool TransformPieceInDocument(ProjectDocument document,Guid pieceId,OrthogonalOp op)
    {
        var piece=document.Pieces.Single(p=>p.Id==pieceId);
        if(piece.Points.Count>Geometry.MaxValidatedContourVertices)
            throw new InvalidOperationException("Parçada 4096'dan fazla nokta var; bu işlemi Ayrıntılı görünümdeki ilgili düğmeyle yapın.");
        if(piece.Notches is not null)_=MarkedPieceTranslation.Translate(piece,0,0);
        var sourcePoints=piece.Points.ToList();
        var center=Geometry.Bounds(sourcePoints).Center;
        var result=op.Apply(sourcePoints,center);
        if(piece.Notches is not null && result.Count!=sourcePoints.Count)
            throw new InvalidOperationException("Dönüşümde köşe sayısı değişti; işaretler korunamadı.");
        var notches=piece.Notches?.Select(mark=>new NotchMark { VertexIndex=mark.VertexIndex,Position=result[mark.VertexIndex] }).ToList();
        var rule=document.GradingRules.FirstOrDefault(r=>r.SourcePieceId==piece.Id);
        var transformedRule=rule?.OrthogonalFor(piece,result,op.Grading);
        var needsReview=!result.SequenceEqual(piece.Points) && rule is not null && transformedRule is null;
        var grain=GrainlineTransform.Transform(piece.Grainline,piece.Points[0],result[0],points=>op.Apply(points,center));
        piece.Points=result;piece.Grainline=grain;piece.Notches=notches;
        if(transformedRule is not null)document.GradingRules[document.GradingRules.IndexOf(rule!)]=transformedRule;
        return needsReview;
    }

    // ---- plans that do not edit the project ----------------------------------------------------------------------------

    /// <summary>Plans whose buttons are not gated by an unfinished recovery restore: they never edit or select pieces.</summary>
    private static bool DoesNotTouchProject(Plan plan) => plan.Command switch
    {
        HandoffCommand h => h.Target is HandoffTarget.RecoveryScan or HandoffTarget.About,
        ViewCommand v => v.Action is ViewAction.ThemeDark or ViewAction.ThemeLight or ViewAction.TextLarger or ViewAction.TextSmaller
            or ViewAction.AllTools or ViewAction.DetailedView,
        _ => false,
    };

    private CommandOutcome ApplyOtherPlan(Plan plan)
    {
        CommandOutcome Refuse(string guard)=>new(CommandResult.Refused,status.Text,guard);
        if(windowClosed || recoveryClosing)
        { SetStatus($"{plan.ActionName} yapılmadı · Pencere kapanıyor."); return Refuse(CommandGuards.Closed); }
        try { if(!EnsureMainMutationReady())return Refuse(CommandGuards.MutationReview); }
        catch(InvalidOperationException ex)
        { SetStatus(ex.Message);return new(CommandResult.Refused,ex.Message,CommandGuards.MutationReview); }
        if(plan.Kind==PlanKind.CancelPreview)return new(CommandResult.Done,"Önizleme kapatıldı. Hiçbir şey değişmedi.");
        if(plan.Kind==PlanKind.Help)return new(CommandResult.Done,"Örnek listesi açıldı; birine tıklayınca kutuya yazılır, siz Enter'a basmadan uygulanmaz.",OpensExamples:true);
        if(!DoesNotTouchProject(plan) && BlockForIncompleteRecovery())return Refuse(CommandGuards.Recovery);
        if(plan.Stamp.Check(CommandView(),CommandSelection())!=StampState.Current)
        {
            SetStatus($"{plan.ActionName} yapılmadı · {Refusals.TemplatePreviewStale}");
            return new(CommandResult.Stale,status.Text,CommandGuards.Stamp);
        }
        CommandOutcome Done(string? message=null)
        {
            var text=message ?? (plan.ResultText.Length>0 ? plan.ResultText : plan.Preview);
            return new(CommandResult.Done,text);
        }
        switch(plan.Kind)
        {
            case PlanKind.SelectPiece:
                if(BlockForActiveDrag() || BlockForLargeVertexValidation())return Refuse(CommandGuards.Drag);
                Select(plan.SelectionAfter);
                return Done(plan.SelectionAfter is null ? "Seçim bırakıldı." : plan.ResultText.Length>0 ? plan.ResultText : plan.Preview);
            case PlanKind.Undo:
            case PlanKind.Redo:
            {
                var redo=plan.Kind==PlanKind.Redo;
                if(canvas.HasPendingDrawing)return Refuse(CommandGuards.PendingWork);
                var steps=plan.Command is UndoCommand undo ? Math.Max(1,undo.Steps) : 1;
                var done=0;
                for(var i=0;i<steps && (redo ? session.RedoCount : session.UndoCount)>0;i++)
                {
                    var before=redo ? session.RedoCount : session.UndoCount;
                    ApplyHistoryChange(redo);
                    if((redo ? session.RedoCount : session.UndoCount)==before)break;
                    done++;
                }
                if(done==0)return Refuse(CommandGuards.PendingWork);
                return new(CommandResult.Applied,(redo ? "Yinelendi" : "Geri alındı")+(done>1 ? $" · {done} işlem." : "."));
            }
            case PlanKind.View:
                if(plan.Command is not ViewCommand view)break;
                switch(view.Action)
                {
                    case ViewAction.FitAll:canvas.Fit();return Done("Tüm parçalar ekrana sığdırıldı.");
                    case ViewAction.FitSelected:canvas.FitSelectedPiece();return Done("Seçili parça ekrana sığdırıldı.");
                    case ViewAction.ZoomIn:canvas.ZoomView(true);return Done("Görünüm yakınlaştırıldı.");
                    case ViewAction.ZoomOut:canvas.ZoomView(false);return Done("Görünüm uzaklaştırıldı.");
                    case ViewAction.PanLeft:canvas.PanView(120,0);return Done("Görünüm sola kaydırıldı.");
                    case ViewAction.PanRight:canvas.PanView(-120,0);return Done("Görünüm sağa kaydırıldı.");
                    case ViewAction.PanUp:canvas.PanView(0,120);return Done("Görünüm yukarı kaydırıldı.");
                    case ViewAction.PanDown:canvas.PanView(0,-120);return Done("Görünüm aşağı kaydırıldı.");
                    case ViewAction.DetailedView:return ApplyDetailedView("Ayrıntılı görünüme geçildi.");
                    case ViewAction.AllTools:return ApplyAllTools();
                    case ViewAction.TextLarger:return ApplyTextScale(true);
                    case ViewAction.TextSmaller:return ApplyTextScale(false);
                    case ViewAction.ThemeDark:return Done(InterfaceSettings.UseTheme(TekstilCAD.Core.Theme.ThemePalette.Dark));
                    case ViewAction.ThemeLight:return Done(InterfaceSettings.UseTheme(TekstilCAD.Core.Theme.ThemePalette.Light));
                }
                break;
            case PlanKind.Tool:
                if(plan.Command is ToolCommand tool && canvas.HasPendingDrawing==false)
                {
                    var wanted=tool.Tool switch
                    {
                        DrawingTool.Line=>"Çizgi",DrawingTool.Polyline=>"Çoklu çizgi",DrawingTool.Rectangle=>"Dikdörtgen",
                        DrawingTool.Bezier=>"Bézier",DrawingTool.Arc=>"Yay",_=>"Seç",
                    };
                    SetTool(wanted);
                    // SetTool returns silently while a drag or a validation runs: report what really happened.
                    if(ActiveTool!=wanted)
                    {
                        var blocked=$"Fare aracı değişmedi · Sürüklemeyi ya da doğrulamayı tamamlayın, sonra yeniden deneyin.";
                        SetStatus(blocked);
                        return new(CommandResult.Refused,blocked,CommandGuards.Drag);
                    }
                    return Done($"Fare aracı: {ActiveTool}.");
                }
                break;
            case PlanKind.FinishDrawing:
                if(plan.Command is FinishDrawingCommand finish && canvas.HasPendingDrawing)
                {
                    if(BlockForActiveDrag() || BlockForLargeVertexValidation())return Refuse(CommandGuards.Drag);
                    var points=canvas.PendingDrawingPointCount;
                    switch(finish.Action)
                    {
                        case FinishDrawingAction.Close:canvas.FinishPolyline();break;
                        case FinishDrawingAction.FinishOpen:FinishOpenDrawing();break;
                        case FinishDrawingAction.Cancel:canvas.CancelDrawing();ClearCanvasError();break;
                        case FinishDrawingAction.RemoveLastPoint:canvas.RemoveLastPendingPoint();break;
                    }
                    var changed=!canvas.HasPendingDrawing || canvas.PendingDrawingPointCount!=points;
                    if(!changed)return Refuse(CommandGuards.PendingWork);
                    return finish.Action is FinishDrawingAction.Close or FinishDrawingAction.FinishOpen
                        ? new(CommandResult.Applied,"Çizim bitirildi.") : Done(finish.Action==FinishDrawingAction.Cancel ? "Çizim iptal edildi." : "Son nokta silindi.");
                }
                break;
            case PlanKind.File:
                if(plan.Command is FileCommand file)
                {
                    try
                    {
                    switch(file.Action)
                    {
                        case FileAction.Save:return RunHandoff("File.Save",Save,"Kaydetme isteği işlendi.");
                        case FileAction.SaveAs:return RunHandoff("File.SaveAs",SaveAs,"Kaydetme isteği işlendi.");
                        case FileAction.SaveCopy:return RunHandoff("File.SaveCopy",SaveNewCopy,"Güvenli kopya isteği işlendi.");
                        case FileAction.Open:return RunHandoff("File.Open",Open,"Açma isteği işlendi.");
                        case FileAction.New:return RunHandoff("File.New",NewDocument,"Yeni proje isteği işlendi.");
                    }
                    }
                    catch(ProjectConflictException) { ShowProjectSaveConflict(this);return Done("Kaydetme çakışması: açılan pencerede bir seçenek seçin."); }
                }
                break;
            case PlanKind.Window:
                if(plan.Command is not HandoffCommand hand)break;
                if(hand.Target==HandoffTarget.Layers)
                {
                    var shown=ApplyDetailedView("Katmanlar Ayrıntılı görünümde yönetilir; katman seçimi bölümü açıldı.");
                    if(shown.Result==CommandResult.Done)layerList.Focus();
                    return shown;
                }
                if(hand.Target==HandoffTarget.RecoveryScan && recoveryRescan is not { IsEnabled:true })
                {
                    // The button is disabled while a scan runs or when there is no recovery store: the box must not claim a scan started.
                    const string busy="Kurtarma taraması şu an başlatılamıyor; çalışıyor ya da kurtarma deposu yok.";
                    SetStatus(busy);
                    return new(CommandResult.Refused,busy,CommandGuards.PendingWork);
                }
                if(HandoffRouteFor(hand) is not { } route)break;
                return RunHandoff("Handoff."+hand.Target,route.Run,route.Done);
        }
        var message=$"{plan.ActionName.Trim()} bu kutudan şu anda yapılamıyor. "+(IsSimpleView ? "“Tüm araçlar” düğmesine basıp ilgili işlemi seçin." : "İlgili düğmeyi kullanın.");
        SetStatus(message);
        return new(CommandResult.Refused,message,CommandGuards.Unsupported);
    }
}
