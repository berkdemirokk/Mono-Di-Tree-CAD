using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using static TekstilCAD.Desktop.DesktopVerification;

namespace TekstilCAD.Desktop;

/// <summary>Headless checks of the keyboard and screen reader side of the simple view (one check per point). Same fresh, never shown windows.</summary>
internal static partial class SimpleViewVerification
{
    private static void VerifyAccessibility(List<string> checks)
    {
        VerifySheetKeyboardAndCount(checks);
        VerifySaveHelpText(checks);
        VerifyOutcomeAnnouncement(checks);
        VerifyAcceleratorKeys(checks);
        VerifyDisabledHistoryButtons(checks);
    }

    private static Button ByName(MainWindow window, string name)
        => Descendants<Button>((FrameworkElement)window.Content).First(b => AutomationProperties.GetName(b) == name);

    // While the page is open the rail and the canvas are out of the Tab and arrow key order; closing restores them. The page
    // announces its result count, or that nothing was found, through a polite live region.
    private static void VerifySheetKeyboardAndCount(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        var under = new FrameworkElement[] { F<ScrollViewer>(fresh, "railScroller"), F<Border>(fresh, "canvasShellBorder") };
        var before = under.Select(e => (KeyboardNavigation.GetTabNavigation(e), KeyboardNavigation.GetDirectionalNavigation(e))).ToList();
        fresh.OpenAllToolsSheet();
        var blocked = under.All(e => KeyboardNavigation.GetTabNavigation(e) == KeyboardNavigationMode.None && KeyboardNavigation.GetDirectionalNavigation(e) == KeyboardNavigationMode.None);
        var sheet = fresh.AllToolsSheetRoot!;
        var box = Logical<TextBox>(sheet).Single(t => AutomationProperties.GetName(t) == "Araç ara");
        box.Text = "ayna";
        var live = Logical<TextBlock>(sheet).Where(t => AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Polite).ToList();
        var count = live.FirstOrDefault(t => t.Visibility == Visibility.Visible && t.Text.EndsWith(" sonuç", StringComparison.Ordinal));
        var listed = SheetRows(fresh).Count;
        var countOk = count is not null && count.Text == listed + " sonuç" && AutomationProperties.GetName(count) == count.Text;
        box.Text = "zzzzqqq";
        var none = live.FirstOrDefault(t => t.Visibility == Visibility.Visible);
        var noneOk = none is not null && AutomationProperties.GetName(none).StartsWith("Sonuç yok.", StringComparison.Ordinal) && live.Count(t => t.Visibility == Visibility.Visible) == 1;
        fresh.CloseAllToolsSheet();
        var restored = under.Select(e => (KeyboardNavigation.GetTabNavigation(e), KeyboardNavigation.GetDirectionalNavigation(e))).SequenceEqual(before);
        Check(blocked && countOk && noneOk && restored,
            "All tools page takes the rail and the canvas out of the Tab order while open (restored on close) and announces the result count or 'Sonuç yok' politely"
            + (blocked && countOk && noneOk && restored ? "" : $" [blocked={blocked}; count={countOk}; none={noneOk}; restored={restored}]"), checks);
    });

    // The Kaydet button describes the saved or unsaved state.
    private static void VerifySaveHelpText(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        var save = ByName(fresh, "Projeyi kaydet");
        var clean = AutomationProperties.GetHelpText(save);
        var edit = new CommandEdit("Dikdörtgen çizildi: 120 × 80 mm.", "Dikdörtgen",
            document => document.Pieces.Add(new PatternPiece { Name = "Parça 9", Points = [new(300, 0), new(420, 0), new(420, 80), new(300, 80)] }),
            NeedsSelection: false, AddsPiece: true);
        fresh.ApplyCommand(edit);
        var dirty = AutomationProperties.GetHelpText(save);
        Check(clean == "Kaydedilmemiş değişiklik yok." && dirty == "Kaydedilmemiş değişiklikler var." && AutomationProperties.GetName(save) == "Projeyi kaydet",
            "Kaydet button's help text follows the saved and unsaved state" + (clean != dirty ? "" : $" [{clean} / {dirty}]"), checks);
    });

    // An outcome reaches the polite status line (name = message); the cursor read-out leaves it untouched.
    private static void VerifyOutcomeAnnouncement(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        var status = F<TextBlock>(fresh, "status");
        fresh.SetStatus("Parça silindi · deneme.");
        var spoken = AutomationProperties.GetName(status) == "Parça silindi · deneme.";
        fresh.SetStatus("Çizgi aracı  ·  X 12 mm  Y 8 mm  ·  Yakınlaşma 100%", announce: false);
        var cursorSilent = AutomationProperties.GetName(status) == "Parça silindi · deneme." && status.Text.Contains("X 12 mm", StringComparison.Ordinal);
        var live = AutomationProperties.GetLiveSetting(status) == AutomationLiveSetting.Polite;
        Simple(fresh, false);
        var restored = AutomationProperties.GetName(status) == "Uygulama durumu";
        Check(spoken && cursorSilent && live && restored,
            "Simple view announces outcome messages through the polite status line and not the cursor read-out"
            + (spoken && cursorSilent && live && restored ? "" : $" [spoken={spoken}; cursor={cursorSilent}; live={live}; restored={restored}]"), checks);
    });

    // Aç, Kaydet, Geri al, Yinele, the view switch and the six tools expose their shortcut; the detailed layout is back to none.
    private static void VerifyAcceleratorKeys(List<string> checks) => WithFresh(fresh =>
    {
        Simple(fresh, true);
        var tools = F<Dictionary<string, Button>>(fresh, "toolButtons");
        var expected = new (Button Button, string Key)[]
        {
            (ByName(fresh, "Proje aç"), "Ctrl+O"), (ByName(fresh, "Projeyi kaydet"), "Ctrl+S"), (F<Button>(fresh, "undoCommand"), "Ctrl+Z"), (F<Button>(fresh, "redoCommand"), "Ctrl+Y"),
            (ByName(fresh, "Ayrıntılı görünüme geç"), "F9"), (tools["Seç"], "V"), (tools["Çizgi"], "L"), (tools["Çoklu çizgi"], "P"), (tools["Dikdörtgen"], "R"), (tools["Bézier"], "B"), (tools["Yay"], "A"),
        };
        var wrong = expected.Where(e => AutomationProperties.GetAcceleratorKey(e.Button) != e.Key).Select(e => AutomationProperties.GetName(e.Button)).ToList();
        Simple(fresh, false);
        var leftover = new[] { F<Button>(fresh, "undoCommand"), F<Button>(fresh, "redoCommand"), tools["Seç"] }.Count(b => AutomationProperties.GetAcceleratorKey(b).Length > 0);
        Check(wrong.Count == 0 && leftover == 0, "Aç, Kaydet, Geri al, Yinele, the view switch and the six tools expose their keyboard shortcut in the simple view only"
            + (wrong.Count == 0 && leftover == 0 ? "" : $" [{string.Join(", ", wrong)}; leftover={leftover}]"), checks);
    });

    // Geri al and Yinele stay focusable without history and answer with the status sentence; the result row's Geri al is 44 px high.
    private static void VerifyDisabledHistoryButtons(List<string> checks) => WithFresh(fresh =>
    {
        var session = F<DocumentSession>(fresh, "session");
        var undo = F<Button>(fresh, "undoCommand"); var redo = F<Button>(fresh, "redoCommand"); var status = F<TextBlock>(fresh, "status");
        Simple(fresh, true);
        var empty = session.UndoCount == 0 && session.RedoCount == 0;
        var focusable = undo.IsEnabled && redo.IsEnabled && undo.Focusable && redo.Focusable
            && AutomationProperties.GetHelpText(undo) == "Geri alınacak işlem yok." && AutomationProperties.GetHelpText(redo) == "Yinelenecek işlem yok.";
        undo.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var undoSaid = status.Text == "Geri alınacak işlem yok.";
        redo.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var redoSaid = status.Text == "Yinelenecek işlem yok.";
        var box = InlineBox(fresh)!;
        box.RefreshChips(true);
        var resultUndo = box.AllButtons.First(b => b.Content as string == "Geri al");
        var tall = resultUndo.MinHeight >= 44;
        Simple(fresh, false);
        var restored = !undo.IsEnabled && !redo.IsEnabled && undo.Opacity == 1 && AutomationProperties.GetHelpText(undo).Length == 0;
        Check(empty && focusable && undoSaid && redoSaid && tall && restored,
            "Geri al and Yinele stay focusable without history and answer with the status sentence; the result row's Geri al is at least 44 px high"
            + (empty && focusable && undoSaid && redoSaid && tall && restored ? "" : $" [empty={empty}; focusable={focusable}; undo={undoSaid}; redo={redoSaid}; tall={tall}; restored={restored}]"), checks);
    });
}
