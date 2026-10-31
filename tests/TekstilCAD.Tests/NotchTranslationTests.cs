using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class NotchTranslationTests
{
    private static PatternPiece Source()=>new() { Points=[new(0,0),new(10,0),new(10,10),new(0,10)],
        Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) },new NotchMark { VertexIndex=3,Position=new(0,10) }],
        Grainline=new Grainline { Start=new(2,2),End=new(2,8),NeedsReview=true },Metadata=new PieceMetadata { Quantity=2 } };
    [Fact]
    public void IntegerTranslationPreservesMarksAndAllSourceInformation()
    {
        var p=Source();var before=p.Clone();var moved=PieceTranslation.Translate(p,5,-2);
        Assert.Equal(p.Id,moved.Id);Assert.Equal(new Point2(15,-2),moved.Notches![0].Position);Assert.Equal(new Point2(5,8),moved.Notches[1].Position);
        Assert.Equal(new[] { 1,3 },moved.Notches.Select(n=>n.VertexIndex));Assert.Equal(new Point2(7,0),moved.Grainline!.Start);
        Assert.True(moved.Grainline.NeedsReview);Assert.Equal(2,moved.Metadata!.Quantity);Assert.NotSame(p.Notches,moved.Notches);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[p] }));
        ProjectStorage.Validate(new ProjectDocument { Pieces=[moved] });
    }
    [Fact]
    public void ExplicitRepeatedClosingVertexRemainsIntact()
    {
        var p=Source();p.Points.Add(p.Points[0]);var moved=PieceTranslation.Translate(p,1,1);
        Assert.Equal(moved.Points[0],moved.Points[^1]);Assert.Equal(moved.Points[1],moved.Notches![0].Position);
    }
    [Fact]
    public void CommonDecimalTranslationMatchesExistingGeometryAndRetainsMarks()
    {
        var p=Source();var before=p.Clone();
        var expected=Geometry.Translate(p.Points,.1,0);var moved=PieceTranslation.Translate(p,.1,0);
        Assert.Equal(expected,moved.Points);Assert.Equal(moved.Points[1],moved.Notches![0].Position);
        Assert.Equal(moved.Points[3],moved.Notches[1].Position);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[p] }));
    }
    [Fact]
    public void RejectsStaleMarksMalformedGeometryAndCancellation()
    {
        var p=Source();p.Notches![0].Position=new(19,0);
        Assert.Throws<ArgumentException>(()=>PieceTranslation.Translate(p,1,0));
        p=Source();p.Points[2]=new(double.NaN,5);
        Assert.Throws<ArgumentOutOfRangeException>(()=>PieceTranslation.Translate(p,1,0));
        p=Source();using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>PieceTranslation.Translate(p,1,0,c.Token));
    }
    [Theory]
    [InlineData(0.1,0.1)] [InlineData(1,1)] [InlineData(100000000,0)] [InlineData(double.NaN,0)]
    public void UnmarkedAndMarkedGeometryHaveTheSameTranslationAcceptance(double dx,double dy)
    {
        var marked=Source();marked.Grainline=null;
        var acceptedGeometry=Record.Exception(()=>Geometry.Translate(marked.Points,dx,dy));
        var acceptedPiece=Record.Exception(()=>PieceTranslation.Translate(marked,dx,dy));
        Assert.Equal(acceptedGeometry is null,acceptedPiece is null);
        if(acceptedGeometry is null)
        {
            var moved=PieceTranslation.Translate(marked,dx,dy);
            Assert.Equal(Geometry.Translate(marked.Points,dx,dy),moved.Points);
            foreach(var notch in moved.Notches!)Assert.Equal(moved.Points[notch.VertexIndex],notch.Position);
        }
    }
    [Fact]
    public void TranslationCanBeCommittedAndUndoneWithMarks()
    {
        var p=Source();var session=new DocumentSession(new ProjectDocument { Pieces=[p] });var before=session.Document.Clone();
        var moved=PieceTranslation.Translate(session.Document.Pieces[0],5,0);
        Assert.True(session.Execute(d=>d.Pieces[0]=moved,ProjectStorage.Validate));
        Assert.True(session.Undo());Assert.True(DocumentSession.ContentEquals(before,session.Document));
        Assert.True(session.Redo());Assert.Equal(new Point2(15,0),session.Document.Pieces[0].Notches![0].Position);
    }
}
