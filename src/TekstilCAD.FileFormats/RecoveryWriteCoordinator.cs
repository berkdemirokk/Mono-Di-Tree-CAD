using TekstilCAD.Core;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TekstilCAD.Tests")]

namespace TekstilCAD.FileFormats;

/// <summary>A single-owner recovery sink. Coordinator calls are serialized; implementations need not be thread-safe.</summary>
public interface IRecoveryWriter : IDisposable
{
    void Save(ProjectDocument document, IReadOnlyList<RecoveryPropertyDraft> drafts, RecoveryPendingDrawing? pendingDrawing);
    bool Clear();
}

/// <summary>Serializes recovery writes and clears without performing file I/O on the calling thread.
/// The coordinator owns its writer and keeps it alive until all accepted operations finish.
/// Callers must capture a consistent source on their own thread before calling SaveAsync.</summary>
public sealed class RecoveryWriteCoordinator : IAsyncDisposable
{
    private sealed class StoreWriter(RecoveryStore store) : IRecoveryWriter
    {
        public void Save(ProjectDocument document, IReadOnlyList<RecoveryPropertyDraft> drafts, RecoveryPendingDrawing? pendingDrawing) => store.Save(document, drafts, pendingDrawing);
        public bool Clear() => store.ClearOwn();
        public void Dispose() => store.Dispose();
    }
    private abstract record Operation;
    private sealed record Write(RecoverySnapshot Snapshot, TaskCompletionSource<bool> Completion) : Operation;
    private sealed record Clear(TaskCompletionSource<bool> Completion) : Operation;
    private sealed record Finish(TaskCompletionSource<bool> Completion) : Operation;
    private readonly IRecoveryWriter writer;
    private readonly object gate = new();
    private readonly LinkedList<Operation> queue = new();
    private bool running, disposing;
    private long generation;
    private long nextCapture, latestQueuedCapture;
    private readonly Action? afterCaptureForTests;
    private readonly Action<Action>? enqueueWorkerForTests;
    private Task? disposeTask;

    public RecoveryWriteCoordinator(RecoveryStore store) : this(new StoreWriter(store ?? throw new ArgumentNullException(nameof(store)))) { }
    public RecoveryWriteCoordinator(IRecoveryWriter writer) => this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
    internal RecoveryWriteCoordinator(IRecoveryWriter writer, Action afterCaptureForTests) : this(writer) => this.afterCaptureForTests = afterCaptureForTests;
    internal RecoveryWriteCoordinator(IRecoveryWriter writer, Action? afterCaptureForTests, Action<Action> enqueueWorkerForTests) : this(writer)
    { this.afterCaptureForTests=afterCaptureForTests;this.enqueueWorkerForTests=enqueueWorkerForTests ?? throw new ArgumentNullException(nameof(enqueueWorkerForTests)); }

    /// <returns>True when written; false if replaced by a newer pending snapshot or invalidated by a clear.</returns>
    public Task<bool> SaveAsync(ProjectDocument document, IReadOnlyList<RecoveryPropertyDraft> drafts, RecoveryPendingDrawing? pendingDrawing)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(drafts);
        long capturedGeneration, captureNumber;
        lock (gate) { ObjectDisposedException.ThrowIf(disposing, this); capturedGeneration = generation; captureNumber = ++nextCapture; }
        // Take ownership before queueing: mutable caller objects must never reach the worker.
        var copy = new RecoverySnapshot(document.Clone(), drafts.ToArray())
        { PendingDrawing = pendingDrawing is null ? null : new(pendingDrawing.Tool, pendingDrawing.Points.ToArray()) };
        afterCaptureForTests?.Invoke();
        var completion = NewCompletion();
        lock (gate)
        {
            if (capturedGeneration != generation || disposing || captureNumber < latestQueuedCapture) return Task.FromResult(false);
            latestQueuedCapture = captureNumber;
            // Only coalesce adjacent writes. A clear is a strict ordering barrier.
            if (queue.Last?.Value is Write previous)
            { queue.RemoveLast(); previous.Completion.TrySetResult(false); }
            queue.AddLast(new Write(copy, completion)); StartWorker();
        }
        return completion.Task;
    }

    /// <summary>Discards all writes pending before this barrier, waits for any active write,
    /// and clears afterward. Writes submitted after this call can create a new recovery file.</summary>
    public Task<bool> ClearAsync()
    {
        var completion = NewCompletion();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposing, this);
            generation++;
            while (queue.Last?.Value is Write pending)
            { queue.RemoveLast(); pending.Completion.TrySetResult(false); }
            queue.AddLast(new Clear(completion)); StartWorker();
        }
        return completion.Task;
    }

    /// <summary>Drains accepted operations and disposes the writer without clearing recovery data.</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposeTask is not null) return new(disposeTask);
            disposing = true; generation++;
            var completion = NewCompletion(); disposeTask = completion.Task;
            queue.AddLast(new Finish(completion)); StartWorker();
            return new(disposeTask);
        }
    }
    private static TaskCompletionSource<bool> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Called only under gate; continuations cannot run inline under the gate.
    private void StartWorker()
    {
        if (running) return;
        running = true;
        if (enqueueWorkerForTests is null) _ = Task.Run(Run);
        else enqueueWorkerForTests(Run);
    }
    private void Run()
    {
        while (true)
        {
            Operation operation;
            lock (gate)
            {
                if (queue.First is null) { running = false; return; }
                operation = queue.First.Value; queue.RemoveFirst();
            }
            try
            {
                switch (operation)
                {
                    case Write w: writer.Save(w.Snapshot.Document, w.Snapshot.Drafts, w.Snapshot.PendingDrawing); w.Completion.TrySetResult(true); break;
                    case Clear c: c.Completion.TrySetResult(writer.Clear()); break;
                    case Finish f: writer.Dispose(); f.Completion.TrySetResult(true); break;
                }
            }
            catch (Exception ex)
            {
                switch (operation)
                {
                    case Write w: w.Completion.TrySetException(ex); break;
                    case Clear c: c.Completion.TrySetException(ex); break;
                    case Finish f: f.Completion.TrySetException(ex); break;
                }
            }
        }
    }
}
