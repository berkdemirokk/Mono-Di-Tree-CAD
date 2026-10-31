using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfBulgeParserIntegrationTests
{
    private static MemoryStream Stream(string body) => new(Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n"+body+"0\nENDSEC\n0\nEOF\n"));
    [Fact]
    public void OptInSamplesOpenLightweightPolylineAndDefaultStillSkips()
    {
        const string body="0\nLWPOLYLINE\n90\n2\n70\n0\n10\n0\n20\n0\n42\n1\n10\n2\n20\n0\n";
        using var input=Stream(body);
        var imported=DxfFormat.ImportWithBulgeApproximation(input,"bulge",1,DxfTextEncoding.Utf8,0.1);
        var piece=Assert.Single(imported.Project.Pieces);
        Assert.True(piece.Points.Count>2);
        Assert.True(piece.Points.Min(p=>p.Y)<0);
        Assert.Equal(0.1,imported.ImportParameters!.BulgeToleranceMm);
        Assert.Contains(imported.Warnings,w=>w.Contains("nominal"));
        input.Position=0;
        Assert.Throws<InvalidDataException>(() => DxfFormat.Import(input,"legacy",1));
        using var mixed=Stream(body+"0\nLINE\n10\n10\n20\n0\n11\n11\n21\n0\n");
        var legacy=DxfFormat.Import(mixed,"legacy",1);
        Assert.Single(legacy.Project.Pieces);
        Assert.Contains(legacy.Warnings, warning => warning.Contains("bulge",StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void ClassicVertexBulgeIsAssociatedWithItsOwnStartingVertex()
    {
        const string body="0\nPOLYLINE\n70\n0\n0\nVERTEX\n10\n0\n20\n0\n42\n-1\n0\nVERTEX\n10\n2\n20\n0\n0\nSEQEND\n";
        using var input=Stream(body);
        var piece=Assert.Single(DxfFormat.ImportWithBulgeApproximation(input,"legacy",1,DxfTextEncoding.Utf8,0.1).Project.Pieces);
        Assert.True(piece.Points.Max(p=>p.Y)>0);
    }
    [Theory]
    [InlineData("42\n1\n42\n0\n")]
    [InlineData("42\n1e-999\n")]
    public void AmbiguousOrUnderflowingBulgeRejectsWholeImport(string bulge)
    {
        using var input=Stream("0\nLWPOLYLINE\n90\n2\n70\n0\n10\n0\n20\n0\n"+bulge+"10\n2\n20\n0\n");
        Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithBulgeApproximation(input,"bad",1,DxfTextEncoding.Utf8,0.1));
    }
    [Fact]
    public void UnsupportedInsertOrDegenerateClosedContourRejectsWholeImport()
    {
        using var inserted=Stream("0\nINSERT\n2\nBLOCK\n");
        Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithBulgeApproximation(inserted,"bad",1,DxfTextEncoding.Utf8,0.1));
        using var invalid=Stream("0\nLWPOLYLINE\n90\n2\n70\n1\n10\n0\n20\n0\n10\n1\n20\n0\n");
        Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithBulgeApproximation(invalid,"bad",1,DxfTextEncoding.Utf8,0.1));
    }
    [Theory]
    [InlineData("ARC")]
    [InlineData("CIRCLE")]
    [InlineData("SPLINE")]
    [InlineData("UNRECOGNIZED_VENDOR_GEOMETRY")]
    public void UnsupportedGeometryRejectsOptInButNotLegacyPath(string type)
    {
        const string line="0\nLINE\n10\n0\n20\n0\n11\n5\n21\n0\n";
        using var input=Stream(line+"0\n"+type+"\n");
        Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithBulgeApproximation(input,"bad",1,DxfTextEncoding.Utf8,0.1));
        input.Position=0;
        Assert.Single(DxfFormat.Import(input,"legacy",1).Project.Pieces);
    }
    [Fact]
    public void AnnotationIsStillReportedAndNoArcWarningIsAccurate()
    {
        const string line="0\nLINE\n10\n0\n20\n0\n11\n5\n21\n0\n";
        using var input=Stream(line+"0\nTEXT\n10\n0\n20\n0\n1\nhello\n");
        var result=DxfFormat.ImportWithBulgeApproximation(input,"text",1,DxfTextEncoding.Utf8,0.1);
        Assert.Single(result.Project.Pieces);
        Assert.Contains(result.Warnings,w=>w.Contains("dönüştürülen yay yok"));
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("Bulge eğrileri örneklendi"));
    }
    [Theory]
    [InlineData("0\nLINE\n10\n0\n20\n0\n11\n5\n21\n0\n")]
    [InlineData("0\nLWPOLYLINE\n90\n2\n70\n0\n10\n0\n20\n0\n10\n5\n20\n0\n")]
    [InlineData("0\nLINE\n60\n1\n10\n0\n20\n0\n11\n5\n21\n0\n")]
    public void OptInRejectsBlockDefinitionGeometryEvenWithoutInsert(string blockGeometry)
    {
        var dxf="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nB\n"+blockGeometry+
            "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n10\n20\n0\n11\n15\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithBulgeApproximation(input,"block",1,DxfTextEncoding.Utf8,0.1));
    }
    [Fact]
    public void OptInPermitsBlockStructureAndAnnotationWithoutGeometry()
    {
        var dxf="0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nB\n0\nTEXT\n10\n0\n20\n0\n1\nlabel\n0\nENDBLK\n0\nENDSEC\n"+
            "0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n5\n21\n0\n0\nENDSEC\n0\nEOF\n";
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        Assert.Single(DxfFormat.ImportWithBulgeApproximation(input,"block-text",1,DxfTextEncoding.Utf8,0.1).Project.Pieces);
    }
}
