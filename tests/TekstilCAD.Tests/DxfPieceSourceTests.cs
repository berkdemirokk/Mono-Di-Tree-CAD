using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfPieceSourceTests
{
    private static DxfImportResult Read(string sections)
    {
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(sections+"0\nEOF\n"));
        return DxfFormat.Import(stream,"source");
    }

    [Fact]
    public void JoinedLinesKeepBothSourceOrdinalsAndPolylineKeepsItsOwn()
    {
        var result=Read("0\nSECTION\n2\nENTITIES\n"+
            "0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n10\n21\n0\n"+
            "0\nLINE\n8\nCUT\n10\n10\n20\n0\n11\n20\n21\n0\n"+
            "0\nLWPOLYLINE\n8\nGRAIN\n90\n2\n10\n0\n20\n4\n10\n5\n20\n4\n0\nENDSEC\n");
        var sources=result.PieceSources!.Value;
        Assert.Equal(result.Project.Pieces.Count,sources.Length);
        var line=Assert.Single(sources,s=>s.EntityType=="LINE");
        Assert.Equal(2,line.SourceOrdinals.Length);
        Assert.Equal(2,line.SourceOrdinals.Distinct().Count());
        Assert.Contains(result.Project.Pieces,p=>p.Id==line.PieceId);
        var poly=Assert.Single(sources,s=>s.EntityType=="LWPOLYLINE");
        Assert.Equal("GRAIN",poly.Layer);
        Assert.Single(poly.SourceOrdinals);
    }

    [Fact]
    public void BlockDefinitionIsNotRepresentedAsPlacedModelGeometry()
    {
        var result=Read("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nB\n"+
            "0\nLINE\n8\nL\n10\n0\n20\n0\n11\n1\n21\n0\n0\nENDBLK\n0\nENDSEC\n");
        var source=Assert.Single(result.PieceSources!.Value);
        Assert.Equal("BLOCKS",source.Section);
        Assert.Equal("B",source.Block);
    }

    [Fact]
    public void ManualResultDoesNotClaimSourceMapping()
        => Assert.Null(new DxfImportResult(new ProjectDocument(),[]).PieceSources);

    [Fact]
    public void LegacyPolylineChildrenLinkToOpaqueMetadataRecords()
    {
        var result=Read("0\nSECTION\n2\nENTITIES\n0\nPOLYLINE\n"+
            "0\nVERTEX\n10\n0\n20\n0\n1001\nAPP\n1000\nnotch\n"+
            "0\nVERTEX\n10\n10\n20\n0\n0\nSEQEND\n102\n{APP\n1\nend\n102\n}\n0\nENDSEC\n");
        var source=Assert.Single(result.PieceSources!.Value);
        Assert.Equal("POLYLINE",source.EntityType);
        Assert.Equal(3,source.ChildSourceOrdinals.Length);
        Assert.All(result.OpaqueMetadata!.Groups,g=>Assert.Contains(g.SourceOrdinal,source.ChildSourceOrdinals));
    }
}
