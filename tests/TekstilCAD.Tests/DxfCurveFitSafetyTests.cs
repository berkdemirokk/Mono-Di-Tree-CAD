using System.Text;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public class DxfCurveFitSafetyTests
{
    [Theory]
    [InlineData(8)] [InlineData(16)] [InlineData(64)]
    public void UnsupportedMeshWithFaceVerticesIsSkippedBeforeCoordinateParsing(int flags)
    {
        var entities=$"0\nPOLYLINE\n70\n{flags}\n0\nVERTEX\n70\n128\n71\n1\n72\n2\n0\nSEQEND\n0\nLINE\n10\n0\n20\n1\n11\n10\n21\n1\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n"));
        var result=DxfFormat.Import(stream,"mesh");
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("3D/mesh POLYLINE"));
    }
    [Theory]
    [InlineData(2,0)] [InlineData(4,0)] [InlineData(0,1)] [InlineData(0,2)] [InlineData(0,8)] [InlineData(0,16)]
    public void CurveFittedPolylinesAreReportedAndNotImportedAsStraightSegments(int polyFlag,int vertexFlag)
    {
        var entities=$"0\nPOLYLINE\n70\n{polyFlag}\n0\nVERTEX\n70\n{vertexFlag}\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n0\nLINE\n10\n0\n20\n1\n11\n10\n21\n1\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n"));
        var result=DxfFormat.Import(stream,"fit");
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("spline POLYLINE"));
    }
    [Fact]
    public void FittedSourceRecordCountsDistinguishModelAndBlockDefinitions()
    {
        const string poly="0\nPOLYLINE\n70\n2\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n";
        var dxf="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nB\n"+poly+"0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n"+poly+poly+"0\nLINE\n10\n0\n20\n1\n11\n10\n21\n1\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        var result=DxfFormat.Import(stream,"fit-count");
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("ENTITIES 2, BLOCKS 1") && w.Contains("parça veya yerleştirilmiş blok sayısı değildir"));
    }
}
