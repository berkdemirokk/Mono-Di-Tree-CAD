using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfCanvasOrientationTests
{
    [Fact]
    public void OpenAndExplicitlyClosedPathsKeepTopologyWhileOnlySelectedModeReflectsY()
    {
        var original=new ProjectDocument { Pieces=[
            new PatternPiece { Name="Closed",Closed=true,Points=[new(1,2),new(11,2),new(4,9),new(1,2)] },
            new PatternPiece { Name="Open",Closed=false,Points=[new(-4,-3),new(5,7),new(-4,-3)] }
        ] };
        var before=original.Clone();
        var direct=Path.Combine(Path.GetTempPath(),$"dxf-direct-{Guid.NewGuid():N}.dxf");
        var flipped=Path.Combine(Path.GetTempPath(),$"dxf-flipped-{Guid.NewGuid():N}.dxf");
        try
        {
            DxfFormat.Export(original,direct,ProjectStorage.CaptureVersion(direct));
            DxfFormat.ExportCanvasOrientation(original,flipped,ProjectStorage.CaptureVersion(flipped));
            var legacy=DxfFormat.Import(direct,1).Project.Pieces;
            var reflected=DxfFormat.Import(flipped,1).Project.Pieces;
            var legacyClosed=legacy.Single(p=>p.Name=="Closed");var newClosed=reflected.Single(p=>p.Name=="Closed");
            var legacyOpen=legacy.Single(p=>p.Name=="Open");var newOpen=reflected.Single(p=>p.Name=="Open");
            Assert.True(legacyClosed.Closed);Assert.True(newClosed.Closed);
            Assert.False(legacyOpen.Closed);Assert.False(newOpen.Closed);
            Assert.Equal(original.Pieces[0].Points.Take(3),legacyClosed.Points);
            Assert.Equal(legacyClosed.Points.Select(p=>new Point2(p.X,-p.Y)),newClosed.Points);
            Assert.Equal(original.Pieces[1].Points,legacyOpen.Points);
            Assert.Equal(legacyOpen.Points.Select(p=>new Point2(p.X,-p.Y)),newOpen.Points);
            Assert.Equal(-Math.Sign(Geometry.SignedArea(legacyClosed.Points)),Math.Sign(Geometry.SignedArea(newClosed.Points)));
            Assert.True(DocumentSession.ContentEquals(before,original));
        }
        finally { File.Delete(direct);File.Delete(flipped); }
    }
    [Fact]
    public void OptionalCanvasOrientationInvertsYOnlyInExportedCopy()
    {
        var path=Path.Combine(Path.GetTempPath(),"tekstil-orientation-"+Guid.NewGuid().ToString("N")+".dxf");
        var original=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(1,2),new(11,2),new(4,9)] }] };
        var before=original.Clone();
        try
        {
            DxfFormat.ExportCanvasOrientation(original,path,ProjectStorage.CaptureVersion(path));
            var actual=DxfFormat.Import(path,1).Project.Pieces.Single();
            Assert.Equal(new[] { new Point2(1,-2),new Point2(11,-2),new Point2(4,-9) },actual.Points);
            Assert.True(DocumentSession.ContentEquals(before,original));
            var version=ProjectStorage.CaptureVersion(path);File.AppendAllText(path,"external");
            Assert.Throws<ProjectConflictException>(()=>DxfFormat.ExportCanvasOrientation(original,path,version));
        }
        finally { File.Delete(path); }
    }
}
