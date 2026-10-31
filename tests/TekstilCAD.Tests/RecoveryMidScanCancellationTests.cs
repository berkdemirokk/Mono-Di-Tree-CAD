using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryMidScanCancellationTests
{
    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-mid-scan-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void BoundedReadCancellationAfterFirstRealFileReadLeavesRecordRecoverable()
    {
        var directory = TestDirectory();
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(new ProjectDocument { Name = "Gerçek kurtarma kaydı" }, Array.Empty<RecoveryPropertyDraft>());
            path = writer.DraftRecoveryPath;
        }
        var original = File.ReadAllBytes(path);
        using var cancellation = new CancellationTokenSource();
        var method = typeof(RecoveryStore).GetMethod("ReadBounded", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Recovery bounded reader was not found.");

        // A wrapper around a real file triggers cancellation only after bytes were
        // actually read. This avoids timing-dependent sleeps or filesystem watchers.
        using (var input = new CancelAfterFirstReadStream(File.OpenRead(path), cancellation))
        {
            var error = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, [input, 80 * 1024 * 1024, cancellation.Token]));
            Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
            Assert.True(input.BytesRead > 0);
        }

        Assert.Equal(original, File.ReadAllBytes(path));
        using var observer = new RecoveryStore(directory);
        var candidate = Assert.Single(observer.Scan().Candidates);
        Assert.Equal(path, candidate.Path);
        Assert.Equal("Gerçek kurtarma kaydı", observer.LoadCandidate(path).Document.Name);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    private sealed class CancelAfterFirstReadStream(Stream inner, CancellationTokenSource cancellation) : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            if (read > 0) cancellation.Cancel();
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
