using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

/// <summary>
/// Checks for the command executor groundwork. Every scenario builds its own fresh window (never shown) so the
/// long-lived verification window and its order-dependent state stay untouched.
/// The expected documents are written out by hand (exact quarter-turn and mirror arithmetic on whole numbers), not
/// computed with the code under test, so they describe the behaviour the buttons and the draft path had before
/// the shared operation table and TranslatePiece existed.
/// </summary>
internal static partial class CommandVerification
{
    private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;

    internal static void Run(MainWindow studio,List<string> checks,string? reportPath=null)
    {
        // The table first: a regression there fails a check before any button is clicked (AddButton turns an
        // exception in a click into a message box, which would hang a headless run instead of failing it).
        VerifyOperationTable(checks);
        VerifyOrthogonalButtonsKeepTheirDocuments(checks);
        VerifyPositionDraftsKeepTheirDocuments(checks);
        VerifyExecutor(checks);
        VerifyPlanExecution(checks);
        VerifyCommandBox(checks,reportPath);
        VerifyGhost(checks,reportPath);
        VerifyVoiceButton(checks,reportPath);
        VerifyOpenTextileToolsAt(checks);
        VerifyCommandCoverage(checks);
        VerifyThemeButton(checks);
    }

    // ---- fixtures ---------------------------------------------------------------------------------------------

    // A 20 x 10 rectangle whose bounding-box centre is (10,5), with two notch marks, a grainline and a grading rule.
    private static readonly Point2[] Rectangle=[new(0,0),new(20,0),new(20,10),new(0,10)];
    private static readonly Point2[] RuleDeltas=[new(0,0),new(2,0),new(0,3),new(1,-1)];

    private static PatternPiece MarkedPiece(string name="Denklik")=>new()
    {
        Name=name,Points=[..Rectangle],
        Notches=[new NotchMark { VertexIndex=0,Position=Rectangle[0] },new NotchMark { VertexIndex=2,Position=Rectangle[2] }],
        Grainline=new Grainline { Start=new(5,2),End=new(5,8) },
    };
    private static GradingRuleSet RuleFor(PatternPiece piece)=>new()
    { SourcePieceId=piece.Id,SourceClosed=true,SourcePoints=[..piece.Points],Deltas=[..RuleDeltas] };

