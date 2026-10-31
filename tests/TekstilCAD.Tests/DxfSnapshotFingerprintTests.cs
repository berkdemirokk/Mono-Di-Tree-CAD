using System.Security.Cryptography;
using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfSnapshotFingerprintTests
{
    private const string Minimal="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static string TestPath()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root not found.");
        var directory=Path.Combine(root.FullName,"work","dxf-fingerprint-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);return Path.Combine(directory,"source.dxf");
    }

    [Fact]
    public void StreamFingerprintCoversOnlyBytesReadIncludingBom()
    {
        var prefix=new byte[] { 1,2,3,4 };var dxf=new byte[] { 0xEF,0xBB,0xBF }.Concat(Encoding.UTF8.GetBytes(Minimal)).ToArray();
        var inputBytes=prefix.Concat(dxf).ToArray();using var input=new MemoryStream(inputBytes,writable:false);input.Position=prefix.Length;
        var result=DxfFormat.Import(input,"offset",1);
        Assert.Equal(Hash(dxf),result.SourceSha256);Assert.Equal(dxf.LongLength,result.SourceByteLength);
        Assert.True(input.CanRead);Assert.Equal(inputBytes,input.ToArray());
    }

    [Fact]
    public void PathFingerprintMatchesSourceBytesWithoutModifyingThem()
    {
        var bytes=Encoding.UTF8.GetBytes(Minimal);var path=TestPath();File.WriteAllBytes(path,bytes);
        var result=DxfFormat.Import(path);
        Assert.Equal(Hash(bytes),result.SourceSha256);Assert.Equal(bytes.LongLength,result.SourceByteLength);
        Assert.Equal(bytes,File.ReadAllBytes(path));
    }

    private sealed class CancelAfterFirstRead(Stream inner,CancellationTokenSource cancellation):Stream
    {
        public override bool CanRead=>inner.CanRead;public override bool CanSeek=>inner.CanSeek;public override bool CanWrite=>false;
        public override long Length=>inner.Length;public override long Position { get=>inner.Position;set=>inner.Position=value; }
        public override int Read(byte[] buffer,int offset,int count) { var result=inner.Read(buffer,offset,count);cancellation.Cancel();return result; }
        public override void Flush()=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }

    [Fact]
    public void CancellationDuringSnapshotDoesNotReturnAResultOrChangeInput()
    {
        var bytes=Encoding.UTF8.GetBytes(Minimal);using var inner=new MemoryStream(bytes,writable:false);using var cancellation=new CancellationTokenSource();
        using var input=new CancelAfterFirstRead(inner,cancellation);
        Assert.Throws<OperationCanceledException>(()=>DxfFormat.Import(input,"cancel",1,cancellation.Token));
        Assert.Equal(bytes,inner.ToArray());
    }
}
