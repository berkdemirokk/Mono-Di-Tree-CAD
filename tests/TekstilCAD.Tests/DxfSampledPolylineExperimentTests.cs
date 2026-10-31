using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfSampledPolylineExperimentTests
{
    private static DxfImportResult Read(int vertexFlag=8, int polylineFlag=1, string extra="", string vertexExtra="", bool experiment=true)
    {
        var text="0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n20\n20\n0\n11\n30\n21\n0\n"+
            $"0\nPOLYLINE\n70\n{polylineFlag}\n{extra}0\nVERTEX\n10\n0\n20\n0\n70\n0\n"+
            $"0\nVERTEX\n10\n10\n20\n0\n70\n{vertexFlag}\n{vertexExtra}"+
            "0\nVERTEX\n10\n0\n20\n10\n70\n0\n0\nSEQEND\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        return experiment ? DxfFormat.ImportWithSampledPolylineExperiment(stream,"sentetik",1,DxfTextEncoding.Utf8) : DxfFormat.Import(stream,"sentetik");
    }

    [Fact] public void ExplicitOptInPreservesAllVerticesInOrder()
    {
        var result=Read();
        var piece=Assert.Single(result.Project.Pieces,p=>p.Closed);
        Assert.Equal(3,piece.Points.Count);
        Assert.Equal(10,piece.Points[1].X);
        Assert.Contains(result.Warnings,w=>w.Contains("üretime uygunluk garanti edilmez"));
    }
    [Fact] public void DefaultStillSkipsFlag8() => Assert.Single(Read(experiment:false).Project.Pieces);
    [Theory]
    [InlineData(16,1,"","")]
    [InlineData(4,1,"","")]
    [InlineData(8,5,"","")]
    [InlineData(8,1,"75\n6\n","")]
    [InlineData(8,1,"76\n1\n","")]
    [InlineData(8,1,"","42\n0.5\n")]
    public void UnvalidatedCasesAreSkipped(int flag,int polylineFlag,string extra,string vertexExtra)
    {
        var result=Read(flag,polylineFlag,extra,vertexExtra);
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.StartsWith("Atlanan çoklu çizgi: 1."));
    }
}