    private static void WithFreshWindow(Action<MainWindow> body)
    {
        var fresh=new MainWindow();StopTimer(fresh);var closed=false;fresh.Closed+=(_,_)=>closed=true;
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(fresh.Dispatcher));
        // If a check fails half way, closing must not open a discard prompt that would hang the run.
        fresh.ConfirmDiscardDocumentForVerification=()=>true;
        fresh.ConfirmDiscardDrawingForVerification=()=>true;
        try
        {
            _=new WindowInteropHelper(fresh).EnsureHandle();
            body(fresh);
        }
        finally
        {
            StopTimer(fresh);
            // A window with unsaved work would open the discard prompt on Close.
            typeof(MainWindow).GetField("dirty",Private)!.SetValue(fresh,false);
            ((System.Collections.IDictionary)typeof(MainWindow).GetField("propertyDrafts",Private)!.GetValue(fresh)!).Clear();
            fresh.Close();PumpUntil(()=>closed);
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
    private static DocumentSession SessionOf(MainWindow window)=>(DocumentSession)typeof(MainWindow).GetField("session",Private)!.GetValue(window)!;
    private static T Field<T>(MainWindow window,string name)=>(T)typeof(MainWindow).GetField(name,Private)!.GetValue(window)!;
    private static void Layout(MainWindow window)
    {
        var root=(FrameworkElement)window.Content;
        root.Measure(new Size(1024,640));root.Arrange(new Rect(0,0,1024,640));root.UpdateLayout();
    }

    // ---- 12: the five transform buttons ------------------------------------------------------------------------

    // Hand-computed about the centre (10,5). Screen is Y-down: "90° sağa" turns clockwise, "90° sola" counter-clockwise.
    // Grading deltas follow the long-standing mapping of each button (see OrthogonalFor in the grading rule).
    private static readonly (string Label,Point2[] Points,Point2 GrainStart,Point2 GrainEnd,Point2[] Deltas)[] ButtonCases=
    [
        ("90° sola",[new(5,15),new(5,-5),new(15,-5),new(15,15)],new(7,10),new(13,10),[new(0,0),new(0,-2),new(3,0),new(-1,-1)]),
        ("90° sağa",[new(15,-5),new(15,15),new(5,15),new(5,-5)],new(13,0),new(7,0),[new(0,0),new(0,2),new(-3,0),new(1,1)]),
        ("180° döndür",[new(20,10),new(0,10),new(0,0),new(20,0)],new(15,8),new(15,2),[new(0,0),new(-2,0),new(0,-3),new(-1,1)]),
        ("Aynala",[new(20,0),new(0,0),new(0,10),new(20,10)],new(15,2),new(15,8),[new(0,0),new(-2,0),new(0,3),new(-1,-1)]),
        ("Yatay aynala",[new(0,10),new(20,10),new(20,0),new(0,0)],new(5,8),new(5,2),[new(0,0),new(2,0),new(0,-3),new(1,1)]),
    ];

    private static void VerifyOrthogonalButtonsKeepTheirDocuments(List<string> checks)
    {
        var verified=0;
        foreach(var (label,points,grainStart,grainEnd,deltas) in ButtonCases)
        {
            WithFreshWindow(window=>
            {
                var piece=MarkedPiece();var rule=RuleFor(piece);
                window.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });window.Select(piece.Id);
                Layout(window);
                var session=SessionOf(window);
                var before=window.Document.Clone();
                var button=Descendants<Button>((FrameworkElement)window.Content).Single(b=>Equals(b.Content,label));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var current=window.Document.Pieces.Single();var mapped=window.Document.GradingRules.Single();
                Check(current.Points.SequenceEqual(points)
                    && current.Notches is { Count:2 } marks && marks[0].VertexIndex==0 && marks[0].Position==points[0]
                    && marks[1].VertexIndex==2 && marks[1].Position==points[2]
                    && current.Grainline is { } grain && grain.Start==grainStart && grain.End==grainEnd && !grain.NeedsReview
                    && mapped.SourcePoints.SequenceEqual(points) && mapped.Deltas.SequenceEqual(deltas) && session.UndoCount==1,
                    $"{label} button gives the contour, marks, grainline and grading rule it gave before the shared operation table",checks);
                Check(session.Undo() && DocumentSession.ContentEquals(before,window.Document),
                    $"{label} button result is one undo step back to the original document",checks);
                verified++;
            });
        }
        Check(verified==ButtonCases.Length && verified==5,"Rotation and mirror buttons produce the same documents after the shared operation table",checks);
    }

    // ---- 13: position drafts -----------------------------------------------------------------------------------

    private static void VerifyPositionDraftsKeepTheirDocuments(List<string> checks)
    {
        WithFreshWindow(window=>
        {
            var piece=MarkedPiece();var rule=RuleFor(piece);
            window.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });window.Select(piece.Id);
            var session=SessionOf(window);var before=window.Document.Clone();
            Point2[] moved=[new(7.5,2.25),new(27.5,2.25),new(27.5,12.25),new(7.5,12.25)];
            Field<TextBox>(window,"name").Text="Taşındı";Field<TextBox>(window,"x").Text="7.5";Field<TextBox>(window,"y").Text="2.25";
            Invoke(window,"ApplyProperties");
            var current=window.Document.Pieces.Single();var mapped=window.Document.GradingRules.Single();
            Check(current.Name=="Taşındı" && current.Points.SequenceEqual(moved)
                && current.Notches is { Count:2 } marks && marks[0].VertexIndex==0 && marks[0].Position==moved[0]
                && marks[1].VertexIndex==2 && marks[1].Position==moved[2]
                && current.Grainline is { } grain && grain.Start==new Point2(12.5,4.25) && grain.End==new Point2(12.5,10.25)
                && mapped.SourcePoints.SequenceEqual(moved) && mapped.Deltas.SequenceEqual(RuleDeltas) && session.UndoCount==1,
                "Position drafts apply the same translation after the TranslatePiece extraction",checks);
            Check(session.Undo() && DocumentSession.ContentEquals(before,window.Document),
                "A position draft is one undo step back to the original piece, marks, grainline and rule",checks);
        });
        WithFreshWindow(window=>
        {
            var piece=MarkedPiece();var rule=RuleFor(piece);
            window.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });window.Select(piece.Id);
            var session=SessionOf(window);
            Field<TextBox>(window,"name").Text="Yalnız ad";
            Invoke(window,"ApplyProperties");
            var current=window.Document.Pieces.Single();var mapped=window.Document.GradingRules.Single();
            Check(current.Name=="Yalnız ad" && current.Points.SequenceEqual(Rectangle)
                && current.Notches is { Count:2 } marks && marks[0].Position==Rectangle[0] && marks[1].Position==Rectangle[2]
                && current.Grainline is { } grain && grain.Start==new Point2(5,2) && grain.End==new Point2(5,8)
                && mapped.SourcePoints.SequenceEqual(Rectangle) && mapped.Deltas.SequenceEqual(RuleDeltas) && session.UndoCount==1,
                "A name-only draft leaves contour, marks, grainline and grading rule untouched",checks);
        });
    }

    // ---- the shared operation table ----------------------------------------------------------------------------

    private static void VerifyOperationTable(List<string> checks)
    {
        var table=OrthogonalOps.All;
        var center=new Point2(10,5);
        Check(table.Count==5 && table.Select(op=>op.Id).Distinct().Count()==5 && table.Select(op=>op.Label).Distinct().Count()==5
            && ButtonCases.All(c=>table.Count(op=>op.Label==c.Label)==1),
            "Operation table lists each of the five transforms once under its button label",checks);
        Check(ButtonCases.All(c=>table.Single(op=>op.Label==c.Label).Apply(Rectangle,center).SequenceEqual(c.Points)),
            "Operation table entries map the sample contour to the hand-computed points the buttons produce",checks);
        // The grading enum members are named the other way round for the two quarter turns; this pairing is long-standing.
        Check(OrthogonalOps.RotateCounterclockwise.Grading==OrthogonalGradingTransform.RotateRight
            && OrthogonalOps.RotateClockwise.Grading==OrthogonalGradingTransform.RotateLeft
            && OrthogonalOps.RotateHalf.Grading==OrthogonalGradingTransform.RotateHalf
            && OrthogonalOps.MirrorVertical.Grading==OrthogonalGradingTransform.MirrorVertical
            && OrthogonalOps.MirrorHorizontal.Grading==OrthogonalGradingTransform.MirrorHorizontal,
            "Operation table keeps the grading transform paired with each button",checks);
        WithFreshWindow(window=>
        {
            Layout(window);
            var buttons=Descendants<Button>((FrameworkElement)window.Content).ToList();
            Check(table.All(op=>buttons.Count(b=>Equals(b.Content,op.Label))==1
                && buttons.Single(b=>Equals(b.Content,op.Label)) is var button && Equals(button.ToolTip,op.Tooltip)
                && (op.AccessibleName is null || System.Windows.Automation.AutomationProperties.GetName(button)==op.AccessibleName)),
                "Every transform button takes its text, tooltip and accessible name from the operation table",checks);
        });
    }

    // ---- executor ----------------------------------------------------------------------------------------------

    private static readonly Point2[] MovedRectangle=[new(7.5,2.25),new(27.5,2.25),new(27.5,12.25),new(7.5,12.25)];
    private static bool windowsStable;

    // Edit that changes the project name (so every run is a real change) and tells whether it ran.
    private static CommandEdit Spy(Action ran,bool needsSelection=true,bool addsPiece=false,CommandStamp? stamp=null)
        =>new("Deneme uygulandı","Deneme",doc=> { ran();doc.Name+="!"; },needsSelection,addsPiece,stamp);

    // The hand-written move uses the same shared routines the position drafts use.
    private static CommandEdit MoveEdit(double dx,double dy)=>new("Parça taşındı","Taşıma",doc=>
    {
        var piece=doc.Pieces.Single();
        var translation=MainWindow.TranslatePiece(piece,dx,dy);
        var (rule,movedRule)=MainWindow.TranslateGradingRule(doc,piece,translation.Points,dx,dy);
        piece.Points=translation.Points!;piece.Notches=translation.Notches;piece.Grainline=translation.Grainline;
        if(movedRule is not null)doc.GradingRules[doc.GradingRules.IndexOf(rule!)]=movedRule;
    });

    // Runs one scenario in a fresh window and notes whether it opened any window or became visible.
    private static void Scenario(Action<MainWindow> body)
        =>WithFreshWindow(window=>
        {
            var count=Application.Current.Windows.Count;
            body(window);
            windowsStable&=Application.Current.Windows.Count==count && !window.IsVisible;
        });

    private static void Set(MainWindow window,string field,object? value)=>typeof(MainWindow).GetField(field,Private)!.SetValue(window,value);
    private static void SetCanvas(CadCanvas canvas,string field,object? value)=>typeof(CadCanvas).GetField(field,Private)!.SetValue(canvas,value);

    private static void VerifyExecutor(List<string> checks)
    {
        windowsStable=true;
        // 3: a move that uses the shared translation keeps marks, grainline and rule with the piece, in one undo step.
        Scenario(window=>
        {
            var piece=MarkedPiece();var rule=RuleFor(piece);
            window.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });window.Select(piece.Id);
            var session=SessionOf(window);var before=window.Document.Clone();
            var outcome=window.ApplyCommand(MoveEdit(7.5,2.25));
            var current=window.Document.Pieces.Single();var mapped=window.Document.GradingRules.Single();
            Check(outcome is { Result:CommandResult.Applied,RefreshFailed:false } && current.Points.SequenceEqual(MovedRectangle)
                && current.Notches is { Count:2 } marks && marks[0].Position==MovedRectangle[0] && marks[1].Position==MovedRectangle[2]
                && current.Grainline is { } grain && grain.Start==new Point2(12.5,4.25) && grain.End==new Point2(12.5,10.25)
                && mapped.SourcePoints.SequenceEqual(MovedRectangle) && mapped.Deltas.SequenceEqual(RuleDeltas)
                && window.Selected?.Id==piece.Id,
                "Command executor move keeps notch marks, grainline and grading rule with the piece",checks);
            Check(session.UndoCount==1 && session.Undo() && DocumentSession.ContentEquals(before,window.Document),
                "Command executor change is exactly one undo step",checks);
        });
        // 4: locked layer.
        Scenario(window=>
        {
            var piece=MarkedPiece();
            window.LoadDocument(new ProjectDocument { Pieces=[piece],Layers=[new Layer { Id=Layer.DefaultId,Name="Katman 1",Locked=true }] });
            window.Select(piece.Id);
            var session=SessionOf(window);var before=window.Document.Clone();var ran=false;
            var outcome=window.ApplyCommand(Spy(()=>ran=true));
            Check(outcome is { Result:CommandResult.Refused,Guard:CommandGuards.Locked } && !ran && outcome.Message.Contains("kilitli")
                && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0,
                "Command executor refuses a move on a locked layer and leaves the document unchanged",checks);
            var addOutcome=window.ApplyCommand(Spy(()=>ran=true,needsSelection:false,addsPiece:true));
            Check(window.ApplyCommand(Spy(()=>ran=true,needsSelection:true,addsPiece:true)) is { Guard:CommandGuards.Locked },
                "Command executor answers a locked selection before a missing layer",checks);
            Check(addOutcome is { Result:CommandResult.Refused,Guard:CommandGuards.NoLayer } && addOutcome.Message.Contains("yapılmadı") && !ran
                && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0,
                "Command executor refuses a new piece when no visible, unlocked layer can take it",checks);
        });
        // A selection is required for edits that target one.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(null);
            var before=window.Document.Clone();var ran=false;
            var outcome=window.ApplyCommand(Spy(()=>ran=true));
            Check(outcome is { Result:CommandResult.Refused,Guard:CommandGuards.NoSelection } && outcome.Message.Contains("yapılmadı") && !ran && DocumentSession.ContentEquals(before,window.Document),
                "Command executor refuses a piece edit when nothing is selected",checks);
        });
        // 5: pending drawing and unapplied drafts.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var canvas=Field<CadCanvas>(window,"canvas");var session=SessionOf(window);var before=window.Document.Clone();var ran=false;
            window.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)]));
            try
            {
                var outcome=window.ApplyCommand(MoveEdit(1,1));
                Check(outcome is { Result:CommandResult.Refused,Guard:CommandGuards.PendingWork } && outcome.Message.Contains("yapılmadı") && canvas.HasPendingDrawing
                    && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0,
                    "Command executor refuses a move while a drawing is pending",checks);
            }
            finally { canvas.CancelDrawing();window.SetTool("Seç"); }
            Field<TextBox>(window,"name").Text="Uygulanmamış ad";
            try
            {
                var outcome=window.ApplyCommand(Spy(()=>ran=true));
                Check(outcome is { Result:CommandResult.Refused,Guard:CommandGuards.PendingWork } && !ran
                    && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0,
                    "Command executor refuses every change while a property draft is unapplied",checks);
            }
            finally { Field<TextBox>(window,"name").Text=piece.Name; }
            Check(!window.HasPendingCanvasOrPropertyWork,"Restoring the original name removes the draft again",checks);
        });
        // 6: incomplete recovery refuses every kind of edit.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var session=SessionOf(window);var before=window.Document.Clone();var ran=false;
            Set(window,"recoveryRestoreIncomplete",true);
            try
            {
                var edits=new[] { Spy(()=>ran=true),Spy(()=>ran=true,needsSelection:false,addsPiece:true),MoveEdit(1,1) };
                Check(edits.All(edit=>window.ApplyCommand(edit) is { Result:CommandResult.Refused,Guard:CommandGuards.Recovery } outcome && outcome.Message.Contains("Kurtarılan"))
                    && !ran && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0,
                    "Command executor refuses every change while a recovery restore is incomplete",checks);
            }
            finally { Set(window,"recoveryRestoreIncomplete",false); }
        });
        // 11: stamp (hand-built and captured).
        Scenario(window=>
        {
            var a=MarkedPiece("A");var b=MarkedPiece("B");
            window.LoadDocument(new ProjectDocument { Pieces=[a,b] });window.Select(a.Id);
            var session=SessionOf(window);var ran=false;
            var epoch=Field<Guid>(window,"projectEpoch");
            var wrongEpoch=new CommandStamp(window.Document,Guid.NewGuid(),window.SelectedId,window.Document.Clone());
            var before=window.Document.Clone();
            var stale=window.ApplyCommand(Spy(()=>ran=true,stamp:wrongEpoch));
            Check(stale is { Result:CommandResult.Stale,Guard:CommandGuards.Stamp } && stale.Message.Contains("yapılmadı") && !ran && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0,
                "Command executor refuses an edit whose stamp no longer matches",checks);
            var handBuilt=new CommandStamp(window.Document,epoch,window.SelectedId,window.Document.Clone());
            Check(window.ApplyCommand(Spy(()=>ran=true,stamp:handBuilt)) is { Result:CommandResult.Applied } && ran && session.UndoCount==1,
                "Command executor applies an edit whose hand-built stamp matches",checks);
            var captured=window.CaptureCommandStamp();
            window.Commit("Başka değişiklik",()=>window.Document.Name="Başka");
            ran=false;var afterOther=window.Document.Clone();var undo=session.UndoCount;
            Check(window.ApplyCommand(Spy(()=>ran=true,stamp:captured)) is { Result:CommandResult.Stale } && !ran
                && DocumentSession.ContentEquals(afterOther,window.Document) && session.UndoCount==undo,
                "Command executor refuses an edit after the project changed since the preview",checks);
            var selectionStamp=window.CaptureCommandStamp();window.Select(b.Id);
            Check(window.ApplyCommand(Spy(()=>ran=true,stamp:selectionStamp)) is { Result:CommandResult.Stale } && !ran,
                "Command executor refuses an edit after the selection changed since the preview",checks);
            // Undo brings equal content back in another document object; the stamp must still call it stale.
            var beforeUndo=window.CaptureCommandStamp();
            window.Commit("Geçici değişiklik",()=>window.Document.Name="Geçici");
            Check(session.Undo() && DocumentSession.ContentEquals(beforeUndo.Snapshot,window.Document)
                && window.ApplyCommand(Spy(()=>ran=true,stamp:beforeUndo)) is { Result:CommandResult.Stale } && !ran,
                "Command executor refuses an edit after undo even though the content is equal again",checks);
            // The same document object changed in place: only the content comparison can notice.
            var inPlace=window.CaptureCommandStamp();window.Document.Name+="?";
            Check(window.ApplyCommand(Spy(()=>ran=true,stamp:inPlace)) is { Result:CommandResult.Stale } && !ran,
                "Command executor refuses an edit when the same document changed in place since the preview",checks);
        });
        // Guard order: each block is removed in turn and the next guard in the ladder must answer.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var canvas=Field<CadCanvas>(window,"canvas");var session=SessionOf(window);var before=window.Document.Clone();var ran=false;
            var order=new List<string>();var expected=new[]
            {
                CommandGuards.MutationReview,CommandGuards.Recovery,CommandGuards.DxfImport,CommandGuards.Export,
                CommandGuards.Drag,CommandGuards.LargeValidation,CommandGuards.PendingWork,
            };
            // Canvas blocks first (RestorePendingDrawing needs the tool and clears any drag), then the window flags
            // (SetTool refuses while a recovery restore is incomplete).
            var cancellation=new CancellationTokenSource();
            try
            {
                window.SetTool("Çizgi");canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çizgi",[new Point2(1,2)]));
                SetCanvas(canvas,"dragId",piece.Id);SetCanvas(canvas,"largeVertexCancellation",cancellation);
                Set(window,"mainMutationReviewRequired",true);Set(window,"recoveryRestoreIncomplete",true);
                Set(window,"<DxfImportRunning>k__BackingField",true);Set(window,"<ExportRunning>k__BackingField",true);
                for(var step=0;step<expected.Length;step++)
                {
                    var outcome=window.ApplyCommand(Spy(()=>ran=true));
                    order.Add(outcome.Result==CommandResult.Refused ? outcome.Guard??"?" : outcome.Result.ToString());
                    switch(expected[step])
                    {
                        case CommandGuards.Recovery:Set(window,"recoveryRestoreIncomplete",false);break;
                        case CommandGuards.DxfImport:Set(window,"<DxfImportRunning>k__BackingField",false);break;
                        case CommandGuards.Export:Set(window,"<ExportRunning>k__BackingField",false);break;
                        case CommandGuards.Drag:SetCanvas(canvas,"dragId",null);break;
                        case CommandGuards.LargeValidation:SetCanvas(canvas,"largeVertexCancellation",null);break;
                        case CommandGuards.PendingWork:canvas.CancelDrawing();window.SetTool("Seç");break;
                    }
                }
                Check(order.SequenceEqual(expected) && !ran && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0,
                    "Command executor guards answer in ladder order: review, recovery, DXF import, export, drag, large validation, pending work ("+string.Join(", ",order)+")",checks);
                Check(window.ApplyCommand(Spy(()=>ran=true)) is { Result:CommandResult.Applied } && ran && session.UndoCount==1,
                    "Command executor applies once every block is gone",checks);
            }
            finally
            {
                Set(window,"mainMutationReviewRequired",false);Set(window,"recoveryRestoreIncomplete",false);
                Set(window,"<DxfImportRunning>k__BackingField",false);Set(window,"<ExportRunning>k__BackingField",false);
                SetCanvas(canvas,"dragId",null);SetCanvas(canvas,"largeVertexCancellation",null);cancellation.Dispose();
                canvas.CancelDrawing();window.SetTool("Seç");
            }
        });
        // A closing window refuses before anything else.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var before=window.Document.Clone();var ran=false;
            Set(window,"windowClosed",true);Set(window,"mainMutationReviewRequired",true);
            try
            {
                var closed=window.ApplyCommand(Spy(()=>ran=true));
                Check(closed is { Result:CommandResult.Refused,Guard:CommandGuards.Closed } && closed.Message.Contains("yapılmadı") && !ran
                    && DocumentSession.ContentEquals(before,window.Document) && Field<bool>(window,"mainMutationReviewRequired"),
                    "Command executor refuses an edit while the window is closing, before any refresh",checks);
            }
            finally { Set(window,"windowClosed",false);Set(window,"mainMutationReviewRequired",false); }
            Set(window,"recoveryClosing",true);
            try
            {
                Check(window.ApplyCommand(Spy(()=>ran=true)) is { Result:CommandResult.Refused,Guard:CommandGuards.Closed } && !ran
                    && DocumentSession.ContentEquals(before,window.Document),
                    "Command executor refuses an edit while the recovery record is being cleared on close",checks);
            }
            finally { Set(window,"recoveryClosing",false); }
        });
        // No change, rollback, planner skeleton.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var session=SessionOf(window);var before=window.Document.Clone();
            var same=window.ApplyCommand(new("Hiçbir şey","Deneme",_=> { }));
            Check(same is { Result:CommandResult.NoChange } && !same.Changed && same.Message.Contains("Değişiklik yok")
                && session.UndoCount==0 && DocumentSession.ContentEquals(before,window.Document),
                "Command executor reports no change and records no undo step when the edit changes nothing",checks);
            var failed=window.ApplyCommand(new("Bozuk","Deneme",doc=> { doc.Name="Yarım kaldı";throw new InvalidOperationException("deneme hatası"); }));
            Check(failed is { Result:CommandResult.Refused,Guard:CommandGuards.Commit } && failed.Message.Contains("deneme hatası")
                && session.UndoCount==0 && DocumentSession.ContentEquals(before,window.Document),
                "Command executor rolls back an edit that throws and reports why",checks);
            Check(window.PreviewCommand("120 x 80 dikdörtgen") is { Kind:CommandPreviewKind.Plan,Plan: not null }
                && window.PreviewCommand("  ").Kind==CommandPreviewKind.Empty && DocumentSession.ContentEquals(before,window.Document) && session.UndoCount==0,
                "Command preview plans the typed text and changes nothing",checks);
        });
        // 15: the project changed but the refresh failed afterwards.
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var session=SessionOf(window);var ran=false;
            window.AfterCommitBeforeRefreshForVerification=()=>throw new InvalidOperationException("injected refresh failure");
            CommandOutcome failedRefresh;
            try { failedRefresh=window.ApplyCommand(Spy(()=>ran=true)); }
            finally { window.AfterCommitBeforeRefreshForVerification=null; }
            Check(failedRefresh is { Result:CommandResult.Applied,RefreshFailed:true } && failedRefresh.Changed && ran
                && failedRefresh.Message.Contains("yenilenemedi") && window.Document.Name.EndsWith("!") && session.UndoCount==1
                && Field<bool>(window,"mainMutationReviewRequired"),
                "Command executor reports an applied change when the refresh fails afterwards",checks);
            var undo=session.UndoCount;ran=false;
            Check(window.ApplyCommand(Spy(()=>ran=true)) is { Result:CommandResult.Refused,Guard:CommandGuards.MutationReview } && !ran && session.UndoCount==undo,
                "Command executor refuses the next edit once after a failed refresh",checks);
            Check(window.ApplyCommand(Spy(()=>ran=true)) is { Result:CommandResult.Applied } && ran && session.UndoCount==undo+1,
                "Command executor accepts edits again after that one refusal",checks);
        });
        Check(windowsStable,"Command executor opens no window and no message box",checks);
    }

    // ---- OpenTextileToolsAt ------------------------------------------------------------------------------------

    private static void VerifyOpenTextileToolsAt(List<string> checks)
    {
        var guard=typeof(MainWindow).GetMethod("EnsurePropertyDraftsApplied",Private)!;
        Check(HasSafeGuardPrefix(typeof(MainWindow).GetMethod("OpenTextileToolsAt",Private)!,guard),
            "OpenTextileToolsAt checks drafts before any dialog",checks);
        Check(HasSafeGuardPrefix(typeof(MainWindow).GetMethod("OpenTextileTools",Private)!,guard),
            "OpenTextileTools still checks drafts before any dialog",checks);
        Scenario(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece] });window.Select(piece.Id);
            var count=Application.Current.Windows.Count;var stopped=false;
            Field<TextBox>(window,"name").Text="Uygulanmamış ad";
            try
            {
                try { window.OpenTextileToolsAt(1,"12"); }
                catch(InvalidOperationException ex) { stopped=ex.Message.Contains("taslağı"); }
                Check(stopped && Application.Current.Windows.Count==count,
                    "OpenTextileToolsAt blocks an unapplied draft without opening a window",checks);
            }
            finally { Field<TextBox>(window,"name").Text=piece.Name; }
            static string Text(TextileToolsWindow tools,string field)=>((TextBox)typeof(TextileToolsWindow).GetField(field,Private)!.GetValue(tools)!).Text;
            static int Tab(TextileToolsWindow tools)=>((TabControl)typeof(TextileToolsWindow).GetField("workshopTabs",Private)!.GetValue(tools)!).SelectedIndex;
            var contour=window.CreateTextileToolsAt(1,"12.5");
            var nesting=window.CreateTextileToolsAt(2,"1650");
            var plain=window.CreateTextileToolsAt(0,null);
            var ignored=window.CreateTextileToolsAt(0,"999");
            var invalid=false;var windowsBefore=Application.Current.Windows.Count;
            try { window.CreateTextileToolsAt(7,null); } catch(ArgumentOutOfRangeException) { invalid=true; }
            invalid&=Application.Current.Windows.Count==windowsBefore;
            try
            {
                Check(Tab(contour)==1 && Text(contour,"offsetAllowance")=="12.5" && Tab(nesting)==2 && Text(nesting,"basicWidth")=="1650"
                    && Tab(plain)==0 && Text(plain,"offsetAllowance")=="10" && Text(plain,"basicWidth")=="1500"
                    && Text(contour,"basicWidth")=="1500" && Text(nesting,"offsetAllowance")=="10"
                    && Tab(ignored)==0 && Text(ignored,"offsetAllowance")=="10" && Text(ignored,"basicWidth")=="1500" && invalid,
                    "OpenTextileToolsAt selects the tab and fills only that tab's input once",checks);
            }
            finally { contour.Close();nesting.Close();plain.Close();ignored.Close(); }
        });
    }
}
