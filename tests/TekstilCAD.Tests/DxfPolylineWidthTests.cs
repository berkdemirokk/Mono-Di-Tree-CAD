using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfPolylineWidthTests
{
    private const string SafeLine="0\nLINE\n8\nSAĞLAM\n10\n0\n20\n20\n11\n10\n21\n20\n";
    private static DxfImportResult Import(string entities,double factor=1)
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.Import(stream,"width",factor);
    }
    private static string Lightweight(string fields) => $"0\nLWPOLYLINE\n8\nGENIS\n90\n2\n70\n0\n{fields}10\n0\n20\n0\n10\n10\n20\n0\n";
    private static string Legacy(string header,string vertex) => $"0\nPOLYLINE\n8\nGENIS\n70\n0\n{header}0\nVERTEX\n10\n0\n20\n0\n{vertex}0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n";

    [Theory]
    [InlineData("43\n4\n")]
    [InlineData("40\n.1\n")]
    [InlineData("41\n2\n")]
    [InlineData("40\n0\n41\n.01\n")]
    public void NonzeroLightweightWidthsSkipOnlyAffectedContour(string fields)
    {
        var result=Import(Lightweight(fields)+SafeLine,2);
        var safe=Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("genişlikli LWPOLYLINE"));
        Assert.Equal(new Point2(0,40),safe.Points[0]);
        Assert.Equal(new Point2(20,40),safe.Points[1]);
    }

    [Theory]
    [InlineData("40\n4\n","")]
    [InlineData("41\n4\n","")]
    [InlineData("","40\n1\n")]
    [InlineData("","41\n1\n")]
    public void NonzeroLegacyWidthsSkipOnlyAffectedContour(string header,string vertex)
    {
        var result=Import(Legacy(header,vertex)+SafeLine);
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("genişlikli POLYLINE"));
    }

    [Fact]
    public void ZeroAndAbsentWidthsKeepExistingGeometry()
    {
        var result=Import(Lightweight("43\n0\n40\n0\n41\n0\n")+Legacy("40\n0\n41\n0\n","40\n0\n41\n0\n"));
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("genişlikli"));
        Assert.All(result.Project.Pieces,p=>Assert.Equal(new[]{new Point2(0,0),new Point2(10,0)},p.Points));
    }

    [Fact]
    public void InvalidWidthIsRejectedEvenAfterNonzeroWidth()
    {
        Assert.Throws<InvalidDataException>(()=>Import(Lightweight("40\n1\n41\nNaN\n")+SafeLine));
        Assert.Throws<InvalidDataException>(()=>Import(Legacy("40\n1\n","41\nnot-a-number\n")+SafeLine));
    }

    [Fact]
    public void WidthOnlyFileFailsWithExplicitReasonAndCancelledTokenStops()
    {
        var error=Assert.Throws<InvalidDataException>(()=>Import(Lightweight("43\n4\n")));
        Assert.Contains("genişlikli LWPOLYLINE",error.Message);
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes("0\nEOF\n"));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfFormat.Import(stream,"cancel",1,cancellation.Token));
    }
}
