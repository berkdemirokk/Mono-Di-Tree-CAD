using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using static TekstilCAD.FileFormats.DxfSourceSnapshotCheck;

namespace TekstilCAD.Tests;

public sealed class DxfSourceSnapshotCheckTests
{
    private static DxfImportResult Capture(byte[] bytes)=>new(new ProjectDocument(),[]) { SourceByteLength=bytes.Length,SourceSha256=Convert.ToHexString(SHA256.HashData(bytes)) };
    private static string PathInWorkspace()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root missing.");
        var directory=Path.Combine(root.FullName,"work","dxf-source-check-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);return Path.Combine(directory,"source.dxf");
    }

    [Fact]
    public void MatchAndChangedReadBytesAreDistinguishedWithoutWriting()
    {
        var path=PathInWorkspace();var bytes=Encoding.UTF8.GetBytes("orijinal");File.WriteAllBytes(path,bytes);
        var snapshot=Capture(bytes);var match=Check(path,snapshot);
        Assert.Equal(Status.MatchesReadBytes,match.Status);Assert.Equal(bytes.Length,match.ActualByteLength);
        Assert.Equal(snapshot.SourceSha256,match.ActualSha256);Assert.Equal(bytes,File.ReadAllBytes(path));
        var modified=Encoding.UTF8.GetBytes("degisti!");File.WriteAllBytes(path,modified);
        var changed=Check(path,snapshot);
        Assert.Equal(Status.Changed,changed.Status);Assert.Equal(modified,File.ReadAllBytes(path));
    }

    [Fact]
    public void MissingUnreadableAndInvalidFingerprintAreNotMatches()
    {
        var path=PathInWorkspace();var snapshot=Capture([]);
        Assert.Equal(Status.Missing,Check(path,snapshot).Status);
        Assert.Equal(Status.Unreadable,Check(Path.GetDirectoryName(path)!,snapshot).Status);
        Assert.Throws<ArgumentException>(()=>Check(path,new DxfImportResult(new ProjectDocument(),[])));
    }

    [Fact]
    public void GrowingStreamCannotBeReportedAsMatch()
    {
        var bytes=Encoding.UTF8.GetBytes("source bytes");using var stream=new ChangingLengthStream(bytes);
        var result=CheckStream(stream,Capture(bytes));
        Assert.Equal(Status.Unstable,result.Status);Assert.Null(result.ActualSha256);
    }

    [Fact]
    public void OversizedStreamIsRejectedBeforeReadingAndCancellationThrows()
    {
        using var stream=new OversizedStream();
        Assert.Equal(Status.TooLarge,CheckStream(stream,Capture([])).Status);
        using var token=new CancellationTokenSource();token.Cancel();
        Assert.Throws<OperationCanceledException>(()=>Check(PathInWorkspace(),Capture([]),token.Token));
    }

    private sealed class ChangingLengthStream(byte[] data) : MemoryStream(data)
    {
        private bool read;
        public override long Length=>base.Length+(read?1:0);
        public override int Read(byte[] buffer,int offset,int count) { var n=base.Read(buffer,offset,count);read=true;return n; }
    }
    private sealed class OversizedStream : MemoryStream
    {
        public override long Length=>MaximumBytes+1;
        public override int Read(byte[] buffer,int offset,int count)=>throw new InvalidOperationException("Oversized input must not be read.");
    }
}
