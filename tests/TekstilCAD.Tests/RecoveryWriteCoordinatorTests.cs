using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryWriteCoordinatorTests
{
    private sealed class ControlledWriter : IRecoveryWriter
    {
        public readonly ManualResetEventSlim Entered = new(false), Release = new(false);
        public readonly List<string> Events = [];
        public ProjectDocument? LastDocument;
        public RecoveryPropertyDraft[]? LastDrafts;
        public RecoveryPendingDrawing? LastDrawing;
        public bool FailSave, FailClear, Disposed;
        public void Save(ProjectDocument document, IReadOnlyList<RecoveryPropertyDraft> drafts, RecoveryPendingDrawing? pendingDrawing)
        {
            Events.Add("save:" + document.Name); Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test worker was not released.");
            if (FailSave) throw new IOException("save failure");
            LastDocument = document; LastDrafts = drafts.ToArray(); LastDrawing = pendingDrawing;
        }
        public bool Clear() { Events.Add("clear"); if (FailClear) throw new IOException("clear failure"); LastDocument = null; return true; }
        public void Dispose() { Events.Add("dispose"); Disposed = true; }
    }
    private static ProjectDocument Doc(string name) => new() { Name=name, Pieces=[new PatternPiece { Name="original", Points=[new(0,0),new(1,0),new(0,1)] }] };

    [Fact]
    public async Task ClearWaitsForInFlightWriteDiscardsPendingAndDisposeWaitsForClear()
    {
        var writer=new ControlledWriter(); var coordinator=new RecoveryWriteCoordinator(writer);
        try
        {
            var first=coordinator.SaveAsync(Doc("first"), [], null);
            Assert.True(writer.Entered.Wait(TimeSpan.FromSeconds(5)));
            var pending=coordinator.SaveAsync(Doc("pending"), [], null);
            var clear=coordinator.ClearAsync();
            var dispose=coordinator.DisposeAsync().AsTask();
            Assert.False(await pending); Assert.False(clear.IsCompleted); Assert.False(dispose.IsCompleted); Assert.False(writer.Disposed);
            Assert.Throws<ObjectDisposedException>(() => { _ = coordinator.SaveAsync(Doc("late"), [], null); });
            writer.Release.Set();
            Assert.True(await first); Assert.True(await clear); await dispose;
            Assert.Equal(new[] { "save:first", "clear", "dispose" }, writer.Events);
            Assert.Null(writer.LastDocument); Assert.True(writer.Disposed);
        }
        finally { writer.Release.Set(); await coordinator.DisposeAsync(); }
    }

    [Fact]
    public async Task CoalescesPendingWritesAndOwnsSourceSnapshots()
    {
        var writer=new ControlledWriter(); var coordinator=new RecoveryWriteCoordinator(writer);
        try
        {
            var running=coordinator.SaveAsync(Doc("running"), [], null);
            Assert.True(writer.Entered.Wait(TimeSpan.FromSeconds(5)));
            var skipped=coordinator.SaveAsync(Doc("skip"), [], null);
            var doc=Doc("retained"); var drafts=new[] { new RecoveryPropertyDraft(Guid.NewGuid(),"draft","bad","3","base",0,0) };
            var points=new List<Point2> { new(3,4) }; var last=coordinator.SaveAsync(doc,drafts,new("Çizgi",points));
            doc.Name="changed"; doc.Pieces[0].Name="changed"; drafts[0]=drafts[0] with { Name="changed" }; points[0]=new(9,9);
            Assert.False(await skipped); writer.Release.Set(); Assert.True(await running); Assert.True(await last);
            Assert.Equal("retained",writer.LastDocument!.Name); Assert.Equal("original",writer.LastDocument.Pieces[0].Name);
            Assert.Equal("draft",writer.LastDrafts![0].Name); Assert.Equal(new Point2(3,4),writer.LastDrawing!.Points[0]);
            Assert.Equal(new[] { "save:running", "save:retained" },writer.Events);
        }
        finally { writer.Release.Set(); await coordinator.DisposeAsync(); }
    }

    [Fact]
    public async Task FailureIsReportedAndDoesNotPreventSubsequentClearAndDispose()
    {
        var writer=new ControlledWriter { FailSave=true, FailClear=true }; writer.Release.Set();
        var coordinator=new RecoveryWriteCoordinator(writer);
        var failure=coordinator.SaveAsync(Doc("broken"),[],null);
        await Assert.ThrowsAsync<IOException>(async () => await failure);
        await Assert.ThrowsAsync<IOException>(async () => await coordinator.ClearAsync());
        writer.FailClear=false; Assert.True(await coordinator.ClearAsync());
        await coordinator.DisposeAsync(); Assert.True(writer.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearOrDisposeDuringCaptureInvalidatesOldSnapshot(bool dispose)
    {
        using var captured=new ManualResetEventSlim(false);
        using var release=new ManualResetEventSlim(false);
        var writer=new ControlledWriter(); writer.Release.Set();
        var coordinator=new RecoveryWriteCoordinator(writer, () =>
        {
            captured.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        });
        try
        {
            var stale=Task.Run(async () => await coordinator.SaveAsync(Doc("stale"),[],null));
            Assert.True(captured.Wait(TimeSpan.FromSeconds(5)));
            if (dispose) await coordinator.DisposeAsync();
            else Assert.True(await coordinator.ClearAsync());
            release.Set(); Assert.False(await stale);
            Assert.Null(writer.LastDocument);
            Assert.DoesNotContain(writer.Events, e => e.StartsWith("save:"));
        }
        finally { release.Set(); await coordinator.DisposeAsync(); }
    }

    [Fact]
    public async Task SlowerEarlierCaptureCannotOverwriteNewerSnapshot()
    {
        using var captured=new ManualResetEventSlim(false);
        using var release=new ManualResetEventSlim(false);
        var calls=0; var writer=new ControlledWriter(); writer.Release.Set();
        var coordinator=new RecoveryWriteCoordinator(writer, () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return;
            captured.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        });
        try
        {
            var older=Task.Run(async () => await coordinator.SaveAsync(Doc("older"),[],null));
            Assert.True(captured.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(await coordinator.SaveAsync(Doc("newer"),[],null));
            release.Set(); Assert.False(await older);
            Assert.Equal("newer",writer.LastDocument!.Name);
        }
        finally { release.Set(); await coordinator.DisposeAsync(); }
    }

    [Fact]
    public async Task NewSnapshotAfterClearCanBeSaved()
    {
        var writer=new ControlledWriter(); writer.Release.Set();
        var coordinator=new RecoveryWriteCoordinator(writer);
        try
        {
            Assert.True(await coordinator.SaveAsync(Doc("before"),[],null));
            Assert.True(await coordinator.ClearAsync());
            Assert.True(await coordinator.SaveAsync(Doc("after"),[],null));
            Assert.Equal("after",writer.LastDocument!.Name);
            Assert.Equal(new[] { "save:before", "clear", "save:after" },writer.Events);
        }
        finally { await coordinator.DisposeAsync(); }
    }

    [Fact]
    public async Task RealStoreLeaseRemainsHeldUntilCoordinatorDisposes()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var directory=Path.Combine(root.FullName,"work","coordinator-tests",Guid.NewGuid().ToString("N"));
        var store=new RecoveryStore(directory); var path=store.DraftRecoveryPath;
        var coordinator=new RecoveryWriteCoordinator(store);
        Assert.True(await coordinator.SaveAsync(Doc("live"),[],null));
        using var reader=new RecoveryStore(directory);
        Assert.DoesNotContain(reader.Scan().Candidates,c=>c.Path==path);
        await coordinator.DisposeAsync();
        Assert.Contains(reader.Scan().Candidates,c=>c.Path==path);
    }
}
