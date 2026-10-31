using System.Text;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public sealed class DxfBoundedSnapshotTests
{
    private sealed class GrowingStream : Stream
    {
        private long remaining = 64L * 1024 * 1024 + 1;
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 1;
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(remaining, count); Array.Fill(buffer, (byte)' ', offset, n); remaining -= n; Position += n; return n;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    [Fact] public void GrowingInputBeyondLimitIsRejectedAndLeftOpen()
    {
        using var input = new GrowingStream();
        Assert.Throws<InvalidDataException>(() => DxfFormat.Import(input, "growing"));
        Assert.True(input.CanRead);
    }
    [Fact] public void StreamPositionIsRespectedAndInputRemainsOpen()
    {
        var bytes=Encoding.UTF8.GetBytes("ignore0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n");
        using var input=new MemoryStream(bytes); input.Position=6;
        var result=DxfFormat.Import(input,"sample");
        Assert.Single(result.Project.Pieces); Assert.True(input.CanRead);
    }
}
