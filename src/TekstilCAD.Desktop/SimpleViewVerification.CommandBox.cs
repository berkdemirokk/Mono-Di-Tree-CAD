using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using TekstilCAD.Core.Guide;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

/// <summary>Headless checks of the real command box inside the simple view: no placeholder, preview, ghost, apply, undo, voice button, fills and chips.</summary>
internal static partial class SimpleViewVerification
{
    private const string BoxCommand = "120 x 80 dikdörtgen";

    private static CommandBarView? InlineBox(MainWindow window) => Descendants<CommandBarView>((FrameworkElement)window.Content).SingleOrDefault();

    private static void VerifyCommandBox(string reportPath, List<string> checks)
    {
        VerifyBoxIsReal(checks);
        VerifyBoxPreviewApplyUndo(reportPath, checks);
        VerifyBoxFills(checks);
        VerifyBoxChips(checks);
    }

    // The tree holds the real view (one box, one voice button) and nothing of the former stand-in.
    private static void VerifyBoxIsReal(List<string> checks) => WithFresh(fresh =>
    {
        fresh.LoadDocument(new ProjectDocument());
        Simple(fresh, true);
        Render(fresh, Temp("box-real-1024x640"), 1024, 640);
        var root = (FrameworkElement)fresh.Content;
        var view = InlineBox(fresh);
        var boxes = Descendants<TextBox>(root).Where(b => AutomationProperties.GetName(b) == "Komut kutusu").ToList();
        var stale = Descendants<TextBox>(root).Any(b => AutomationProperties.GetHelpText(b).Contains("henüz bağlı değil", StringComparison.Ordinal));
        Check(view is not null && boxes.Count == 1 && ReferenceEquals(boxes[0], view.Box) && !stale && fresh.CommandBoxView is null,
            "Simple view contains the real command bar view and no placeholder box", checks);

        var voice = Descendants<Button>(root).Where(b => AutomationProperties.GetName(b) == "Sesle yaz").ToList();
        Check(voice.Count == 1 && view is not null && ReferenceEquals(voice[0], view.VoiceButton) && FullyVisible(voice[0], root),
            "Simple view command box has the Sesle yaz button", checks);

        var names = new[] { "Proje aç", "Projeyi kaydet", "Tüm araçlar" };
        var row = names.Select(n => Descendants<Button>(root).FirstOrDefault(b => AutomationProperties.GetName(b) == n)).ToList();
        Check(view is not null && FullyVisible(view.Box, root) && row.All(b => b is not null && FullyVisible(b, root))
            && row.All(b => !BoundsInRoot(b!, root).IntersectsWith(BoundsInRoot(view.Box, root))),
            "Command box, Aç, Kaydet and Tüm araçlar all fit side by side at 1024x640", checks);

        // Ctrl+K focuses the inline box and opens no second one.
        var handled = fresh.HandleCommandBoxShortcut(ModifierKeys.Control, Key.K);
        Check(handled && !fresh.IsCommandBoxOpen && fresh.CommandBoxView is null && view is not null && view.LastFocusRequest is not null,
            "Ctrl+K focuses the inline command box in the simple view instead of opening a popup", checks);
    });

