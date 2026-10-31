using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfUnsupportedWarningBoundsTests
{
    [Fact]
    public void UnsupportedTypeSummaryIsBoundedWhileSourceDetailsRemainAvailable()
    {
        var source=new StringBuilder("0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n");
        for(var i=0;i<80;i++)source.Append("0\nVENDOR_").Append(i).Append('_').Append('A',80).Append("\n8\nMARK\n1\ndata\n");
        source.Append("0\nENDSEC\n0\nEOF\n");
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source.ToString()));
        var import=DxfFormat.Import(stream,"bounded warnings");
        var warning=Assert.Single(import.Warnings,w=>w.StartsWith("Atlanan desteklenmeyen DXF verileri:"));
        Assert.True(warning.Length<=4096);
        Assert.Contains("özet kısaltıldı",warning);
        Assert.Equal(80,import.SkippedSourceDetails!.Records.Length);
        Assert.Contains(import.SkippedSourceDetails.Records,r=>r.Source.EntityType=="VENDOR_79_"+new string('A',80));
        Assert.False(import.SkippedSourceDetails.Truncated);
        new DxfImportProvenance { SourceFileName="source.dxf",SourceSha256=import.SourceSha256!,
            SourceByteLength=import.SourceByteLength!.Value,MillimetersPerUnit=1,Warnings=[..import.Warnings],
            PieceIds=import.Project.Pieces.Select(p=>p.Id).ToList() }.Validate();
    }

    [Fact]
    public void WarningTruncationDoesNotSplitSurrogatePairsOrExposeControlCharacters()
    {
        var entityType="VENDOR_"+new string('A',40)+"😀\tTAIL";
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n"+
            "0\n"+entityType+"\n8\nMARK\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var import=DxfFormat.Import(stream,"unicode warning");
        var warning=Assert.Single(import.Warnings,w=>w.StartsWith("Atlanan desteklenmeyen DXF verileri:"));
        Assert.Contains("özet kısaltıldı",warning);
        Assert.DoesNotContain(warning,char.IsControl);
        Assert.Equal(entityType,Assert.Single(import.SkippedSourceDetails!.Records).Source.EntityType);
        new DxfImportProvenance { SourceFileName="source.dxf",SourceSha256=import.SourceSha256!,
            SourceByteLength=import.SourceByteLength!.Value,MillimetersPerUnit=1,Warnings=[..import.Warnings],
            PieceIds=import.Project.Pieces.Select(p=>p.Id).ToList() }.Validate();
    }

    [Fact]
    public void DashedGeometryRemainsExplicitWhenUnsupportedSummaryIsFull()
    {
        var source=new StringBuilder("0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n6\nCONTINUOUS\n10\n0\n20\n0\n11\n1\n21\n0\n");
        for(var i=0;i<70;i++)source.Append("0\nVENDOR_").Append(i).Append("\n8\nMARK\n");
        source.Append("0\nLINE\n8\nCUT\n6\nDASHED\n10\n1\n20\n0\n11\n2\n21\n0\n0\nENDSEC\n0\nEOF\n");
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source.ToString()));
        var import=DxfFormat.Import(stream,"line warning");
        Assert.Single(import.Project.Pieces);
        Assert.Contains(import.Warnings,w=>w.Contains("çizgi tipi") && w.Contains("atlandı"));
        Assert.Contains(import.Warnings,w=>w.Contains("özet kısaltıldı"));
        Assert.Contains(import.SkippedSourceRecords!.Records,r=>r.EntityType=="LINE" && r.Reason.Contains("çizgi tipi"));
    }

    [Fact]
    public void CurveAndWidthReasonsRemainVisibleWhenVendorSummaryIsFull()
    {
        var source=new StringBuilder("0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n6\nCONTINUOUS\n10\n0\n20\n0\n11\n1\n21\n0\n");
        for(var i=0;i<70;i++)source.Append("0\nVENDOR_").Append(i).Append("\n8\nMARK\n");
        source.Append("0\nLWPOLYLINE\n8\nCUT\n90\n2\n10\n2\n20\n0\n42\n0.25\n10\n3\n20\n0\n");
        source.Append("0\nLWPOLYLINE\n8\nCUT\n90\n2\n43\n1\n10\n4\n20\n0\n10\n5\n20\n0\n0\nENDSEC\n0\nEOF\n");
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source.ToString()));
        var import=DxfFormat.Import(stream,"geometry warnings");
        Assert.Single(import.Project.Pieces);
        Assert.Contains(import.Warnings,w=>w.Contains("bulge") && w.Contains("atlandı"));
        Assert.Contains(import.Warnings,w=>w.Contains("genişlikli LWPOLYLINE") && w.Contains("atlandı"));
    }
}
