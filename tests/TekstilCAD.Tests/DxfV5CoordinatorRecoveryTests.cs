using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfV5CoordinatorRecoveryTests
{
    private sealed class BlockingWriter(RecoveryStore store) : IRecoveryWriter
    {
        public readonly ManualResetEventSlim Entered = new(false);
        public readonly ManualResetEventSlim Release = new(false);
        public void Save(ProjectDocument document, IReadOnlyList<RecoveryPropertyDraft> drafts, RecoveryPendingDrawing? drawing)
        {
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException();
            store.Save(document, drafts, drawing);
        }
        public bool Clear() => store.ClearOwn();
        public void Dispose() => store.Dispose();
    }

    [Fact]
    public async Task CapturedV5EnvelopeSurvivesCallerEditsAndRejectedWrites()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "v5-coordinator", Guid.NewGuid().ToString("N"));
        var store = new RecoveryStore(directory);
        var path = store.DraftRecoveryPath;
        var writer = new BlockingWriter(store);
        var workers = new List<Thread>();
        var coordinator = new RecoveryWriteCoordinator(writer, null, action =>
        {
            var thread = new Thread(() => action()) { IsBackground = true, Name = "Recovery snapshot test worker" };
            workers.Add(thread);
            thread.Start();
        });
        var piece = new PatternPiece { Name = "Ön", Points = [new(0, 0), new(10, 0), new(0, 10)] };
        var document = new ProjectDocument { Pieces = [piece], ImportProvenance = new DxfImportProvenance {
            SourceFileName = "kaynak.dxf", SourceSha256 = new string('A', 64), SourceByteLength = 53,
            MillimetersPerUnit = 1, Warnings = ["Önceki uyarı"], PieceIds = [piece.Id] } };
        var draft = new RecoveryPropertyDraft(piece.Id, "Taslak", "?", "0", "Ön", 0, 0);
        var drafts = new[] { draft };
        var points = new List<Point2> { new(1, 2), new(3, 4) };
        var drawing = new RecoveryPendingDrawing("Yay", points);
        try
        {
            var saved = coordinator.SaveAsync(document, drafts, drawing);
            Assert.True(writer.Entered.Wait(TimeSpan.FromSeconds(30)), "Recovery worker did not enter the writer.");
            document.ImportProvenance!.Warnings[0] = "değişti";
            document.ImportProvenance.PieceIds.Clear();
            piece.Points[0] = new(99, 99);
            drafts[0] = draft with { Name = "değişti" };
            points[0] = new(99, 99);
            writer.Release.Set();
            Assert.True(await saved);
            var original = File.ReadAllBytes(path);

            await Assert.ThrowsAsync<InvalidDataException>(async () => await coordinator.SaveAsync(document, [draft, draft], drawing));
            Assert.Equal(original, File.ReadAllBytes(path));

            // An external change invalidates the store's ownership stamp. The
            // rejected write must preserve that changed, still readable envelope.
            await File.AppendAllTextAsync(path, " ");
            var external = File.ReadAllBytes(path);
            await Assert.ThrowsAsync<IOException>(async () => await coordinator.SaveAsync(document, [draft], drawing));
            Assert.Equal(external, File.ReadAllBytes(path));
        }
        finally
        {
            writer.Release.Set();
            await coordinator.DisposeAsync();
            foreach(var worker in workers) Assert.True(worker.Join(TimeSpan.FromSeconds(30)), "Recovery worker did not stop.");
            writer.Entered.Dispose();writer.Release.Dispose();
        }

        using var reader = new RecoveryStore(directory);
        var restored = reader.LoadCandidate(path);
        Assert.Equal("Önceki uyarı", Assert.Single(restored.Document.ImportProvenance!.Warnings));
        Assert.Equal(piece.Id, Assert.Single(restored.Document.ImportProvenance.PieceIds));
        Assert.Equal(new Point2(0, 0), restored.Document.Pieces[0].Points[0]);
        Assert.Equal(draft, Assert.Single(restored.Drafts));
        Assert.Equal("Yay", restored.PendingDrawing!.Tool);
        Assert.Equal(new[] { new Point2(1, 2), new Point2(3, 4) }, restored.PendingDrawing.Points);
    }
}
