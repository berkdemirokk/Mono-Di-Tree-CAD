using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfStrictPieceSourceTests
{
    [Fact]
    public void NestedInsertRetainsActualPathAndLegacyPolylineOrdinal()
    {
        var source="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
            "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n10\n0\n20\n0\n"+
            "0\nINSERT\n2\nB\n10\n0\n20\n0\n0\nENDBLK\n"+
            "0\nBLOCK\n2\nB\n10\n0\n20\n0\n0\nPOLYLINE\n8\n0\n"+
            "0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n1\n20\n0\n0\nSEQEND\n0\nENDBLK\n0\nENDSEC\n"+
            "0\nSECTION\n2\nENTITIES\n0\nINSERT\n2\nA\n10\n0\n20\n0\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var result=DxfFormat.ImportWithBlockPlacements(stream,"nested",1,DxfTextEncoding.Utf8,0,["0"]);
        var origin=Assert.Single(result.PieceSources!.Value);
        Assert.Equal("POLYLINE",origin.EntityType);
        Assert.Equal(new[] { 10 },origin.SourceOrdinals.ToArray());
        Assert.Equal(new[] { 11, 12, 13 },origin.ChildSourceOrdinals.ToArray());
        Assert.Equal(new[] { "A", "B" },origin.InsertPath.Select(p=>p.BlockName));
        Assert.Equal(new[] { 17, 7 },origin.InsertPath.Select(p=>p.SourceOrdinal));
    }

    [Fact]
    public void DirectLineChainAndRepeatedPlacementsKeepDistinctRealSources()
    {
        var source="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
            "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nB\n10\n0\n20\n0\n30\n0\n"+
            "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n2\n0\nENDBLK\n0\nENDSEC\n"+
            "0\nSECTION\n2\nENTITIES\n"+
            "0\nINSERT\n2\nB\n10\n10\n20\n10\n0\nINSERT\n2\nB\n10\n10\n20\n10\n"+
            "0\nLINE\n10\n30\n20\n0\n11\n31\n21\n0\n0\nLINE\n10\n31\n20\n0\n11\n32\n21\n0\n"+
            "0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var result=DxfFormat.ImportWithBlockPlacements(stream,"strict",1,DxfTextEncoding.Utf8,0,["0"]);
        var sources=result.PieceSources!.Value;
        Assert.Equal(3,sources.Length);
        var direct=Assert.Single(sources,s=>s.EntityType=="LINE");
        Assert.Equal("ENTITIES",direct.Section);
        Assert.Empty(direct.InsertPath);
        Assert.Equal(2,direct.SourceOrdinals.Distinct().Count());
        var placed=sources.Where(s=>s.EntityType=="LWPOLYLINE").ToArray();
        Assert.Equal(2,placed.Length);
        Assert.All(placed,s=> { Assert.Equal("BLOCKS",s.Section); Assert.Equal("B",s.Block); Assert.Single(s.InsertPath); });
        Assert.Equal(placed[0].SourceOrdinals[0],placed[1].SourceOrdinals[0]);
        Assert.NotEqual(placed[0].InsertPath[0].SourceOrdinal,placed[1].InsertPath[0].SourceOrdinal);
        Assert.All(sources,s=>Assert.Contains(result.Project.Pieces,p=>p.Id==s.PieceId));
    }
}
