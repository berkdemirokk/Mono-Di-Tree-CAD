using System.IO;
using TekstilCAD.Core;
using TekstilCAD.Core.Guide;
using TekstilCAD.Core.Theme;

namespace TekstilCAD.Desktop;

public partial class MainWindow
{
    internal static string DisplayName(string name) => SafePieceName(name);

    /// <summary>Interactive start only: the saved first steps and recent files, and whether this is the first launch.</summary>
    internal void LoadSimpleViewState(UiPreferences preferences, bool firstLaunch)
        => (simpleView ??= new SimpleViewController(this)).LoadState(preferences, firstLaunch);

    internal SideCardView? SideCard => simpleView?.Card;
    internal FirstStepsTracker FirstSteps => simpleView?.Steps ?? FirstStepsTracker.Fresh;
    internal IReadOnlyList<string> RecentProjectFiles => simpleView?.Recent ?? [];

    /// <summary>Verification only: sets the guide state without touching ui.json.</summary>
    internal void SetGuideStateForVerification(IEnumerable<string> recent, FirstStepsTracker steps, bool firstLaunch)
        => (simpleView ??= new SimpleViewController(this)).SetState(recent, steps, firstLaunch);

    private sealed partial class SimpleViewController
    {
        private static readonly string[] StepIds = ["draw", "size", "move", "undo", "save", "reopen"];
        private FirstStepsTracker steps = FirstStepsTracker.Fresh;
        private List<string> recent = [];
        private bool recentExists, firstLaunch, observed;
        private Guid? exampleEpoch;
        private Guid prevEpoch, prevSelected;
        private int prevPieces, prevUndo, prevRedo;
        private bool prevUnsaved;
        private string? prevFile;
        private Bounds2 prevBounds;

        internal FirstStepsTracker Steps => steps;
        internal IReadOnlyList<string> Recent => recent;
        internal SideCardView? Card => card;

        internal void LoadState(UiPreferences preferences, bool first)
        {
            recent = [.. preferences.Recent];
            steps = TrackerFrom(preferences.FirstSteps);
            firstLaunch = first;
            RefreshRecentExists();
        }

        internal void SetState(IEnumerable<string> files, FirstStepsTracker tracker, bool first)
        {
            recent = [.. files]; steps = tracker; firstLaunch = first;
            RefreshRecentExists();
        }

        private SideCardView BuildCard() => new(text => commandBar!.SetText(text), RunStartTask, DismissFirstSteps);

        private static FirstStepsTracker TrackerFrom(IEnumerable<string> ids)
        {
            var list = ids.ToList();
            var done = StepIds.Where(list.Contains).Select(id => "\"" + id + "\"");
            return FirstStepsTracker.FromJson($"{{\"v\":{FirstStepsTracker.JsonVersion},\"done\":[{string.Join(',', done)}],\"hidden\":{(list.Contains("hidden") ? "true" : "false")}}}");
        }

        private static List<string> IdsOf(FirstStepsTracker tracker)
        {
            var ids = StepIds.Where((_, i) => tracker.IsDone((FirstStep)i)).ToList();
            if (tracker.IsDismissed) ids.Add("hidden");
            return ids;
        }

