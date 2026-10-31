using System.Security.Cryptography;
using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfCompanionSnapshotTests
{
    private sealed class ShortNonSeekableStream(byte[] bytes,Action? afterRead=null) : MemoryStream(bytes,writable:false)
    {
        public override bool CanSeek=>false;
        public override int Read(byte[] buffer,int offset,int count)
        {
            var read=base.Read(buffer,offset,Math.Min(2,count));
            if(read>0)afterRead?.Invoke();
            return read;
        }
    }
    private static DxfSourceRecordView ReadDxf()
    {
        var text="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n0\nENDSEC\n0\nEOF\n";
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(text));
        var inspection=DxfFormat.InspectBlockPlacements(input,DxfTextEncoding.Utf8);
        return DxfSourceGeometryView.Read(inspection,Assert.Single(inspection.SourceGeometry!.Records).SourceOrdinal);
    }

    [Fact]
    public void CapturesOnlySelectedSuffixAndReturnsIndependentCopies()
    {
        var dxf=ReadDxf();var selected=Encoding.ASCII.GetBytes("GRADE DATA\r\n");
        using var input=new MemoryStream(new byte[] {1,2}.Concat(selected).ToArray(),writable:false);
        input.Position=2;
        var result=DxfCompanionSnapshot.Capture(input,dxf);
        Assert.Equal(selected.Length,result.ByteLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(selected)),result.Sha256);
        Assert.Equal(dxf.SourceSha256,result.DxfSha256);
        Assert.Equal(dxf.SourceByteLength,result.DxfByteLength);
        Assert.Equal(DxfTextEncoding.Utf8,result.DxfEncoding);
        Assert.Null(result.DxfImportParameters);
        var copy=result.CopyBytes();Assert.Equal(selected,copy);copy[0]^=1;
        Assert.Equal(selected,result.CopyBytes());Assert.True(input.CanRead);Assert.Equal(input.Length,input.Position);
    }

    [Fact]
    public void UnboundRecordAndCancellationDoNotReadCompanion()
    {
        var dxf=ReadDxf();
        var unbound=new DxfSourceRecordView(dxf.SourceOrdinal,dxf.Section,dxf.Block,dxf.EntityType,dxf.Groups);
        using var input=new MemoryStream(new byte[] {1,2,3});
        Assert.Throws<InvalidDataException>(()=>DxfCompanionSnapshot.Capture(input,unbound));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfCompanionSnapshot.Capture(input,dxf,cancellation.Token));
        Assert.Equal(0,input.Position);
    }

    [Fact]
    public void EmptyInspectionCanBindSelectedCompanionWithoutInventingARecord()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n";
        using var dxf=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var inspection=DxfFormat.InspectBlockPlacements(dxf,DxfTextEncoding.Utf8);
        Assert.Empty(inspection.SourceGeometry!.Records);
        using var companion=new MemoryStream(Encoding.ASCII.GetBytes("GRADE"));
        var captured=DxfCompanionSnapshot.Capture(companion,inspection);
        Assert.Equal(inspection.SourceSha256,captured.DxfSha256);
        Assert.Equal(inspection.SourceByteLength,captured.DxfByteLength);
        Assert.Equal(inspection.Encoding,captured.DxfEncoding);
        Assert.Null(captured.DxfImportParameters);
        Assert.Equal("GRADE",Encoding.ASCII.GetString(captured.CopyBytes()));
    }

    [Fact]
    public void ChangedImportIdentityIsRejectedBeforeReadingCompanion()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n0\nENDSEC\n0\nEOF\n";
        using var dxf=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var import=DxfFormat.Import(dxf,"source",2);
        using var companion=new MemoryStream(new byte[] {1,2,3});
        var forged=import with { SourceSha256=new string('0',64) };
        Assert.Throws<InvalidDataException>(()=>DxfCompanionSnapshot.Capture(companion,forged));
        Assert.Equal(0,companion.Position);
        var captured=DxfCompanionSnapshot.Capture(companion,import);
        Assert.Equal(import.SourceSha256,captured.DxfSha256);
        Assert.Equal(DxfTextEncoding.Utf8,captured.DxfEncoding);
        Assert.Equal(2,captured.DxfImportParameters!.MillimetersPerUnit);
    }

    [Fact]
    public void NonSeekableShortReadsAreCapturedExactly()
    {
        var source=ReadDxf();var bytes=Encoding.ASCII.GetBytes("RULES\r\nEND\r\n");
        using var input=new ShortNonSeekableStream(bytes);
        var captured=DxfCompanionSnapshot.Capture(input,source);
        Assert.Equal(bytes,captured.CopyBytes());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),captured.Sha256);
        Assert.True(input.CanRead);
    }

    [Fact]
    public void CancellationDuringCompanionReadLeavesStreamOpen()
    {
        var source=ReadDxf();
        using var cancellation=new CancellationTokenSource();
        using var input=new ShortNonSeekableStream(new byte[] {1,2,3,4},()=>cancellation.Cancel());
        Assert.Throws<OperationCanceledException>(()=>DxfCompanionSnapshot.Capture(input,source,cancellation.Token));
        Assert.True(input.CanRead);
    }
}
