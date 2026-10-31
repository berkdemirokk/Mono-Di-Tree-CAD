using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfBulgeFingerprintTests
{
    private const string Source="0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n10\n0\n20\n0\n10\n20\n20\n0\n10\n0\n20\n20\n0\nENDSEC\n0\nEOF\n";

    private static DxfImportResult Read(double? tolerance)
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(Source));
        return tolerance is double value
            ? DxfFormat.ImportWithBulgeApproximation(input,"review",1,DxfTextEncoding.Utf8,value)
            : DxfFormat.Import(input,"review",1,DxfTextEncoding.Utf8);
    }

    [Fact]
    public void NullOptInAndDifferentTolerancesHaveDifferentReviewFingerprints()
    {
        var standard=Read(null);
        // Use the same geometry and IDs so only the captured import choice differs.
        var coarse=Read(0.1) with { Project=standard.Project.Clone() };
        var fine=Read(0.001) with { Project=standard.Project.Clone() };
        var baseline=DxfTextileGrainlineMapping.CaptureFingerprint(standard);
        var coarseToken=DxfTextileGrainlineMapping.CaptureFingerprint(coarse);
        var fineToken=DxfTextileGrainlineMapping.CaptureFingerprint(fine);
        Assert.NotEqual(baseline,coarseToken);Assert.NotEqual(coarseToken,fineToken);
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.Apply(coarse,[],baseline));
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.Apply(fine,[],coarseToken));
        Assert.Equal(fineToken,DxfTextileGrainlineMapping.CaptureFingerprint(fine));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidMutatedToleranceCannotBeApproved(double value)
    {
        var imported=Read(null);
        var changed=imported with { ImportParameters=imported.ImportParameters! with { BulgeToleranceMm=value } };
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.CaptureFingerprint(changed));
    }
}
