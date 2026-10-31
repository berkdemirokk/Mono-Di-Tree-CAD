using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfSourceSnapshotIdentityTests
{
    [Fact]
    public void AlteredCompletedImportCannotAuthenticateDifferentBytes()
    {
        var original=Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n1\n21\n1\n0\nENDSEC\n0\nEOF\n");
        using var input=new MemoryStream(original,writable:false);
        var import=DxfFormat.Import(input,"source");
        using var same=new MemoryStream(original,writable:false);
        Assert.True(DxfSourceSnapshotCheck.CheckStream(same,import).ImportIdentityVerified);
        var other=Encoding.UTF8.GetBytes("other source");
        var forged=import with { SourceSha256=Convert.ToHexString(SHA256.HashData(other)),SourceByteLength=other.Length };
        using var changed=new MemoryStream(other,writable:false);
        Assert.Throws<ArgumentException>(()=>DxfSourceSnapshotCheck.CheckStream(changed,forged));
        Assert.Equal(0,changed.Position);
    }

    [Fact]
    public void ManuallySuppliedFingerprintIsExplicitlyUnverified()
    {
        var bytes=Encoding.UTF8.GetBytes("caller supplied bytes");
        var supplied=new DxfImportResult(new ProjectDocument(),[]) { SourceSha256=Convert.ToHexString(SHA256.HashData(bytes)),SourceByteLength=bytes.Length };
        using var input=new MemoryStream(bytes,writable:false);
        var result=DxfSourceSnapshotCheck.CheckStream(input,supplied);
        Assert.Equal(DxfSourceSnapshotCheck.Status.MatchesReadBytes,result.Status);
        Assert.False(result.ImportIdentityVerified);
        Assert.Contains("doğrulanmadı",result.Detail);
    }
}
