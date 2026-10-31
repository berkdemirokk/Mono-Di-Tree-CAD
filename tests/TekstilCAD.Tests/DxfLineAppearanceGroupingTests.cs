using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfLineAppearanceGroupingTests
{
    private static DxfImportResult Read(string first,string second)
    {
        var source="0\nSECTION\n2\nENTITIES\n"+
            $"0\nLINE\n8\nCUT\n6\nCONTINUOUS\n10\n0\n20\n0\n11\n10\n21\n0\n{first}"+
            $"0\nLINE\n8\nCUT\n6\nCONTINUOUS\n10\n10\n20\n0\n11\n20\n21\n0\n{second}"+
            "0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        return DxfFormat.Import(stream,"appearance");
    }

    [Theory]
    [InlineData("370\n25\n","370\n50\n")]
    [InlineData("440\n33554560\n","440\n33554600\n")]
    [InlineData("","370\n-1\n")]
    [InlineData("","440\n33554560\n")]
    public void DifferentOrUnresolvedDeclarationsDoNotJoin(string first,string second)
    {
        var result=Read(first,second);
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Warnings,w=>w.Contains("çizgi kalınlığı veya saydamlık"));
    }

    [Theory]
    [InlineData("370\n25\n")]
    [InlineData("440\n33554560\n")]
    [InlineData("")]
    public void MatchingDeclarationsCanJoin(string field)
        => Assert.Single(Read(field,field).Project.Pieces);

    [Theory]
    [InlineData("370\n25\n370\n50\n")]
    [InlineData("440\ninvalid\n")]
    public void AmbiguousDeclarationsAreRejected(string field)
        => Assert.Throws<InvalidDataException>(()=>Read(field,""));
}
