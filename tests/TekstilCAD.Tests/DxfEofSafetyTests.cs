using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfEofSafetyTests
{
    private const string Drawing = "0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n3\n21\n0\n0\nENDSEC\n0\nEOF\n";

    private static DxfImportResult Import(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return DxfFormat.Import(stream, "fixture");
    }

    [Fact]
    public void FinalEofAcceptsValidDrawing()
    {
        Assert.Single(Import(Drawing).Project.Pieces);
    }

    [Theory]
    [InlineData("0\nEOF\n")]
    [InlineData("999\nextra data\n")]
    [InlineData("0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n5\n20\n0\n11\n7\n21\n0\n0\nENDSEC\n0\nEOF\n")]
    public void RecordsAfterEofAreRejected(string trailingRecords)
    {
        var error = Assert.Throws<InvalidDataException>(() => Import(Drawing + trailingRecords));
        Assert.Contains("EOF", error.Message);
    }
}
