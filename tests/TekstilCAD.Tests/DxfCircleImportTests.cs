using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfCircleImportTests
{
    private static MemoryStream Stream(string body)=>new(Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n"+body+"0\nENDSEC\n0\nEOF\n"));
    private const string Circle="0\nCIRCLE\n8\nDaire\n10\n10\n20\n20\n30\n0\n40\n5\n210\n0\n220\n0\n230\n1\n";
    private static DxfImportResult Import(Stream stream)=>DxfFormat.ImportWithCircleAndBulgeApproximation(stream,"circle",1,DxfTextEncoding.Utf8,0.01);

    [Fact]
    public void ExplicitModeImportsClosedCircleAndRetainsSourceIdentity()
    {
        using var input=Stream(Circle);var result=Import(input);var piece=Assert.Single(result.Project.Pieces);
        Assert.True(piece.Closed);Assert.True(piece.Points.Count>4);
        Assert.Equal(15,piece.Points.Max(p=>p.X),8);Assert.Equal(5,piece.Points.Min(p=>p.X),8);
        Assert.Equal(25,piece.Points.Max(p=>p.Y),8);Assert.Equal(15,piece.Points.Min(p=>p.Y),8);
        Assert.True(result.ImportParameters!.CircleApproximationEnabled);
        Assert.Contains(result.Warnings,w=>w.Contains("CIRCLE örneklendi"));
        Assert.Contains(result.PieceSources!.Value,source=>source.PieceId==piece.Id && source.EntityType=="CIRCLE");
        Assert.Contains(result.SourceGeometry!.Records,source=>source.EntityType=="CIRCLE");
        input.Position=0;Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithBulgeApproximation(input,"old",1,DxfTextEncoding.Utf8,0.01));
        input.Position=0;Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(input,"old",1));
    }

    [Theory]
    [InlineData("10\n0\n20\n0\n40\n0\n")]
    [InlineData("10\n0\n20\n0\n40\n1e-400\n")]
    [InlineData("10\n1e-400\n20\n0\n40\n1\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n40\n2\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n30\n1\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n39\n1\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n210\n0\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n210\n0\n220\n0\n230\n-1\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n60\n1\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n67\n1\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n6\nDASHED\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n370\n25\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n1001\nCUSTOM\n1000\nprivate\n")]
    [InlineData("10\n0\n20\n0\n40\n1\n100\nUnknownClass\n")]
    public void UnsupportedOrAmbiguousCircleRejectsEntireImport(string fields)
    {
        using var input=Stream("0\nLINE\n10\n0\n20\n0\n11\n1\n21\n0\n0\nCIRCLE\n"+fields);
        Assert.Throws<InvalidDataException>(()=>Import(input));
    }

    [Fact]
    public void ImportChoiceParticipatesInReviewFingerprint()
    {
        using var input=Stream(Circle);var result=Import(input);
        var changed=result with { ImportParameters=result.ImportParameters! with { CircleApproximationEnabled=false } };
        Assert.NotEqual(DxfTextileGrainlineMapping.CaptureFingerprint(result),DxfTextileGrainlineMapping.CaptureFingerprint(changed));
    }
}
