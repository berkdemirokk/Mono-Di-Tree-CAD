using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfCanvasOrientationTests
{
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
