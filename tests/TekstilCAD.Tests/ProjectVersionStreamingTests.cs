using System.Security.Cryptography;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectVersionStreamingTests
{
    [Fact]
    public void IncrementalHashMatchesExactFileBytesAcrossMultipleBuffers()
    {
        var bytes = new byte[200_123];
        new Random(42).NextBytes(bytes);
        using var stream = new MemoryStream(bytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), ProjectStorage.CaptureVersionHash(stream, CancellationToken.None));
    }

    [Fact]
    public void HashUsesActuallyReadBytesWhenLengthChanges()
    {
        var bytes = new byte[200_123]; new Random(19).NextBytes(bytes);
        using var stream = new ReportedLengthStream(bytes, 1);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), ProjectStorage.CaptureVersionHash(stream, CancellationToken.None));
    }

    [Fact]
    public void InitialLengthAboveLimitFailsBeforeReading()
    {
        using var stream = new ReportedLengthStream([], 64L * 1024 * 1024 + 1);
        Assert.Throws<InvalidDataException>(() => ProjectStorage.CaptureVersionHash(stream, CancellationToken.None));
    }

    [Fact]
    public void GrowthPastLimitFailsInsteadOfReturningPartialHash()
    {
        using var stream = new RepeatingStream(64L * 1024 * 1024 + 1);
        Assert.Throws<InvalidDataException>(() => ProjectStorage.CaptureVersionHash(stream, CancellationToken.None));
    }

    [Fact]
    public void CancellationDuringReadDoesNotReturnAHash()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellingStream(new byte[200_000], cancellation);
        Assert.Throws<OperationCanceledException>(() => ProjectStorage.CaptureVersionHash(stream, cancellation.Token));
    }

    private sealed class ReportedLengthStream(byte[] bytes, long length) : MemoryStream(bytes)
    {
        public override long Length => length;
    }

    private sealed class CancellingStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            cancellation.Cancel();
            return read;
        }
    }

    private sealed class RepeatingStream(long size) : Stream
    {
        private long position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 1; // Simulates growth after the initial length check.
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, size - position);
            position += read;
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
