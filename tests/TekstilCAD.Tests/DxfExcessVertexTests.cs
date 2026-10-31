using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfExcessVertexTests
{
    [Fact]
    public void RejectsExcessVertexBeforeParsingItsYValue()
    {
        const string data = "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n10\n1\n20\n1\n10\n2\n20\nnot-a-number\n0\nENDSEC\n0\nEOF\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var error = Assert.Throws<InvalidDataException>(() => DxfFormat.Import(input, "excess"));
        Assert.Contains("nokta sayısını aşıyor", error.Message);
    }
}
