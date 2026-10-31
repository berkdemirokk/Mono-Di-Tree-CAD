using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfRepeatedPlaneFlagsTests
{
    private static DxfImportResult Import(string entities)
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.Import(stream,"sentetik");
    }

    private static string Line(string fields) => $"0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n{fields}";
    private static string Lightweight(string fields) => $"0\nLWPOLYLINE\n90\n3\n10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n{fields}";
    private static string Polyline(string headerFields,string vertexFields) =>
        $"0\nPOLYLINE\n{headerFields}0\nVERTEX\n10\n0\n20\n0\n{vertexFields}0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n";

    [Theory]
    [InlineData("30", "0", "100")]
    [InlineData("31", "0", "100")]
    [InlineData("38", "0", "1")]
    [InlineData("39", "0", "1")]
    [InlineData("210", "0", "1")]
    [InlineData("220", "0", "1")]
    [InlineData("230", "1", "-1")]
    public void LineRejectsRepeatedPlaneOrExtrusionGroup(string code,string first,string second)
    {
        Assert.Throws<InvalidDataException>(()=>Import(Line($"{code}\n{first}\n{code}\n{second}\n")));
    }

    [Fact]
    public void LightweightPolylineRejectsRepeatedPlaneGroup()
    {
        Assert.Throws<InvalidDataException>(()=>Import(Lightweight("38\n0\n38\n10\n")));
    }

    [Fact]
    public void LegacyPolylineVertexRejectsRepeatedPlaneAndBulgeGroups()
    {
        Assert.Throws<InvalidDataException>(()=>Import(Polyline("", "30\n0\n30\n1\n")));
        Assert.Throws<InvalidDataException>(()=>Import(Polyline("", "42\n0\n42\n1\n")));
    }

    [Theory]
    [InlineData("70\n0\n70\n1\n")]
    [InlineData("70\n1.5\n")]
    [InlineData("70\n-1\n")]
    [InlineData("70\n32768\n")]
    public void PolylineFlagsMustBeUniqueNonnegativeIntegers(string flags)
    {
        Assert.Throws<InvalidDataException>(()=>Import(Lightweight(flags)));
        Assert.Throws<InvalidDataException>(()=>Import(Polyline(flags,"")));
        Assert.Throws<InvalidDataException>(()=>Import(Polyline("",flags)));
    }

    [Fact]
    public void ValidTwoDimensionalFlagsRemainSupported()
    {
        Assert.True(Assert.Single(Import(Lightweight("70\n129\n230\n1\n")).Project.Pieces).Closed);
        Assert.True(Assert.Single(Import(Polyline("70\n1\n", "70\n0\n230\n1\n")).Project.Pieces).Closed);
        Assert.False(Assert.Single(Import(Line("30\n0\n31\n0\n230\n1\n")).Project.Pieces).Closed);
    }
}
