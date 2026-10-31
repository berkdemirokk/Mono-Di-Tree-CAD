using TekstilCAD.Core;
using TekstilCAD.Grading;
using TekstilCAD.Nesting;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class NotchOperationGuardTests
{
    private static PatternPiece Closed()=>new() { Points=[new(0,0),new(20,0),new(20,20),new(0,20)],
        Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=1 },
        Notches=[new NotchMark { VertexIndex=1,Position=new(20,0) }] };
    private static PatternPiece Open()=>new() { Closed=false,Points=[new(0,0),new(10,0),new(10,10)],
        Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }] };
    [Fact]
    public void AllDerivedPatternEditsRejectWithoutChangingSource()
    {
        var p=Closed();var before=p.Clone();
        Action[] operations=[()=>PatternTools.Offset(p,1),()=>PatternTools.OffsetConvex(p,1),()=>PatternTools.InsertPoint(p,0,new(10,0)),
            ()=>PatternTools.InsertEdgeMidpoint(p,0),()=>PatternTools.DeletePoint(p,0),()=>PatternTools.MovePoint(p,0,new(1,1))];
        foreach(var action in operations) { var ex=Assert.Throws<ArgumentException>(action);Assert.Contains("Çentik",ex.Message); }
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[before] },new ProjectDocument { Pieces=[p] }));
    }
    [Fact]
    public void OpenPathOperationsRejectMarksFromEitherSource()
    {
        var marked=Open();var other=new PatternPiece { Closed=false,Points=[new(10,10),new(15,10)] };
        Assert.Throws<ArgumentException>(()=>PatternTools.ReverseOpenPath(marked));
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(marked,20));
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.Join(marked,other,0));
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.Join(other,marked,0));
        Assert.Single(marked.Notches!);
    }
    [Fact]
    public void NestingRejectsInvalidMarkerBeforeCreatingResults()
    {
        var p=Closed();p.Notches![0].Position=new Point2(19,0);var original=p.Clone();
        Assert.Throws<ArgumentException>(()=>OrthogonalRotation.Rotate(p,90));
        Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([p],100));
        Assert.Throws<ArgumentException>(()=>ProductionNestingPlan.Arrange(new ProjectDocument { Pieces=[p] },[p.Id],100));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[original] },new ProjectDocument { Pieces=[p] }));
    }
    [Fact]
    public void AddingOrChangingNotchMakesPreviousLayoutFingerprintStale()
    {
        var p=Closed();p.Notches=null;var d=new ProjectDocument { Pieces=[p] };
        var prior=NestingSnapshot.ComputeLayoutFingerprint(d);
        p.Notches=[new NotchMark { VertexIndex=1,Position=p.Points[1] }];
        var withMark=NestingSnapshot.ComputeLayoutFingerprint(d);Assert.NotEqual(prior,withMark);
        p.Notches[0].VertexIndex=2;p.Notches[0].Position=p.Points[2];
        Assert.NotEqual(withMark,NestingSnapshot.ComputeLayoutFingerprint(d));
        p.Notches=null;Assert.Equal(prior,NestingSnapshot.ComputeLayoutFingerprint(d));
    }
}
