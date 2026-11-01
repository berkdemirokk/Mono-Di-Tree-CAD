using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class MarkedPieceTranslationTests
{
    private static PatternPiece Marked() => new()
    {
        Points=[new(0,0),new(20,0),new(20,20),new(0,20)],
        Notches=[new NotchMark { VertexIndex=1,Position=new(20,0) },new NotchMark { VertexIndex=3,Position=new(0,20) }]
    };
    [Fact]
    public void TranslationReturnsDetachedMarksAtCorrespondingMovedVertices()
    {
        var source=Marked();var original=source.Clone();
        var (points,marks)=MarkedPieceTranslation.Translate(source,3,-4);
        Assert.Equal(new Point2(23,-4),points[1]);Assert.Equal(new Point2(3,16),points[3]);
        Assert.Equal(points[1],marks![0].Position);Assert.Equal(points[3],marks[1].Position);
        Assert.Equal(new[] { 1,3 },marks.Select(m=>m.VertexIndex));
        marks[0].Position=new Point2(0,0);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[original] },new ProjectDocument { Pieces=[source] }));
    }
    [Theory]
    [InlineData(0.00000001, 0)]
    [InlineData(-0.00000001, 0)]
    [InlineData(0, 0.00000001)]
    public void RejectsMarkedMovementWhoseRepresentedDisplacementDiffersMaterially(double dx,double dy)
    {
        var source=Marked();source.Points=source.Points.Select(p=>new Point2(p.X+50_000_000,p.Y+50_000_000)).ToList();
        foreach(var mark in source.Notches!)mark.Position=source.Points[mark.VertexIndex];
        var original=source.Clone();
        Assert.ThrowsAny<ArgumentException>(()=>MarkedPieceTranslation.Translate(source,dx,dy));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[original] },new ProjectDocument { Pieces=[source] }));
    }
    [Fact]
    public void OrdinaryFractionalDisplacementsRemainUsable()
    {
        var source=Marked();var (points,marks)=MarkedPieceTranslation.Translate(source,.1,-.1);
        Assert.Equal(points[1],marks![0].Position);
        Assert.Equal(points[3],marks[1].Position);
    }
    [Fact]
    public void InvalidOrDuplicateAnchorsAreRejectedWithoutMutation()
    {
        var source=Marked();source.Notches![0].Position=new Point2(19,0);var original=source.Clone();
        Assert.ThrowsAny<ArgumentException>(()=>MarkedPieceTranslation.Translate(source,1,0));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[original] },new ProjectDocument { Pieces=[source] }));
        source=Marked();source.Notches!.Add(source.Notches[0].Clone());
        Assert.ThrowsAny<ArgumentException>(()=>MarkedPieceTranslation.Translate(source,1,0));
    }
    [Fact]
    public void CancellationAndUnrepresentableTranslationDoNotChangeTheSource()
    {
        var source=Marked();var original=source.Clone();using var canceled=new CancellationTokenSource();canceled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>MarkedPieceTranslation.Translate(source,1,0,canceled.Token));
        Assert.ThrowsAny<ArgumentException>(()=>MarkedPieceTranslation.Translate(source,1e12,0));
        Assert.ThrowsAny<ArgumentException>(()=>MarkedPieceTranslation.Translate(source,double.Epsilon,0));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[original] },new ProjectDocument { Pieces=[source] }));
        var tiny=new PatternPiece { Closed=false,Points=[new(0,0),new(1e-12,0)],Notches=[new NotchMark { VertexIndex=1,Position=new(1e-12,0) }] };
        Assert.ThrowsAny<ArgumentException>(()=>MarkedPieceTranslation.Translate(tiny,1e8,0));
        Assert.Equal(new Point2(1e-12,0),tiny.Notches![0].Position);
    }
}
