using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class DxfClosureNotchTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NotchedClosedContourRoundTripsWithoutDuplicateClosingVertex(bool explicitClosure)
    {
        var points = new List<Point2> { new(0,0),new(20,0),new(20,10),new(0,10) };
        if (explicitClosure) points.Add(points[0]);
        var piece = PatternTools.InsertNotch(new PatternPiece { Points=points },0,.5,2);
        var document = new ProjectDocument { Pieces=[piece] }; var before = document.Clone();
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dxf");
        try
        {
            DxfFormat.Export(document,path);
            var imported = DxfFormat.Import(path,1).Project.Pieces.Single();
            Assert.True(imported.Closed);
            Assert.Equal(explicitClosure ? piece.Points.Take(piece.Points.Count-1) : piece.Points, imported.Points);
            Assert.NotEqual(imported.Points[0],imported.Points[^1]);
            Assert.True(DocumentSession.ContentEquals(before,document));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OpenPathWithEqualEndpointsKeepsBothEndpoints()
    {
        var piece = new PatternPiece { Closed=false,Points=[new(0,0),new(5,0),new(0,0)] };
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dxf");
        try { DxfFormat.Export(new ProjectDocument { Pieces=[piece] },path); var result=DxfFormat.Import(path,1).Project.Pieces.Single(); Assert.False(result.Closed); Assert.Equal(piece.Points,result.Points); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DoubleClosureAndTooFewClosedVerticesAreRejectedBeforeWriting()
    {
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dxf");
        try
        {
            File.WriteAllText(path,"original");
            foreach (var points in new List<Point2>[] { [new(0,0),new(1,0),new(0,1),new(0,0),new(0,0)], [new(0,0),new(1,0),new(0,0)] })
                Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(new ProjectDocument { Pieces=[new PatternPiece { Points=points }] },path));
            Assert.Equal("original",File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
