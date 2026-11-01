using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfPointVertexMappingTests
{
    private static byte[] Fixture(string point="0", string extra="") => Encoding.ASCII.GetBytes(
        "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n8\nA\n90\n4\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n10\n20\n10\n10\n0\n20\n10\n"+
        "0\nPOINT\n8\nA\n10\n"+point+"\n20\n0\n"+extra+"0\nENDSEC\n0\nEOF\n");

    [Fact]
    public void ExplicitMatchingCreatesDetachedVisualMarkerOnly()
    {
        var bytes=Fixture();var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var source=Assert.Single(import.Project.Pieces);var point=Assert.Single(import.SupplementalGeometry!.Records.Where(r=>r.EntityType=="POINT"));
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var result=DxfPointVertexMapping.Apply(import,new MemoryStream(bytes),[new(point.SourceOrdinal,source.Id,0)],fingerprint);
        var mapped=Assert.Single(result.Project.Pieces);var marker=Assert.Single(mapped.Notches!);
        Assert.Equal(new Point2(0,0),marker.Position);Assert.Equal(0,marker.VertexIndex);
        Assert.Null(source.Notches);Assert.Single(result.Assignments);
    }

    [Fact]
    public void DecimalCoordinateUsesImportedScaleAndOptionalReflection()
    {
        var text=Encoding.ASCII.GetString(Fixture()).Replace("10\n0\n20\n0\n10\n10", "10\n4662.231\n20\n1\n10\n10");
        text=text.Replace("0\nPOINT\n8\nA\n10\n0\n20\n0", "0\nPOINT\n8\nA\n10\n4662.2310\n20\n1");
        var bytes=Encoding.ASCII.GetBytes(text);var import=DxfFormat.Import(new MemoryStream(bytes),"test",10);
        var piece=Assert.Single(import.Project.Pieces);var point=Assert.Single(import.SupplementalGeometry!.Records.Where(r=>r.EntityType=="POINT"));
        var result=DxfPointVertexMapping.Apply(import,new MemoryStream(bytes),[new(point.SourceOrdinal,piece.Id,0)],DxfTextileGrainlineMapping.CaptureFingerprint(import),true);
        var position=Assert.Single(Assert.Single(result.Project.Pieces).Notches!).Position;
        Assert.Equal(4662.231*10,position.X);Assert.Equal(-10,position.Y);
        Assert.Null(piece.Notches);
    }

    [Fact]
    public void ChangedSourceAmbiguousVertexAndUnsupportedPlaneAreRejected()
    {
        var bytes=Fixture();var import=DxfFormat.Import(new MemoryStream(bytes),"test");
        var source=Assert.Single(import.Project.Pieces);var point=Assert.Single(import.SupplementalGeometry!.Records.Where(r=>r.EntityType=="POINT"));
        var fingerprint=DxfTextileGrainlineMapping.CaptureFingerprint(import);var selection=new DxfPointVertexSelection(point.SourceOrdinal,source.Id,0);
        Assert.Throws<InvalidDataException>(()=>DxfPointVertexMapping.Apply(import,new MemoryStream(Fixture("1")),[selection],fingerprint));
        Assert.Throws<InvalidDataException>(()=>DxfPointVertexMapping.Apply(import,new MemoryStream(bytes),[selection,selection],fingerprint));
        var bad=Fixture("0","30\n1\n");var other=DxfFormat.Import(new MemoryStream(bad),"test");
        var otherPoint=Assert.Single(other.SupplementalGeometry!.Records.Where(r=>r.EntityType=="POINT"));
        var otherPiece=Assert.Single(other.Project.Pieces);
        Assert.Throws<InvalidDataException>(()=>DxfPointVertexMapping.Apply(other,new MemoryStream(bad),[new(otherPoint.SourceOrdinal,otherPiece.Id,0)],DxfTextileGrainlineMapping.CaptureFingerprint(other)));
        Assert.Null(source.Notches);
    }
}
