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

    private static MemoryStream WithOwnerTable(string records,string circleFields)
    {
        var source="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nBLOCK_RECORD\n"+records+
            "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n10\n0\n20\n0\n40\n5\n"+
            circleFields+"0\nENDSEC\n0\nEOF\n";
        return new MemoryStream(Encoding.UTF8.GetBytes(source));
    }
    private const string OwnerRecords="0\nBLOCK_RECORD\n2\n*Model_Space\n5\nA1\n0\nBLOCK_RECORD\n2\n*Paper_Space\n5\nB2\n";

    [Theory]
    [InlineData("A1")] [InlineData("0000a1")]
    public void CircleOwnerMustResolveToTheUniqueModelSpaceBlock(string owner)
    {
        using var input=WithOwnerTable(OwnerRecords,"5\nF3\n330\n"+owner+"\n");
        var result=Import(input);Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("tanıtıcıları"));
    }

    [Theory]
    [InlineData("330\nB2\n")]
    [InlineData("330\nA2\n")]
    [InlineData("330\n0\n")]
    [InlineData("330\nnothex\n")]
    [InlineData("330\nA1\n330\nA1\n")]
    [InlineData("5\nA1\n330\nA1\n")]
    [InlineData("5\nX\n")]
    [InlineData("5\n12\n5\n13\n")]
    public void InvalidOrNonModelOwnerDoesNotImport(string fields)
    {
        using var input=WithOwnerTable(OwnerRecords,fields);Assert.Throws<InvalidDataException>(()=>Import(input));
    }

    [Fact]
    public void MissingAmbiguousOrDuplicateBlockRecordsCannotVerifyOwner()
    {
        using var missing=Stream(Circle+"330\nA1\n");Assert.Throws<InvalidDataException>(()=>Import(missing));
        using var duplicate=WithOwnerTable(OwnerRecords+"0\nBLOCK_RECORD\n2\nOther\n5\n00A1\n","330\nA1\n");
        Assert.Throws<InvalidDataException>(()=>Import(duplicate));
        using var modelDuplicate=WithOwnerTable(OwnerRecords+"0\nBLOCK_RECORD\n2\n*model_space\n5\nC3\n","330\nA1\n");
        Assert.Throws<InvalidDataException>(()=>Import(modelDuplicate));
    }

    [Fact]
    public void IndependentlyGeneratedEzdxfCircleHasModelOwnerAndExpectedBounds()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);
        var path=Path.Combine(root!.FullName,"tests","TekstilCAD.Tests","Fixtures","ezdxf-circle-owner.dxf");
        var bytes=File.ReadAllBytes(path);
        Assert.Equal("951066BE129929750030C9214D2D816F9EC8ABB3384532B5659E6580869A9824",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        using var input=new MemoryStream(bytes);var result=Import(input);var piece=Assert.Single(result.Project.Pieces);
        Assert.True(piece.Closed);
        Assert.Equal(5,piece.Points.Min(p=>p.X),8);Assert.Equal(15,piece.Points.Max(p=>p.X),8);
        Assert.Equal(15,piece.Points.Min(p=>p.Y),8);Assert.Equal(25,piece.Points.Max(p=>p.Y),8);
        Assert.Contains(result.PieceSources!.Value,source=>source.EntityType=="CIRCLE");
    }

    [Fact]
    public void ImportChoiceParticipatesInReviewFingerprint()
    {
        using var input=Stream(Circle);var result=Import(input);
        var changed=result with { ImportParameters=result.ImportParameters! with { CircleApproximationEnabled=false } };
        Assert.NotEqual(DxfTextileGrainlineMapping.CaptureFingerprint(result),DxfTextileGrainlineMapping.CaptureFingerprint(changed));
    }
}
