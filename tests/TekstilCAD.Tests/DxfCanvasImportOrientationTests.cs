using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfCanvasImportOrientationTests
{
    [Fact]
    public void ReflectionPreservesIdentityMetadataTopologyAndSource()
    {
        var source=new ProjectDocument { Pieces=[
            new PatternPiece { Name="Kapalı",Points=[new(1,2),new(11,2),new(4,9),new(1,2)],
                Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 },
                Grainline=new Grainline { Start=new(2,3),End=new(2,7),NeedsReview=true } },
            new PatternPiece { Name="Açık",Closed=false,Points=[new(-3,-4),new(5,6)] }
        ] };
        var before=source.Clone();var result=DxfCanvasImportOrientation.ReflectY(source);
        Assert.True(DocumentSession.ContentEquals(before,source));
        Assert.Equal(source.Pieces.Select(p=>p.Id),result.Pieces.Select(p=>p.Id));
        Assert.True(result.Pieces[0].Closed);Assert.False(result.Pieces[1].Closed);
        Assert.Equal(source.Pieces[0].Points.Select(p=>new Point2(p.X,-p.Y)),result.Pieces[0].Points);
        Assert.Equal(source.Pieces[1].Points.Select(p=>new Point2(p.X,-p.Y)),result.Pieces[1].Points);
        Assert.Equal(new Point2(2,-3),result.Pieces[0].Grainline!.Start);
        Assert.Equal(new Point2(2,-7),result.Pieces[0].Grainline!.End);
        Assert.True(result.Pieces[0].Grainline!.NeedsReview);
        Assert.Equal(2,result.Pieces[0].Metadata!.Quantity);
        Assert.Equal(-Math.Sign(Geometry.SignedArea(source.Pieces[0].Points)),Math.Sign(Geometry.SignedArea(result.Pieces[0].Points)));
    }

    [Fact]
    public void BulgeApproximationIsReflectedAfterSampling()
    {
        const string dxf="0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n90\n2\n70\n0\n10\n0\n20\n0\n42\n1\n10\n2\n20\n0\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        var imported=DxfFormat.ImportWithBulgeApproximation(stream,"yay",1,DxfTextEncoding.Utf8,0.1);
        var piece=Assert.Single(imported.Project.Pieces);Assert.True(piece.Points.Count>2);
        var reflected=Assert.Single(DxfCanvasImportOrientation.ReflectY(imported.Project).Pieces);
        Assert.Equal(piece.Points.Select(p=>new Point2(p.X,-p.Y)),reflected.Points);
    }

    [Fact]
    public void ControlledInsertIsReflectedAfterPlacement()
    {
        const string dxf="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
            "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n10\n0\n20\n0\n0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n0\nENDBLK\n0\nENDSEC\n"+
            "0\nSECTION\n2\nENTITIES\n0\nINSERT\n2\nA\n10\n10\n20\n20\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(dxf));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var imported=DxfFormat.ImportWithInspectedBlockPlacements(stream,"blok",1,0,inspection.Layers.Select(l=>l.Name).ToArray(),inspection);
        var piece=Assert.Single(imported.Project.Pieces);
        var reflected=Assert.Single(DxfCanvasImportOrientation.ReflectY(imported.Project).Pieces);
        Assert.Equal(piece.Points.Select(p=>new Point2(p.X,-p.Y)),reflected.Points);
    }
}
