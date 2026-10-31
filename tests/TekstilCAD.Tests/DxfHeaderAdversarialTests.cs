using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfHeaderAdversarialTests
{
    private static DxfImportResult Parse(string prefix, double factor = 1, string entities = "")
    {
        var text = prefix + "0\nSECTION\n2\nENTITIES\n" + entities
            + "0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfFormat.Import(stream, "Header test", factor);
    }

    [Fact]
    public void ConflictingHeadersInSeparateSectionsRemainUnreliable()
    {
        var result = Parse("0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n4\n0\nENDSEC\n"
            + "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n");
        Assert.False(result.Units.IsReliable);
        Assert.Null(result.Units.MillimetersPerUnit);
        Assert.Contains(result.Warnings, w => w.Contains("güvenilir biçimde belirlenemedi"));
    }

    [Theory]
    [InlineData("9\n$INSUNITS\n70\n4\n9\n$INSUNITS\n70\n1\n")]
    [InlineData("9\n$INSUNITS\n70\n4\n70\n4\n")]
    [InlineData("9\n$INSUNITS\n70\n-1\n")]
    [InlineData("9\n$INSUNITS\n70\n1.5\n")]
    [InlineData("9\n$INSUNITS\n70\n999999999999999999999\n")]
    public void ConflictingOrMalformedUnitValuesAreNotTrusted(string header)
    {
        var result = Parse("0\nSECTION\n2\nHEADER\n" + header + "0\nENDSEC\n");
        Assert.False(result.Units.IsReliable);
        Assert.Null(result.Units.MillimetersPerUnit);
        Assert.Contains(result.Warnings, w => w.Contains("güvenilir biçimde belirlenemedi"));
    }

    [Fact]
    public void EntityTextDoesNotOverrideHeaderAndMismatchIsReported()
    {
        var result = Parse("0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n", 1,
            "0\nTEXT\n1\n$INSUNITS\n70\n4\n");
        Assert.True(result.Units.IsReliable);
        Assert.Equal(1, result.Units.Code);
        Assert.Equal(25.4, result.Units.MillimetersPerUnit);
        Assert.Contains(result.Warnings, w => w.Contains("uyuşmazlığı"));
        Assert.Equal(10, Math.Abs(result.Project.Pieces[0].Points[1].X - result.Project.Pieces[0].Points[0].X));
    }
}
