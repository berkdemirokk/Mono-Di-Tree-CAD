using System.Text;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public class DxfHeaderUnitsTests
{
    private static DxfImportResult Parse(string header, double factor=1, string extra="")
    {
        var text="0\nSECTION\n2\nHEADER\n"+header+"0\nENDSEC\n0\nSECTION\n2\nENTITIES\n"+extra+"0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(text)); return DxfFormat.Import(input,"Units",factor);
    }
    [Theory]
    [InlineData(1,25.4)] [InlineData(2,304.8)] [InlineData(4,1)] [InlineData(5,10)] [InlineData(6,1000)] [InlineData(10,914.4)] [InlineData(13,0.001)] [InlineData(14,100)]
    public void KnownHeaderIsReportedWithoutAutomaticScaling(int code,double expected)
    {
        var result=Parse($"9\n$INSUNITS\n70\n{code}\n",1);
        Assert.True(result.Units.IsReliable); Assert.Equal(expected,result.Units.MillimetersPerUnit);
        Assert.Equal(10,Math.Abs(result.Project.Pieces[0].Points[1].X-result.Project.Pieces[0].Points[0].X));
        Assert.Contains(result.Warnings,w=>expected==1 ? w.StartsWith("DXF birim başlığı mevcut;") : w.Contains("uyuşmazlığı"));
    }
    [Theory]
    [InlineData("")] [InlineData("9\n$INSUNITS\n70\n0\n")] [InlineData("9\n$INSUNITS\n70\n999\n")]
    [InlineData("9\n$INSUNITS\n70\nx\n")] [InlineData("9\n$INSUNITS\n70\n4\n70\n5\n")]
    [InlineData("9\n$INSUNITS\n70\n4\n9\n$INSUNITS\n70\n4\n")]
    public void UncertainHeadersNeverProduceKnownFactor(string header)
    { var result=Parse(header); Assert.False(result.Units.IsReliable); Assert.Null(result.Units.MillimetersPerUnit); Assert.Contains(result.Warnings,w=>w.Contains("güvenilir biçimde belirlenemedi")); }
    [Fact] public void HeaderLikeEntityDataDoesNotSetDrawingUnits()
    { var result=Parse("",1,"0\nTEXT\n9\n$INSUNITS\n70\n4\n"); Assert.False(result.Units.IsReliable); Assert.Null(result.Units.Code); }
    [Fact] public void SelectedFactorRemainsAuthoritativeEvenWhenMismatched()
    { var result=Parse("9\n$INSUNITS\n70\n5\n",25.4); Assert.Equal(254,Math.Abs(result.Project.Pieces[0].Points[1].X-result.Project.Pieces[0].Points[0].X),8); Assert.Contains(result.Warnings,w=>w.Contains("uyuşmazlığı")); }
}
