using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfPolylinePrecisionSafetyTests
{
    private static DxfImportResult Import(string entities,double factor=1)
    {
        var text=$"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfFormat.Import(stream,"sentetik",factor);
    }

    private static string Polyline(bool lightweight,bool closed,params (string X,string Y)[] points)
    {
        if(lightweight)
            return $"0\nLWPOLYLINE\n6\nCONTINUOUS\n90\n{points.Length}\n70\n{(closed?1:0)}\n"+
                string.Concat(points.Select(p=>$"10\n{p.X}\n20\n{p.Y}\n"));
        return $"0\nPOLYLINE\n6\nCONTINUOUS\n70\n{(closed?1:0)}\n"+
            string.Concat(points.Select(p=>$"0\nVERTEX\n10\n{p.X}\n20\n{p.Y}\n"))+"0\nSEQEND\n";
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DifferentSourcePointsCollapsingAfterScalingAreRejected(bool lightweight)
    {
        var poly=Polyline(lightweight,false,("1e-300","0"),("2e-300","0"));
        var error=Assert.Throws<InvalidDataException>(()=>Import(poly,1e-50));
        Assert.Contains("hassasiyeti",error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonAdjacentSourcePointsCollapsingAfterScalingAreRejected(bool lightweight)
    {
        var poly=Polyline(lightweight,true,("1e-300","0"),("1e-300","1"),("2e-300","0"));
        var error=Assert.Throws<InvalidDataException>(()=>Import(poly,1e-50));
        Assert.Contains("hassasiyeti",error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RepeatedClosingPointIsPreserved(bool lightweight)
    {
        var poly=Polyline(lightweight,true,("0","0"),("10","0"),("0","10"),("0","0"));
        var piece=Assert.Single(Import(poly).Project.Pieces);
        Assert.True(piece.Closed);
        Assert.Equal(4,piece.Points.Count);
        Assert.Equal(piece.Points[0],piece.Points[^1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SmallDistinctPointsRemainDistinct(bool lightweight)
    {
        var poly=Polyline(lightweight,false,("0","0"),("0.000000001","0"));
        var piece=Assert.Single(Import(poly).Project.Pieces);
        Assert.Equal(new Point2(0,0),piece.Points[0]);
        Assert.Equal(new Point2(1e-9,0),piece.Points[1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ScalingOverflowIsRejectedAsInvalidData(bool lightweight)
    {
        var poly=Polyline(lightweight,false,("1e308","0"),("1.1e308","0"));
        var error=Assert.Throws<InvalidDataException>(()=>Import(poly,2));
        Assert.Contains("çalışma sınırlarını",error.Message);
    }
}
