using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TekstilCAD.Core;
using TekstilCAD.Core.Guide;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

// PR 8: side card, start tasks, next step, first steps and recent files.
internal static partial class SimpleViewVerification
{
    private static PatternPiece Rectangle(string name, double x, double y, double width, double height) => new()
    {
        Name = name, Points = [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)],
    };

    private static List<string> TextsOf(DependencyObject root) => Descendants<TextBlock>(root).Select(t => t.Text).ToList();

    private static TextBox CommandBox(MainWindow window)
        => Descendants<TextBox>((FrameworkElement)window.Content).Single(box => AutomationProperties.GetName(box) == "Komut kutusu");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static void VerifySideCard(string reportPath, List<string> checks)
    {
        VerifyStartTasks(checks);
        VerifySampleTask(checks);
        VerifyPieceTiles(checks);
        VerifyFirstStepsTick(reportPath, checks);
        VerifyContinueTask(reportPath, checks);
    }

    private static void VerifyStartTasks(List<string> checks) => WithFresh(fresh =>
    {
        fresh.SetGuideStateForVerification([], FirstStepsTracker.Fresh, true);
        fresh.LoadDocument(new ProjectDocument());
        Simple(fresh, true);
        Render(fresh, Temp("start-tasks-1440x900"), 1440, 900);
        var root = (FrameworkElement)fresh.Content; var card = fresh.SideCard!;
        var tasks = card.TaskButtons;
        var titles = tasks.Select(button => AutomationProperties.GetName(button)).ToList();
        var enabled = tasks.Select(button => button.IsEnabled).ToList();
        Render(fresh, Temp("start-tasks-1024x640"), 1024, 640);
        Check(card.StartTasksVisible && titles.SequenceEqual(["Ölçü yazarak başla", "Dosya aç", "Örnekle dene", "Kaldığım yerden devam et"])
            && enabled.SequenceEqual([true, true, true, false]) && card.NextStepText.Length > 10
            && card.StepsHeadingText == "İlk adımlar 0/6" && card.FirstStepsVisible && !card.PieceVisible,
            "Empty project shows the four start tasks and the next step sentence", checks);
        Render(fresh, Temp("start-tasks-1440x900"), 1440, 900);
        Check(tasks.All(button => button.ActualHeight >= 48 - .5 && FullyVisible(button, root)), "Start task buttons are at least 48 px high and fully visible at 1440x900", checks);
    });

    private static void VerifySampleTask(List<string> checks) => WithFresh(fresh =>
    {
        fresh.LoadDocument(new ProjectDocument());
        Simple(fresh, true);
        Render(fresh, Temp("sample-task"), 1440, 900);
        var example = fresh.SideCard!.TaskButtons.Single(button => AutomationProperties.GetName(button) == "Örnekle dene");
        Click(example);
        var loaded = fresh.Document.Pieces.Count == 2 && fresh.Document.Pieces[0].Name.Contains("örnek", StringComparison.Ordinal) && !fresh.SideCard.StartTasksVisible;

        // A project with unsaved work is not touched: the task is off, says why, and a forced click changes nothing.
        fresh.LoadDocument(new ProjectDocument());
        typeof(MainWindow).GetField("dirty", Private)!.SetValue(fresh, true);
        Invoke(fresh, "Refresh");
        var off = fresh.SideCard!.TaskButtons.Single(button => AutomationProperties.GetName(button) == "Örnekle dene");
        var disabled = !off.IsEnabled && TextsOf(off).Contains("Örnek yalnızca boş, yeni bir çalışmada açılır.");
        Click(off);
        var untouched = fresh.Document.Pieces.Count == 0 && F<TextBlock>(fresh, "status").Text.Contains("Örnek yalnızca boş", StringComparison.Ordinal);
        Check(loaded && disabled && untouched, "Sample task loads the example only into a clean empty project", checks);
    });

    private static void VerifyPieceTiles(List<string> checks) => WithFresh(fresh =>
    {
        fresh.LoadDocument(new ProjectDocument { Name = "Deneme", Pieces = [Rectangle("Parça 1", 30, 40, 120, 80)] });
        fresh.Select(fresh.Document.Pieces[0].Id);
        Simple(fresh, true);
        Render(fresh, Temp("piece-card-1440x900"), 1440, 900);
        var root = (FrameworkElement)fresh.Content; var card = fresh.SideCard!;
        var before = fresh.Document.Clone();
        var content = new[] { card.WidthTile, card.HeightTile, card.PositionTile }.Select(tile => string.Join("|", TextsOf(tile))).ToList();
        var texts = content.SequenceEqual(["Genişlik|120 mm", "Yükseklik|80 mm", "Konum|soldan 30 mm, yukarıdan 40 mm"]) && card.PieceNameText == "Parça 1";
        var visible = new[] { card.WidthTile, card.HeightTile, card.PositionTile }.All(tile => FullyVisible(tile, root) && tile.ActualHeight >= 48 - .5);
        Click(card.WidthTile);
        var filled = CommandBox(fresh).Text == "genişliği 120 yap";
        var unchanged = DocumentSession.ContentEquals(fresh.Document, before) && !F<TextBlock>(fresh, "saveState").Text.StartsWith("●", StringComparison.Ordinal);
        Check(texts && visible && filled && unchanged && card.PieceVisible && !card.StartTasksVisible, "Side card shows size and position of the selected piece", checks);
    });

    private static void VerifyFirstStepsTick(string reportPath, List<string> checks) => WithFresh(fresh =>
    {
        var path = Path.ChangeExtension(reportPath, ".simple-view-steps.tcad");
        fresh.SetGuideStateForVerification([], FirstStepsTracker.Fresh, false);
        fresh.LoadDocument(new ProjectDocument());
        Simple(fresh, true);
        fresh.ApplyCommand(new CommandEdit("Dikdörtgen çizildi.", "Dikdörtgen", document => document.Pieces.Add(Rectangle("Parça 1", 20, 20, 120, 80)), NeedsSelection: false, AddsPiece: true));
        var drawn = fresh.FirstSteps.IsDone(FirstStep.DrawPiece) && !fresh.FirstSteps.IsDone(FirstStep.ChangeSize);
        fresh.ApplyCommand(new CommandEdit("Genişlik değişti.", "Ölçü", document => document.Pieces[0].Points = [new(20, 20), new(115, 20), new(115, 100), new(20, 100)]));
        var sized = fresh.FirstSteps.IsDone(FirstStep.ChangeSize) && !fresh.FirstSteps.IsDone(FirstStep.MovePiece);
        fresh.ApplyCommand(new CommandEdit("Parça taşındı.", "Taşıma", document => document.Pieces[0].Points = [new(45, 20), new(140, 20), new(140, 100), new(45, 100)]));
        var moved = fresh.FirstSteps.IsDone(FirstStep.MovePiece) && !fresh.FirstSteps.IsDone(FirstStep.Undo);
        Invoke(fresh, "Undo");
        var undone = fresh.FirstSteps.IsDone(FirstStep.Undo) && !fresh.FirstSteps.IsDone(FirstStep.Save);
        Invoke(fresh, "SaveTo", path);
        Invoke(fresh, "Refresh");
        var saved = fresh.FirstSteps.IsDone(FirstStep.Save) && !fresh.FirstSteps.IsDone(FirstStep.ReopenFile) && fresh.FirstSteps.DoneCount == 5;
        Render(fresh, Temp("first-steps-1440x900"), 1440, 900);
        var heading = fresh.SideCard!.StepsHeadingText == "İlk adımlar 5/6" && fresh.RecentProjectFiles.SequenceEqual([path]);
        Check(drawn && sized && moved && undone && saved && heading, "First steps tick as the project is drawn, resized, moved, undone and saved", checks);
    });

    private static void VerifyContinueTask(string reportPath, List<string> checks)
    {
        var path = Path.ChangeExtension(reportPath, ".simple-view-recent.tcad");
        WithFresh(fresh =>
        {
            fresh.LoadDocument(new ProjectDocument { Name = "Kayıtlı çalışma", Pieces = [Rectangle("Kayıtlı parça", 10, 10, 100, 60)] });
            Invoke(fresh, "SaveTo", path);
        });
        WithFresh(fresh =>
        {
            fresh.SetGuideStateForVerification([path], FirstStepsTracker.Fresh, false);
            fresh.LoadDocument(new ProjectDocument());
            Simple(fresh, true);
            Render(fresh, Temp("continue-task"), 1440, 900);
            var task = fresh.SideCard!.TaskButtons.Single(button => AutomationProperties.GetName(button) == "Kaldığım yerden devam et");
            var named = task.IsEnabled && TextsOf(task).Contains(Path.GetFileName(path));

            // With unsaved work the guarded path asks first; declining leaves the project as it was.
            var asked = false;
            fresh.ConfirmOpenDiscardForVerification = () => { asked = true; return (false, false); };
            typeof(MainWindow).GetField("dirty", Private)!.SetValue(fresh, true);
            Click(task);
            PumpUntil(() => asked || fresh.Document.Pieces.Count > 0);
            var declined = asked && fresh.Document.Pieces.Count == 0;

            fresh.ConfirmOpenDiscardForVerification = () => (true, false);
            Click(task);
            PumpUntil(() => fresh.Document.Pieces.Count > 0);
            var opened = fresh.Document.Pieces.Count == 1 && fresh.Document.Pieces[0].Name == "Kayıtlı parça" && fresh.RecentProjectFiles.SequenceEqual([path]);
            Check(named && declined && opened, "Continue task opens the most recent file through the same guarded path", checks);
        });
    }
}
