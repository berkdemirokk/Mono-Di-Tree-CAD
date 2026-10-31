using System.Text;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public class DxfMalformedStructureTests
{
    private static void Reject(string entity)
    {
        var data="0\nSECTION\n2\nENTITIES\n"+entity+"0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(data));
        Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(stream,"fixture"));
    }
    [Theory]
    [InlineData("0\nLWPOLYLINE\n90\n3\n10\n0\n20\n0\n10\n1\n20\n1\n")]
    [InlineData("0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n20\n2\n10\n1\n20\n1\n")]
    [InlineData("0\nLWPOLYLINE\n90\n2\n90\n2\n10\n0\n20\n0\n10\n1\n20\n1\n")]
    [InlineData("0\nLINE\n10\n0\n10\n3\n20\n0\n11\n1\n21\n1\n")]
    [InlineData("0\nPOLYLINE\n70\n0\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n1\n20\n1\n")]
    public void MalformedSupportedEntityIsRejected(string entity) => Reject(entity);
}
