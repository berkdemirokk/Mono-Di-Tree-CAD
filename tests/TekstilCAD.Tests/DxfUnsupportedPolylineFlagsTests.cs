using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfUnsupportedPolylineFlagsTests
{
    private static DxfImportResult Import(string entities)
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.Import(input,"sentetik");
    }

    private static string Lightweight(int flags) => $"0\nLWPOLYLINE\n90\n3\n70\n{flags}\n10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n";
    private static string Polyline(int flags) => $"0\nPOLYLINE\n70\n{flags}\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n";
    private const string SafeLine="0\nLINE\n10\n20\n20\n0\n11\n30\n21\n0\n";

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(256)]
    [InlineData(257)]
    public void LightweightUnknownBitsCannotBeImportedAsTwoDimensionalContours(int flags)
    {
        var error=Assert.Throws<InvalidDataException>(()=>Import(Lightweight(flags)+SafeLine));
        Assert.Contains("LWPOLYLINE",error.Message);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(512)]
    public void LegacyUnknownBitsCannotBeImportedAsTwoDimensionalContours(int flags)
    {
        var error=Assert.Throws<InvalidDataException>(()=>Import(Polyline(flags)+SafeLine));
        Assert.Contains("POLYLINE",error.Message);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(160)]
    public void MeshDirectionFlagSkipsPolylineAndReportsItAlongsideValidGeometry(int flags)
    {
        var result=Import(Polyline(flags)+SafeLine);
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("3D/mesh POLYLINE"));
    }

    [Theory]
    [InlineData(0,false)]
    [InlineData(1,true)]
    [InlineData(128,false)]
    [InlineData(129,true)]
    public void DocumentedTwoDimensionalFlagsRemainSupported(int flags,bool closed)
    {
        Assert.Equal(closed,Assert.Single(Import(Lightweight(flags)).Project.Pieces).Closed);
        Assert.Equal(closed,Assert.Single(Import(Polyline(flags)).Project.Pieces).Closed);
    }
}
