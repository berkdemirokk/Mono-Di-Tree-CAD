using System.Windows.Controls;
using System.Windows;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

// PR 3b: planner plans applied by the executor. Expected values are written out by hand (whole-number geometry).
internal static partial class CommandVerification
{
    private static Plan PlanOf(MainWindow window,string text)
        =>window.PreviewCommand(text,true).Plan ?? throw new InvalidOperationException($"No plan for “{text}”: {window.PreviewCommand(text,true).Text}");

    private static Bounds2 BoundsOf(MainWindow window,int index)=>Geometry.Bounds(window.Document.Pieces[index].Points);

    private static void VerifyPlanExecution(List<string> checks)
    {
        var stable=true;
        void Run(Action<MainWindow> body)=>WithFreshWindow(window=>
        {
            var count=Application.Current.Windows.Count;
            body(window);
            stable&=Application.Current.Windows.Count==count && !window.IsVisible;
        });
        // 1 + 2: rectangle on an empty project, then a second one beside it.
        Run(window=>
        {
            window.LoadDocument(new ProjectDocument());
            var session=SessionOf(window);
            var preview=window.PreviewCommand("120 x 80 dikdörtgen");
            Check(preview.Kind==CommandPreviewKind.Plan && preview.Plan!.Preview.Contains("120") && preview.Plan.Preview.Contains("80") && window.Document.Pieces.Count==0 && session.UndoCount==0,
                "Command preview of 120 x 80 dikdörtgen names the size and leaves the project empty",checks);
            var outcome=window.ApplyCommand(preview.Plan!);
            var bounds=window.Document.Pieces.Count==1 ? BoundsOf(window,0) : default;
            Check(outcome is { Result:CommandResult.Applied,RefreshFailed:false } && window.Document.Pieces.Count==1
                && bounds.MinX==20 && bounds.MinY==20 && bounds.Width==120 && bounds.Height==80
                && session.UndoCount==1 && window.Selected?.Id==window.Document.Pieces[0].Id,
                "Command executor draws a 120 x 80 rectangle as one undo step and selects the new piece",checks);
            var second=window.ApplyCommand(PlanOf(window,"50 x 40 dikdörtgen"));
            var firstBounds=BoundsOf(window,0);var secondBounds=window.Document.Pieces.Count==2 ? BoundsOf(window,1) : default;
            Check(second.Result==CommandResult.Applied && window.Document.Pieces.Count==2
                && secondBounds.MinX==firstBounds.MaxX+20 && secondBounds.MinY==firstBounds.MinY && secondBounds.Width==50 && secondBounds.Height==40 && session.UndoCount==2,
                "Command executor places the second piece 20 mm right of the first piece",checks);
            Check(session.Undo() && window.Document.Pieces.Count==1 && session.Undo() && window.Document.Pieces.Count==0,
                "Two rectangle commands are undone one step at a time",checks);
        });
        // 7 + 8: rotation and mirror equal the buttons.
        foreach(var (typed,label) in new[] { ("90 sağa döndür","90° sağa"),("90 sola döndür","90° sola"),("yarım tur","180° döndür"),("aynala","Aynala"),("alt üst aynala","Yatay aynala") })
        {
            Run(window=>
            {
                var piece=MarkedPiece();var rule=RuleFor(piece);
                window.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });window.Select(piece.Id);Layout(window);
                var viaBox=window.Document.Clone();
                var outcome=window.ApplyCommand(PlanOf(window,typed));
                var afterBox=window.Document.Clone();
                var boxUndo=SessionOf(window).UndoCount;
                SessionOf(window).Undo();
                var button=Descendants<Button>((FrameworkElement)window.Content).Single(b=>Equals(b.Content,label));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(outcome.Result==CommandResult.Applied && boxUndo==1 && DocumentSession.ContentEquals(afterBox,window.Document) && !DocumentSession.ContentEquals(viaBox,afterBox),
                    $"Command executor “{typed}” gives exactly the document the {label} button gives",checks);
            });
        }
        // 3: a move by command keeps marks, grainline and grading rule (the same translation the position boxes use).
        Run(window=>
        {
            var piece=MarkedPiece();var rule=RuleFor(piece);
            window.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[rule] });window.Select(piece.Id);
            var outcome=window.ApplyCommand(PlanOf(window,"25 mm sağa 10 mm aşağı taşı"));
            var current=window.Document.Pieces.Single();
            Check(outcome.Result==CommandResult.Applied && current.Points.SequenceEqual(Rectangle.Select(p=>new Point2(p.X+25,p.Y+10)))
                && current.Notches is { Count:2 } marks && marks[0].Position==new Point2(25,10) && current.Grainline is { } grain && grain.Start==new Point2(30,12)
                && window.Document.GradingRules.Single().SourcePoints.SequenceEqual(current.Points) && outcome.Caveats is { Count:>0 },
                "Command executor “taşı” moves contour, marks, grainline and rule together and keeps the caveat",checks);
            var moved=window.ApplyCommand(PlanOf(window,"x 100 y 50 konumuna taşı"));
            var bounds=Geometry.Bounds(window.Document.Pieces.Single().Points);
            Check(moved.Result==CommandResult.Applied && bounds.MinX==100 && bounds.MinY==50,
                "Command executor moves the top left corner to the typed position",checks);
        });
        // 9: resize keeps the top left corner and refuses curve pieces.
        Run(window=>
        {
            var rect=new PatternPiece { Name="Düz",Points=[new(10,10),new(110,10),new(110,90),new(10,90)] };
            window.LoadDocument(new ProjectDocument { Pieces=[rect] });window.Select(rect.Id);
            var outcome=window.ApplyCommand(PlanOf(window,"genişliği 95 yap"));
            var bounds=Geometry.Bounds(window.Document.Pieces.Single().Points);
            Check(outcome.Result==CommandResult.Applied && bounds.MinX==10 && bounds.MinY==10 && bounds.Width==95 && bounds.Height==80 && SessionOf(window).UndoCount==1,
                "Command executor resize keeps the top left corner and the other side",checks);
            var curved=new PatternPiece { Name="Eğri",Points=[new(0,0),new(100,0),new(100,50),new(0,50)],Curves=[new CurveSegment.Line(new(0,0),new(100,0)),new CurveSegment.Line(new(100,0),new(100,50)),new CurveSegment.Line(new(100,50),new(0,50)),new CurveSegment.Line(new(0,50),new(0,0))] };
            window.LoadDocument(new ProjectDocument { Pieces=[curved] });window.Select(curved.Id);
            var refused=window.PreviewCommand("genişliği 95 yap",true);
            Check(refused.Kind==CommandPreviewKind.Refusal && refused.Notice!.Message.Length>0 && SessionOf(window).UndoCount==0,
                "Command resize of a curve piece is refused with a reason and changes nothing",checks);
        });
        // 10: delete needs the explicit button; undo restores.
        Run(window=>
        {
            var piece=MarkedPiece();window.LoadDocument(new ProjectDocument { Pieces=[piece],GradingRules=[RuleFor(piece)] });window.Select(piece.Id);
            var before=window.Document.Clone();var plan=PlanOf(window,"sil");
            var unconfirmed=window.ApplyCommand(plan);
            Check(plan.RequiresConfirmation && unconfirmed is { Result:CommandResult.Refused,Guard:CommandGuards.Confirm } && DocumentSession.ContentEquals(before,window.Document) && SessionOf(window).UndoCount==0,
                "Command executor delete changes nothing until confirmed",checks);
            var confirmed=window.ApplyCommand(plan,true);
            Check(confirmed.Result==CommandResult.Applied && window.Document.Pieces.Count==0 && window.Document.GradingRules.Count==0 && window.Selected is null
                && SessionOf(window).Undo() && DocumentSession.ContentEquals(before,window.Document),
                "Command executor confirmed delete removes piece and rule and undo restores them",checks);
        });
        // 11: a plan whose project changed since the preview is stale and applies nothing.
        Run(window=>
        {
            var a=MarkedPiece("A");window.LoadDocument(new ProjectDocument { Pieces=[a] });window.Select(a.Id);
            var plan=PlanOf(window,"25 sağa taşı");
            window.Commit("Başka değişiklik",()=>window.Document.Pieces[0].Name="Başka ad");
            var after=window.Document.Clone();var undo=SessionOf(window).UndoCount;
            var outcome=window.ApplyCommand(plan);
            Check(outcome is { Result:CommandResult.Stale } && DocumentSession.ContentEquals(after,window.Document) && SessionOf(window).UndoCount==undo,
                "Command executor refuses a plan whose stamp no longer matches",checks);
        });
        // A locked layer is checked by id for a piece named by the command, and a locked selection is refused.
        Run(window=>
        {
            var piece=MarkedPiece();
            window.LoadDocument(new ProjectDocument { Pieces=[piece],Layers=[new Layer { Id=Layer.DefaultId,Name="Katman 1",Locked=true }] });window.Select(piece.Id);
            var preview=window.PreviewCommand("25 sağa taşı",true);
            var applied=preview.Plan is { } plan ? window.ApplyCommand(plan) : null;
            Check((preview.Kind==CommandPreviewKind.Refusal || applied is { Result:CommandResult.Refused }) && window.Document.Pieces[0].Points.SequenceEqual(Rectangle) && SessionOf(window).UndoCount==0,
                "Command on a locked layer is refused before or at the executor and changes nothing",checks);
        });
        Check(stable,"Command plans open no window and no message box",checks);
    }
}
