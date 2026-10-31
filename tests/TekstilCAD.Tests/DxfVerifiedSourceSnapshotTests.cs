using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfVerifiedSourceSnapshotTests
{
    private sealed class ChunkedStream(byte[] bytes, int chunk, Action? afterRead=null) : MemoryStream(bytes,writable:false)
    {
        public override int Read(byte[] buffer,int offset,int count)
        {
            var read=base.Read(buffer,offset,Math.Min(count,chunk));
            afterRead?.Invoke();return read;
        }
    }
    private static readonly byte[] Dxf=Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n");

    [Fact]
    public void ReturnsExactlyImportedSuffixWithoutClosingInput()
    {
        var bytes=new byte[] {1,2,3}.Concat(Dxf).ToArray();
        using var stream=new MemoryStream(bytes,writable:false);
        stream.Position=3;
        var import=DxfFormat.Import(stream,"source");
        stream.Position=3;
        var snapshot=DxfVerifiedSourceSnapshot.Capture(stream,import);
        Assert.Equal(Dxf,snapshot);
        Assert.True(stream.CanRead);
        Assert.Equal(stream.Length,stream.Position);
        Assert.Equal(bytes,stream.ToArray());
    }

    [Fact]
    public void RejectsDifferentBytesEvenWhenLengthMatches()
    {
        using var original=new MemoryStream(Dxf,writable:false);
        var import=DxfFormat.Import(original,"source");
        var changed=(byte[])Dxf.Clone();changed[10]^=1;
        using var other=new MemoryStream(changed,writable:false);
        Assert.Throws<InvalidDataException>(()=>DxfVerifiedSourceSnapshot.Capture(other,import));
        Assert.Equal(changed,other.ToArray());
    }

    [Fact]
    public void RejectsLengthChangesAndUntrustedResultIdentity()
    {
        using var original=new MemoryStream(Dxf,writable:false);
        var import=DxfFormat.Import(original,"source");
        using var shorter=new MemoryStream(Dxf[..^1],writable:false);
        Assert.Throws<InvalidDataException>(()=>DxfVerifiedSourceSnapshot.Capture(shorter,import));
        using var source=new MemoryStream(Dxf,writable:false);
        var forged=import with { SourceSha256=new string('0',64) };
        Assert.Throws<InvalidDataException>(()=>DxfVerifiedSourceSnapshot.Capture(source,forged));
        Assert.Throws<InvalidDataException>(()=>DxfVerifiedSourceSnapshot.Capture(source,new DxfImportResult(new ProjectDocument(),[])));
    }

    [Fact]
    public void CancellationDoesNotReturnBytesOrCloseInput()
    {
        using var stream=new MemoryStream(Dxf,writable:false);
        var import=DxfFormat.Import(stream,"source");stream.Position=0;
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfVerifiedSourceSnapshot.Capture(stream,import,cancellation.Token));
        Assert.True(stream.CanRead);Assert.Equal(0,stream.Position);
    }

    [Fact]
    public void InspectionSnapshotCanBeCapturedEvenWithUnsupportedMetadata()
    {
        var tables="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n";
        var body="0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n1001\nAPP\n1000\nvalue\n0\nENDSEC\n0\nEOF\n";
        var bytes=Encoding.UTF8.GetBytes(tables+body);
        using var stream=new MemoryStream(bytes,writable:false);
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        Assert.Contains(inspection.Limitations,s=>s.Contains("kontrollü aktarım reddedilir"));
        stream.Position=0;
        Assert.Equal(bytes,DxfVerifiedSourceSnapshot.Capture(stream,inspection));
    }

    [Fact]
    public void ShortReadsAreReassembledBeforeCheckingDigest()
    {
        using var original=new MemoryStream(Dxf,writable:false);
        var import=DxfFormat.Import(original,"source");
        using var chunks=new ChunkedStream(Dxf,3);
        Assert.Equal(Dxf,DxfVerifiedSourceSnapshot.Capture(chunks,import));
    }

    [Fact]
    public void MidReadCancellationReturnsNoSnapshotAndLeavesStreamOpen()
    {
        using var original=new MemoryStream(Dxf,writable:false);
        var import=DxfFormat.Import(original,"source");
        using var cancellation=new CancellationTokenSource();
        using var chunks=new ChunkedStream(Dxf,3,()=>cancellation.Cancel());
        Assert.Throws<OperationCanceledException>(()=>DxfVerifiedSourceSnapshot.Capture(chunks,import,cancellation.Token));
        Assert.True(chunks.CanRead);
    }
}
