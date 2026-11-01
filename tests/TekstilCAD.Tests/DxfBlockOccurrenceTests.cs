using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfBlockOccurrenceTests
{
    [Theory]
    [InlineData("0\nSECTION\n2\nENTITIES\n0\nBLOCK\n2\nA\n0\nENDBLK\n0\nENDSEC\n0\nEOF\n")]
    [InlineData("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n")]
    [InlineData("0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n0\nENDSEC\n0\nEOF\n")]
    [InlineData("0\nSECTION\n2\nBLOCKS\n0\nPOINT\n10\n0\n20\n0\n0\nENDSEC\n0\nEOF\n")]
    public void MalformedBlockBoundariesCannotProduceSourceEvidence(string source)
    {
        var bytes=Encoding.ASCII.GetBytes(source);var original=bytes.ToArray();
        Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(new MemoryStream(bytes),"test"));
        Assert.Equal(original,bytes);
    }

    [Fact]
    public void BlockOccurrenceCannotBeSetThroughPublicReportApi()
    {
        foreach(var type in new[]{typeof(DxfPieceSource),typeof(DxfSourceGeometryRecord),typeof(DxfSupplementalGeometryRecord)})
            Assert.Null(type.GetProperty("BlockHeaderOrdinal"));
    }

    [Fact]
    public void DuplicateBlockNamesKeepDistinctSourceOccurrencesAndChildren()
    {
        const string block="0\nBLOCK\n2\nA\n0\nPOLYLINE\n8\nA\n70\n1\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n10\n20\n0\n0\nVERTEX\n10\n0\n20\n10\n0\nSEQEND\n0\nPOINT\n8\nA\n10\n0\n20\n0\n0\nENDBLK\n";
        var bytes=Encoding.ASCII.GetBytes("0\nSECTION\n2\nBLOCKS\n"+block+block+"0\nENDSEC\n0\nEOF\n");
        var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var pieces=import.PieceSources!.Value.Where(p=>p.EntityType=="POLYLINE").ToArray();
        var points=import.SupplementalGeometry!.Records.Where(p=>p.EntityType=="POINT").ToArray();
        Assert.Equal(2,pieces.Length);Assert.Equal(2,points.Length);
        Assert.NotNull(pieces[0].BlockHeaderOrdinal);Assert.NotEqual(pieces[0].BlockHeaderOrdinal,pieces[1].BlockHeaderOrdinal);
        for(int i=0;i<2;i++)
        {
            Assert.Equal(pieces[i].BlockHeaderOrdinal,points[i].BlockHeaderOrdinal);
            var source=Assert.Single(import.SourceGeometry!.Records.Where(r=>r.SourceOrdinal==pieces[i].SourceOrdinals[0]));
            Assert.Equal(source.BlockHeaderOrdinal,pieces[i].BlockHeaderOrdinal);
            Assert.Equal(4,pieces[i].ChildSourceOrdinals.Length);
        }
        Assert.True(import.ImportedBlockDefinitions);
        Assert.Throws<InvalidDataException>(()=>DxfTextileGrainlineMapping.CaptureFingerprint(import));
    }
}