        private void RefreshRecentExists()
        {
            try { recentExists = recent.Count > 0 && File.Exists(recent[0]); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { recentExists = false; }
        }

        // Interactive windows keep the first steps and the recent files in ui.json; verification windows never write it.
        private void Persist()
        {
            if (!w.simpleViewSwitching) return;
            (UiPreferences.Load(AppPaths.UiPreferencesPath) with { Recent = recent, FirstSteps = IdsOf(steps) }).Save(AppPaths.UiPreferencesPath);
        }

        private void Record(FirstStepEvent observedEvent)
        {
            var next = steps.Observe(observedEvent);
            if (next.Equals(steps)) return;
            steps = next; firstLaunch = false;
            Persist();
        }

        private void NoteFile(string path)
        {
            var next = RecentProjects.Add(recent, path).ToList();
            if (next.SequenceEqual(recent)) return;
            recent = next; RefreshRecentExists(); Persist();
        }

        // Watches the project between refreshes and ticks the first steps; runs whether or not the simple view is on.
        private void Observe()
        {
            var epoch = w.projectEpoch; var pieces = w.Document.Pieces.Count;
            var selected = w.Selected is { Points.Count: > 0 } piece ? piece : null;
            var bounds = selected is null ? default : Geometry.Bounds(selected.Points);
            var unsaved = w.HasUnsavedDocumentWork; var file = w.filePath; var undo = w.session.UndoCount; var redo = w.session.RedoCount;
            if (observed)
            {
                if (epoch != prevEpoch)
                {
                    if (file is not null) { Record(FirstStepEvent.ProjectReopened); NoteFile(file); }
                }
                else
                {
                    var stepBack = undo < prevUndo && redo > prevRedo;
                    var moved = undo < prevUndo || redo > prevRedo;
                    if (stepBack) Record(FirstStepEvent.UndoDone);
                    if (!moved)
                    {
                        if (pieces > prevPieces) Record(FirstStepEvent.PieceDrawn);
                        if (selected is not null && selected.Id == prevSelected)
                        {
                            var sizeChanged = Math.Abs((bounds.MaxX - bounds.MinX) - (prevBounds.MaxX - prevBounds.MinX)) > 1e-9 || Math.Abs((bounds.MaxY - bounds.MinY) - (prevBounds.MaxY - prevBounds.MinY)) > 1e-9;
                            var positionChanged = Math.Abs(bounds.MinX - prevBounds.MinX) > 1e-9 || Math.Abs(bounds.MinY - prevBounds.MinY) > 1e-9;
                            if (sizeChanged) Record(FirstStepEvent.SizeChanged); else if (positionChanged) Record(FirstStepEvent.PieceMoved);
                        }
                    }
                    if (prevUnsaved && !unsaved && file is not null) { Record(FirstStepEvent.ProjectSaved); NoteFile(file); }
                    else if (file is not null && file != prevFile) NoteFile(file);
                }
            }
            (observed, prevEpoch, prevPieces, prevSelected, prevBounds, prevUnsaved, prevFile, prevUndo, prevRedo) =
                (true, epoch, pieces, selected?.Id ?? Guid.Empty, bounds, unsaved, file, undo, redo);
        }

        private GuideSituation Situation(PatternPiece? selected, int pieceCount) => new()
        {
            PieceCount = pieceCount, IsEmptyProject = pieceCount == 0, HasSelectedPiece = selected is not null,
            HasUnsavedChanges = w.HasUnsavedWork, FilePathKnown = w.filePath is not null,
            HasRecoveryNotice = w.recoveryNotice.Visibility == System.Windows.Visibility.Visible,
            ImportedWithWarnings = w.dxfReview.Visibility == System.Windows.Visibility.Visible,
            IsFirstLaunch = firstLaunch, ActiveToolName = w.ActiveTool, HasPendingDrawing = w.canvas.HasPendingDrawing,
            SelectedCanResize = selected is not null && IsPlainRectangle(selected),
            ExampleLoaded = exampleEpoch == w.projectEpoch && w.filePath is null, HasTriedEditing = steps.HasTriedEditing,
        };

        // A closed, axis-parallel, four-point piece without grain line, corner marks, curve data or grading rule.
        private bool IsPlainRectangle(PatternPiece piece)
        {
            if (!piece.Closed || piece.Points.Count != 4 || piece.Grainline is not null || piece.Notches is { Count: > 0 } || piece.Curves is { Count: > 0 }) return false;
            if (w.Document.GradingRules.Any(rule => rule.SourcePieceId == piece.Id)) return false;
            var p = piece.Points;
            for (var i = 0; i < 4; i++)
            {
                var a = p[i]; var b = p[(i + 1) % 4];
                if (a.X != b.X && a.Y != b.Y) return false;
            }
            return true;
        }

        private string? RecentName => recentExists && recent.Count > 0 ? Path.GetFileName(recent[0]) : null;

        private void UpdateCard(PatternPiece? selected, int pieceCount, bool unsaved)
        {
            var situation = Situation(selected, pieceCount);
            card!.Update(GuideRules.NextStep(situation), StartTasks.For(situation, RecentName), selected, pieceCount, steps);
        }

        private void RunStartTask(StartTask task)
        {
            try
            {
                switch (task.Id)
                {
                    case "type-size": commandBar!.SetText(task.SuggestedCommand ?? "120 x 80 dikdörtgen"); break;
                    case "open-file": RaiseClick("Aç…"); break;
                    case "try-example": LoadExample(); break;
                    case "continue-last": ContinueLast(); break;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                w.SetStatus("İşlem tamamlanamadı · " + ex.Message);
            }
        }

        // Loads the built-in example only into a clean, empty project (the one place a project changes without a preview).
        internal bool LoadExample()
        {
            var situation = Situation(null, w.Document.Pieces.Count);
            var task = StartTasks.For(situation, RecentName).FirstOrDefault(t => t.Id == "try-example");
            if (task is not { Enabled: true })
            {
                w.SetStatus(task?.DisabledReason ?? "Örnek yalnızca boş, yeni bir projede açılır.");
                return false;
            }
            w.LoadDocument(Sample());
            exampleEpoch = w.projectEpoch;
            w.SetStatus("Hazır örnek açıldı; kaydederseniz yeni bir proje olur.");
            w.Refresh();
            return true;
        }

        // Opens the most recent file through the same guarded path as the Aç button (it asks before discarding work).
        internal Task? ContinueLast()
        {
            RefreshRecentExists();
            if (recent.Count == 0 || !recentExists)
            {
                w.SetStatus("Son çalışma bulunamadı; Aç düğmesiyle seçin.");
                w.Refresh();
                return null;
            }
            return OpenRecent(recent[0]);
        }

        private async Task OpenRecent(string path)
        {
            try { await w.OpenProjectFileAsync(path); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            { w.SetStatus("Dosya açılamadı · " + ex.Message); }
        }

        private void DismissFirstSteps()
        {
            Record(FirstStepEvent.Dismissed);
            w.Refresh();
        }
    }
}
