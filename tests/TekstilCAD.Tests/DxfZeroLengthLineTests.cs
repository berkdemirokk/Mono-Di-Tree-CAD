using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfZeroLengthLineTests
{
    private static DxfImportResult Import(string entities,double millimetersPerUnit=1)
    {
        var dxf=$"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        return DxfFormat.Import(stream,"sentetik",millimetersPerUnit);
    }

    private static string Line(string x1,string y1,string x2,string y2,string layer="0") =>
        $"0\nLINE\n8\n{layer}\n6\nCONTINUOUS\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}\n";

    [Fact]
    public void ZeroLengthLineIsSkippedAndReportedWithoutLosingOtherGeometry()
    {
        var result=Import(Line("0","0","0","0")+Line("10","20","30","20"));
        var piece=Assert.Single(result.Project.Pieces);
        Assert.False(piece.Closed);
        Assert.Equal(new[] { new Point2(10,20),new Point2(30,20) },piece.Points);
        Assert.Contains(result.Warnings,w=>w.Contains("sıfır uzunluklu LINE",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OnlyZeroLengthLinesAreRejectedWithAnExplanation()
    {
        var error=Assert.Throws<InvalidDataException>(()=>Import(Line("0","0","-0","0")));
        Assert.Contains("sıfır uzunluklu LINE",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonzeroSubMicronLineIsNotDiscardedAsZero()
    {
        var result=Import(Line("0","0","0.000000001","0"));
        var piece=Assert.Single(result.Project.Pieces);
        Assert.Equal(0,piece.Points[0].X);
        Assert.Equal(1e-9,piece.Points[1].X);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("sıfır uzunluklu LINE",StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(25.4)]
    public void SkippingZeroLinePreservesScaledConnectedAndSeparateLines(double factor)
    {
        var source=Line("0","0","0","0","sifir")+
                   Line("10","0","20","0","bagli")+
                   Line("20","0","30","0","bagli")+
                   Line("50","0","60","0","ayri");
        var result=Import(source,factor);
        Assert.Contains(result.Warnings,w=>w.Contains("sıfır uzunluklu LINE",StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2,result.Project.Pieces.Count);
        var connected=Assert.Single(result.Project.Pieces,p=>p.Name=="bagli");
        var separate=Assert.Single(result.Project.Pieces,p=>p.Name=="ayri");
        Assert.Equal(3,connected.Points.Count);
        Assert.Equal(10*factor,connected.Points.Min(p=>p.X),10);
        Assert.Equal(30*factor,connected.Points.Max(p=>p.X),10);
        Assert.Equal(50*factor,separate.Points.Min(p=>p.X),10);
        Assert.Equal(60*factor,separate.Points.Max(p=>p.X),10);
    }
}
