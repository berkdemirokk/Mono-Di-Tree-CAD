using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Desktop;

/// <summary>Hidden, workspace-isolated child process used only by the crash-recovery script.</summary>
internal static class RecoveryCrashProbe
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ProjectName = "Async crash probe";
    private const string DraftName = "Unapplied draft";
    private const string InvalidX = "invalid coordinate";
    private static MainWindow? heldWindow;
    private static string Prepare(string path)
    {
        var directory = Path.GetFullPath(path);
        AppPaths.UseVerificationDirectory(Path.Combine(directory, "appdata"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal static void Start(string path)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var directory = Prepare(path);
                var studio = new MainWindow(); // Never shown. Its lease must remain held until process termination.
                heldWindow = studio;
                ((DispatcherTimer)typeof(MainWindow).GetField("autosaveTimer", PrivateInstance)!.GetValue(studio)!).Stop();
                var boundaryMode = File.Exists(Path.Combine(directory, "pre-rename-mode"));
                if (boundaryMode)
                {
                    var commits = 0;
                    var scopedStore = new RecoveryStore(AppPaths.RecoveryDirectory, () =>
                    {
                        if (Interlocked.Increment(ref commits) != 2) return;
                        File.WriteAllText(Path.Combine(directory, "pre-rename-ready"), "flushed");
                        // Parent normally terminates this child at this barrier. A bounded wait
                        // keeps an orphaned verification process from running indefinitely.
                        if (!new ManualResetEventSlim(false).Wait(TimeSpan.FromSeconds(120)))
                            throw new TimeoutException("Pre-rename verification timed out.");
                    });
                    await studio.SetRecoveryForVerificationAsync(scopedStore, new RecoveryWriteCoordinator(scopedStore));
                }
                var piece = new PatternPiece { Name = "Base piece", Points = [new(0, 0), new(10, 0), new(10, 10)] };
                studio.LoadDocument(new ProjectDocument { Name = ProjectName, Pieces = [piece] });
                studio.Select(piece.Id);
                ((TextBox)typeof(MainWindow).GetField("name", PrivateInstance)!.GetValue(studio)!).Text = DraftName;
                ((TextBox)typeof(MainWindow).GetField("x", PrivateInstance)!.GetValue(studio)!).Text = InvalidX;
                studio.SetTool("Çoklu çizgi");
                var canvas = (CadCanvas)typeof(MainWindow).GetField("canvas", PrivateInstance)!.GetValue(studio)!;
                canvas.RestorePendingDrawing(new RecoveryPendingDrawing("Çoklu çizgi", [new(1, 2), new(3, 4)]));
                await studio.TriggerRecoveryForVerificationAsync();
                var store = (RecoveryStore?)typeof(MainWindow).GetField("recoveryStore", PrivateInstance)!.GetValue(studio)
                    ?? throw new InvalidOperationException("Recovery session unavailable.");
                if (!File.Exists(store.DraftRecoveryPath)) throw new IOException("Recovery snapshot was not committed.");
                File.WriteAllText(Path.Combine(directory, "ready.json"), JsonSerializer.Serialize(new { Path = store.DraftRecoveryPath, PieceId = piece.Id }));
                if (boundaryMode)
                {
                    // The first snapshot is the durable baseline. The second differs and
                    // is paused after flush, before the atomic rename replaces the baseline.
                    ((TextBox)typeof(MainWindow).GetField("name", PrivateInstance)!.GetValue(studio)!).Text = "Second draft must not commit";
                    await studio.TriggerRecoveryForVerificationAsync();
                    throw new InvalidOperationException("Pre-rename barrier unexpectedly returned.");
                }
                // Explicit shutdown mode retains the process and its live lease. Parent kills only this child.
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(path, "child-error.txt"), ex.ToString()); } catch { }
                Application.Current.Shutdown(1);
            }
        }));
    }

    internal static int Inspect(string path)
    {
        try
        {
            var directory = Prepare(path);
            var ready = JsonSerializer.Deserialize<Ready>(File.ReadAllText(Path.Combine(directory, "ready.json"))) ?? throw new InvalidDataException("Missing ready marker.");
            var expectedDirectory = Path.GetFullPath(AppPaths.RecoveryDirectory) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(ready.Path).StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Recovery path outside test directory.");
            var scan = RecoveryStore.ScanDirectory(AppPaths.RecoveryDirectory);
            var candidate = scan.Candidates.Single(c => string.Equals(c.Path, ready.Path, StringComparison.OrdinalIgnoreCase));
            if (scan.Errors.Count != 0 || !candidate.IsRecoverable) throw new InvalidDataException("Recovery candidate unavailable.");
            using var reader = new RecoveryStore(AppPaths.RecoveryDirectory);
            var recovered = reader.LoadCandidate(candidate.Path);
            var draft = recovered.Drafts.Single();
            if (recovered.Document.Name != ProjectName || recovered.Document.Pieces.Count != 1 || recovered.Document.Pieces[0].Id != ready.PieceId ||
                draft.PieceId != ready.PieceId || draft.Name != DraftName || draft.X != InvalidX ||
                recovered.PendingDrawing is not { Tool: "Çoklu çizgi" } pending ||
                !pending.Points.SequenceEqual(new Point2[] { new(1, 2), new(3, 4) }))
                throw new InvalidDataException("Recovered document, draft or pending drawing differs.");
            File.WriteAllText(Path.Combine(directory, "inspection.txt"), "PASS");
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(path, "inspection-error.txt"), ex.ToString()); } catch { }
            return 1;
        }
    }
    private sealed record Ready(string Path, Guid PieceId);
}
