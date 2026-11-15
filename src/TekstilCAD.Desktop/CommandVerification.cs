using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using TekstilCAD.Core;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

/// <summary>
/// Checks for the command executor groundwork. Every scenario builds its own fresh window (never shown) so the
/// long-lived verification window and its order-dependent state stay untouched.
/// The expected documents are written out by hand (exact quarter-turn and mirror arithmetic on whole numbers), not
/// computed with the code under test, so they describe the behaviour the buttons and the draft path had before
/// the shared operation table and TranslatePiece existed.
/// </summary>
internal static class CommandVerification
{
    private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;

    internal static void Run(MainWindow studio,List<string> checks)
    {
        VerifyOrthogonalButtonsKeepTheirDocuments(checks);
        VerifyPositionDraftsKeepTheirDocuments(checks);
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
}
