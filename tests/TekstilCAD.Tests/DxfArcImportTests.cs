using System.Text;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;
public sealed class DxfArcImportTests
{
    private static MemoryStream Stream(string body)=>new(Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n"+body+"0\nENDSEC\n0\nEOF\n"));
    private const string Arc="0\nARC\n8\nCUT\n10\n10\n20\n20\n40\n5\n50\n350\n51\n10\n";
    private static DxfImportResult Import(Stream source)=>DxfFormat.ImportWithCircularCurvesApproximation(source,"arc",1,DxfTextEncoding.Utf8,0.01);

    [Fact] public void ExplicitModeCreatesOpenWrappedArcAndPreservesSourceLink()
    {
        using var input=Stream(Arc);var result=Import(input);var piece=Assert.Single(result.Project.Pieces);
        Assert.False(piece.Closed);Assert.True(piece.Points.Count>2);Assert.True(result.ImportParameters!.ArcApproximationEnabled);
        Assert.InRange(piece.Points[0].Y,19.1,19.2);Assert.InRange(piece.Points[^1].Y,20.8,20.9);
        Assert.Contains(result.Warnings,w=>w.Contains("ARC örneklendi"));
        Assert.Contains(result.PieceSources!.Value,s=>s.EntityType=="ARC" && s.PieceId==piece.Id);
        Assert.Contains(result.SourceGeometry!.Records,s=>s.EntityType=="ARC");
        input.Position=0;Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithCircleAndBulgeApproximation(input,"old",1,DxfTextEncoding.Utf8,0.1));
    }

    [Fact] public void CardinalArcAndLineCanJoinAtZeroToleranceWithoutTinyBridge()
    {
        const string source="0\nARC\n8\nCUT\n10\n0\n20\n0\n40\n10\n50\n0\n51\n90\n"+
            "0\nLINE\n8\nCUT\n10\n0\n20\n10\n11\n-5\n21\n10\n";
        using var input=Stream(source);var import=Import(input);
        var arcLink=Assert.Single(import.PieceSources!.Value,p=>p.EntityType=="ARC");
        var lineLink=Assert.Single(import.PieceSources!.Value,p=>p.EntityType=="LINE");
        var arc=Assert.Single(import.Project.Pieces,p=>p.Id==arcLink.PieceId);
        var line=Assert.Single(import.Project.Pieces,p=>p.Id==lineLink.PieceId);
        var beforeArc=arc.Points.ToArray();var beforeLine=line.Points.ToArray();
        var joined=OpenPathJoin.Join(arc,line,0);
        Assert.Equal(0,joined.BridgeLength);Assert.Equal(beforeArc.Length+beforeLine.Length-1,joined.Piece.Points.Count);
        Assert.Equal(beforeArc,arc.Points);Assert.Equal(beforeLine,line.Points);
    }

    [Theory]
    [InlineData("50\n0\n51\n360\n")]
    [InlineData("50\n1e-400\n51\n90\n")]
    [InlineData("50\n0\n51\n90\n50\n1\n")]
    [InlineData("50\n0\n")]
    [InlineData("50\n0\n51\nNaN\n")]
    [InlineData("50\n0\n51\n90\n30\n1\n")]
    [InlineData("50\n0\n51\n90\n210\n0\n")]
    [InlineData("50\n0\n51\n90\n60\n1\n")]
    [InlineData("50\n0\n51\n90\n6\nDASHED\n")]
    [InlineData("50\n0\n51\n90\n330\nA1\n")]
    [InlineData("50\n0\n51\n90\n1001\nX\n1000\nprivate\n")]
    public void InvalidOrAmbiguousArcRejectsWholeImport(string fields)
    {
        using var input=Stream("0\nLINE\n10\n0\n20\n0\n11\n1\n21\n0\n0\nARC\n10\n0\n20\n0\n40\n5\n"+fields);
        Assert.Throws<InvalidDataException>(()=>Import(input));
    }

    [Fact] public void IndependentEzdxfWrappedArcImportsWithModelOwner()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);
        var path=Path.Combine(root!.FullName,"tests","TekstilCAD.Tests","Fixtures","ezdxf-arc-wrap.dxf");
        var bytes=File.ReadAllBytes(path);
        Assert.Equal("D3F52B73D4F1C2F82A0F719E0A9841466ACD8053529E6BF1DF06398570D19952",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        using var input=new MemoryStream(bytes);var result=Import(input);var piece=Assert.Single(result.Project.Pieces);
        Assert.False(piece.Closed);Assert.InRange(piece.Points[0].Y,19.1,19.2);Assert.InRange(piece.Points[^1].Y,20.8,20.9);
    }

    [Fact] public void ImportOptionIsBoundToReviewFingerprint()
    {
        using var input=Stream(Arc);var result=Import(input);
        var modified=result with { ImportParameters=result.ImportParameters! with { ArcApproximationEnabled=false } };
        Assert.NotEqual(DxfTextileGrainlineMapping.CaptureFingerprint(result),DxfTextileGrainlineMapping.CaptureFingerprint(modified));
    }
}
