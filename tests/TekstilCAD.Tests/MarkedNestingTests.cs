using TekstilCAD.Core;
using TekstilCAD.Nesting;
using TekstilCAD.FileFormats;
using System.Text;

namespace TekstilCAD.Tests;

public sealed class MarkedNestingTests
{
    [Fact]
    public void QuantityAuditRejectsLostOrChangedExperimentalProvenance()
    {
        var piece=Source();piece.ExperimentalALine=new(ALineExperimentalProvenance.CurrentAlgorithm,new string('A',64),"front");
        var source=new ProjectDocument { Pieces=[piece] };
        var result=ProductionNestingPlan.Arrange(source,[piece.Id],100,5);
        Assert.True(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id],result));
        var copy=result.Nesting.Layout.Pieces[0];
        copy.ExperimentalALine=null;
        Assert.False(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id],result));
        copy.ExperimentalALine=piece.ExperimentalALine with { Side="back" };
        Assert.False(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id],result));
    }

    private static PatternPiece Source()
    {
        var negativeZero=BitConverter.Int64BitsToDouble(long.MinValue);
        return new PatternPiece { Name="Ön",Points=[new(0,0),new(20,0),new(20,10),new(0,10),new(0,0)],
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 },
            Notches=[new NotchMark { VertexIndex=0,Position=new(negativeZero,0) },new NotchMark { VertexIndex=2,Position=new(20,10) }] };
    }

    [Fact]
    public void QuantityLayoutKeepsEachMarkedVertexAndDetectsLoss()
    {
        var piece=Source();var source=new ProjectDocument { Pieces=[piece] };var before=source.Clone();
        var result=ProductionNestingPlan.Arrange(source,[piece.Id],100,5);
        Assert.True(DocumentSession.ContentEquals(before,source));Assert.Equal(2,result.Nesting.Layout.Pieces.Count);
        Assert.True(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id],result));
        ShelfLayoutVerifier.Verify(result.Nesting);
        var json=ProjectStorage.SerializeProject(result.Nesting.Layout);
        using(var parsed=System.Text.Json.JsonDocument.Parse(json))Assert.Equal(6,parsed.RootElement[0].GetInt32());
        var loaded=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(json));
        Assert.True(DocumentSession.ContentEquals(result.Nesting.Layout,loaded));
        foreach(var copy in result.Nesting.Layout.Pieces)
        {
            Assert.Equal(new[]{0,2},copy.Notches!.Select(m=>m.VertexIndex));
            Assert.All(copy.Notches,m=>Assert.Equal(copy.Points[m.VertexIndex],m.Position));
            Assert.NotSame(piece.Notches,copy.Notches);
        }
        var first=result.Nesting.Layout.Pieces[0];
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(first.Notches![0].Position.X));
        var originalMark=first.Notches[0].Position;first.Notches[0].Position=new Point2(3,3);
        Assert.False(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id],result));
        Assert.Throws<InvalidOperationException>(()=>ShelfLayoutVerifier.Verify(result.Nesting));
        first.Notches[0].Position=originalMark;
        first.Notches=null;Assert.False(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id],result));
    }

    [Fact]
    public void MarkerAuditRejectsMissingCopiesEmptyResultsAndWrongSelection()
    {
        var piece=Source();var other=Source();var source=new ProjectDocument { Pieces=[piece,other] };
        var result=ProductionNestingPlan.Arrange(source,[piece.Id],100,5);
        Assert.True(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id],result));
        Assert.False(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id,other.Id],result));
        Assert.False(ProductionNestingPlan.AreMarkersPreserved(source,[],result));
        var one=result.Nesting with { Layout=new ProjectDocument { Pieces=[result.Nesting.Layout.Pieces[0].Clone()] } };
        var incomplete=new ProductionNestingResult(one,[result.Placements[0]]);
        Assert.False(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id],incomplete));
        var empty=result.Nesting with { Layout=new ProjectDocument() };
        Assert.False(ProductionNestingPlan.AreMarkersPreserved(source,[piece.Id],new ProductionNestingResult(empty,[])));
    }

    [Fact]
    public void InvalidMarksRejectWithoutChangingSource()
    {
        var piece=Source();piece.Notches![0].Position=new(3,0);var source=new ProjectDocument { Pieces=[piece] };var before=source.Clone();
        Assert.ThrowsAny<ArgumentException>(()=>NestingEngine.Arrange([piece],100));
        Assert.ThrowsAny<ArgumentException>(()=>ProductionNestingPlan.Arrange(source,[piece.Id],100));
        Assert.True(DocumentSession.ContentEquals(source,before));
    }
}
