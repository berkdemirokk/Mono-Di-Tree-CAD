using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class NotchGradingTests
{
    private static PatternPiece Source()=>new() { Name="Ön",Closed=true,
        Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)],
        Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) },new NotchMark { VertexIndex=0,Position=new(0,0) }] };
    private static Point2[] Increments()=>[new(0,0),new(1,0),new(1,0),new(0,0),new(0,0)];
    [Fact]
    public void EachMarkerFollowsItsGradedVertexAndIsIndependent()
    {
        var p=Source();var before=p.Clone();var generated=GradingEngine.Generate(p,Increments(),2,"L");
        Assert.NotEqual(p.Id,generated.Id);Assert.Equal(new Point2(12,0),generated.Notches![0].Position);
        Assert.Equal(new[] { 1,0 },generated.Notches.Select(n=>n.VertexIndex));
        Assert.All(generated.Notches,n=>Assert.Equal(generated.Points[n.VertexIndex],n.Position));
        Assert.Equal(generated.Points[0],generated.Points[^1]);Assert.NotSame(p.Notches,generated.Notches);
        Assert.NotSame(p.Notches![0],generated.Notches[0]);generated.Notches[0].VertexIndex=2;
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[p] }));
    }
    [Fact]
    public void BatchUsesEachSizesOwnCoordinatesWithoutSharingMarkers()
    {
        var p=Source();var sizes=GradingEngine.GenerateSizes(p,Increments(),[new GradeSize("S",-1),new GradeSize("L",2)]);
        Assert.Equal(new Point2(9,0),sizes[0].Notches![0].Position);Assert.Equal(new Point2(12,0),sizes[1].Notches![0].Position);
        Assert.NotSame(sizes[0].Notches,sizes[1].Notches);Assert.NotSame(sizes[0].Notches![0],sizes[1].Notches![0]);
    }
    [Theory]
    [InlineData("stale")] [InlineData("duplicate")] [InlineData("alias")] [InlineData("missing")]
    public void RejectsInvalidOrAmbiguousSourceMarkers(string kind)
    {
        var p=Source();
        switch(kind)
        {
            case "stale":p.Notches![0].Position=new(9,0);break;
            case "duplicate":p.Notches!.Add(p.Notches[0].Clone());break;
            case "alias":p.Notches=[new NotchMark { VertexIndex=4,Position=p.Points[4] }];break;
            case "missing":p.Notches=[];break;
        }
        Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(p,Increments(),1,"L"));
        Assert.Throws<ArgumentException>(()=>GradingEngine.GenerateSizes(p,Increments(),Array.Empty<GradeSize>()));
    }
    [Fact]
    public void DifferingClosingDeltasAndCollapsedResultAreRejected()
    {
        var p=Source();var rules=Increments();rules[^1]=new(1,0);
        Assert.Throws<ArgumentException>(()=>GradingEngine.Generate(p,rules,1,"L"));
        rules=Increments();rules[1]=new(-10,0);rules[2]=new(-10,0);
        Assert.Throws<InvalidDataException>(()=>GradingEngine.Generate(p,rules,1,"L"));
        Assert.Equal(new Point2(10,0),p.Notches![0].Position);
    }
    [Fact]
    public void GeneratedMarksPersistAndUndoWithGeneratedSize()
    {
        var p=Source();var session=new DocumentSession(new ProjectDocument { Pieces=[p] });var before=session.Document.Clone();
        var size=GradingEngine.Generate(p,Increments(),2,"L");
        Assert.True(session.Execute(d=>d.Pieces.Add(size),ProjectStorage.Validate));
        var json=ProjectStorage.SerializeProject(session.Document);var restored=ProjectStorage.DeserializeProject(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.True(DocumentSession.ContentEquals(session.Document,restored));
        Assert.True(session.Undo());Assert.True(DocumentSession.ContentEquals(before,session.Document));
        Assert.True(session.Redo());Assert.Equal(new Point2(12,0),session.Document.Pieces[1].Notches![0].Position);
    }
    [Fact]
    public void CancellationPreservesSource()
    {
        var p=Source();using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>GradingEngine.Generate(p,Increments(),1,"L",c.Token));
        Assert.Equal(new Point2(10,0),p.Notches![0].Position);
    }
}