    // Typing shows the preview and the ghost without touching the document; the second Enter applies once; undo removes it.
    private static void VerifyBoxPreviewApplyUndo(string reportPath, List<string> checks)
    {
        WithFresh(fresh =>
        {
            fresh.LoadDocument(new ProjectDocument());
            Simple(fresh, true);
            Render(fresh, Temp("box-apply"), 1440, 900);
            var view = InlineBox(fresh)!; view.AutoPreview = false;
            var canvas = F<CadCanvas>(fresh, "canvas"); var session = F<DocumentSession>(fresh, "session");
            var before = fresh.Document.Clone(); var undoBefore = session.UndoCount;
            view.SetText(BoxCommand); view.HandleKey(Key.Enter, ModifierKeys.None);
            var previewed = view.State == CommandBarState.Preview && view.ShownPlan is not null && canvas.Ghost is not null;
            var untouched = DocumentSession.ContentEquals(fresh.Document, before) && session.UndoCount == undoBefore && fresh.Document.Pieces.Count == 0;
            Render(fresh, ReportPng(reportPath, 1440, 900), 1440, 900);
            view.HandleKey(Key.Enter, ModifierKeys.None);
            var applied = fresh.Document.Pieces.Count == 1 && session.UndoCount == undoBefore + 1;
            Invoke(fresh, "Undo");
            var undone = fresh.Document.Pieces.Count == 0 && DocumentSession.ContentEquals(fresh.Document, before);
            Check(previewed && untouched && applied && undone,
                "Simple view command box previews with a ghost, applies one piece on the second Enter and undo removes it"
                + (previewed && untouched && applied && undone ? "" : $" [previewed={previewed} untouched={untouched} applied={applied} undone={undone}]"), checks);
        });

        // The card that grows past the box never shrinks the canvas below the simple view floors.
        foreach (var (width, height, floor) in new[] { (1024, 640, Floor1024), (1440, 900, Floor1440) })
            WithFresh(fresh =>
            {
                fresh.LoadDocument(new ProjectDocument());
                Simple(fresh, true);
                Render(fresh, Temp($"box-floor-{width}"), width, height);
                var view = InlineBox(fresh)!; view.AutoPreview = false;
                view.SetText(BoxCommand); view.HandleKey(Key.Enter, ModifierKeys.None);
                if (width == 1024) Render(fresh, ReportPng(reportPath, width, height), width, height);
                else Render(fresh, Temp("box-floor-shown"), width, height);
                var canvas = F<CadCanvas>(fresh, "canvas");
                checks.Add($"[measured] simple view canvas height {canvas.ActualHeight:0.#} px with a command preview shown at {width}x{height}");
                CheckFloor(canvas.ActualHeight, floor, $"Canvas keeps at least {floor} px of height in the simple view while the command preview is shown at {width}x{height}", checks);
                var root = (FrameworkElement)fresh.Content;
                Check(view.ApplyButton.IsVisible && FullyVisible(view.ApplyButton, root) && FullyVisible(view.CancelButton, root),
                    $"Preview card with Uygula and Vazgeç stays fully visible at {width}x{height}", checks);
            });
    }

    private static string ReportPng(string reportPath, int width, int height)
        => Path.ChangeExtension(reportPath, $".simple-view-command-preview-{width}x{height}.png");

    // An All tools "fill" entry and the side card task put their text into this box and apply nothing.
    private static void VerifyBoxFills(List<string> checks) => WithFresh(fresh =>
    {
        fresh.SetGuideStateForVerification([], FirstStepsTracker.Fresh, true);
        fresh.LoadDocument(new ProjectDocument());
        Simple(fresh, true);
        Render(fresh, Temp("box-fills"), 1440, 900);
        var view = InlineBox(fresh)!; var before = fresh.Document.Clone();
        fresh.OpenAllToolsSheet();
        var fill = SheetRows(fresh).First(r => r.Entry.Kind == CommandCatalogueKind.Fill && r.Row.IsEnabled);
        fill.Row.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var fromSheet = view.Box.Text == fill.Entry.Examples[0] && !fresh.IsAllToolsSheetOpen;
        view.SetText("");
        var task = fresh.SideCard!.TaskButtons.Single(b => AutomationProperties.GetName(b) == "Ölçü yazarak başla");
        task.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var text = view.Box.Text;
        var fromCard = text.Length > 0 && fresh.CreateCommandBarHost().Preview(text, true).Kind == CommandPreviewKind.Plan;
        Check(fromSheet && fromCard && DocumentSession.ContentEquals(fresh.Document, before),
            "All tools fill entry and the side card start task put their text into the real command box without applying it"
            + (fromSheet && fromCard ? "" : $" [fromSheet={fromSheet} fromCard={fromCard} text={text}]"), checks);
    });

    // Every example chip shown in the simple view (empty project, piece not selected, piece selected) parses to a plan.
    private static void VerifyBoxChips(List<string> checks) => WithFresh(fresh =>
    {
        var host = fresh.CreateCommandBarHost();
        var bad = new List<string>(); var seen = 0;
        void Scan(string state)
        {
            Invoke(fresh, "Refresh");
            foreach (var chip in InlineBox(fresh)!.ChipButtons)
            {
                var text = (string)chip.Tag; seen++;
                if (host.Preview(text, true).Kind != CommandPreviewKind.Plan) bad.Add($"{state}: {text}");
            }
        }
        fresh.LoadDocument(new ProjectDocument());
        Simple(fresh, true);
        Scan("empty");
        fresh.LoadDocument(new ProjectDocument { Pieces = [new PatternPiece { Name = "Parça 1", Points = [new(30, 40), new(150, 40), new(150, 120), new(30, 120)] }] });
        Scan("unselected");
        fresh.Select(fresh.Document.Pieces[0].Id);
        Scan("selected");
        Check(seen >= 6 && bad.Count == 0, "Every example chip in the simple view command box parses to a plan"
            + (seen >= 6 && bad.Count == 0 ? "" : $" [{seen} chips; {string.Join("; ", bad)}]"), checks);
    });
}
