using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;
public class ContourPathExtractionTests
{
    private static PatternPiece Source(bool repeated) {
        var p=new PatternPiece { Points=[new(0,0),new(10,0),new(10,10),new(0,10)],
            Metadata=new PieceMetadata { Quantity=2 },Grainline=new Grainline { Start=new(2,2),End=new(2,8) },
            Notches=[new NotchMark { VertexIndex=0,Position=new(0,0) },new NotchMark { VertexIndex=2,Position=new(10,10) }] };
        if(repeated)p.Points.Add(p.Points[0]);return p;
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WrapPathPreservesOnlyIncludedMarksAndDoesNotMutateSource(bool repeated)
    {
        var source=Source(repeated);var before=source.Clone();var result=ContourPathExtraction.Extract(source,3,1);
        Assert.True(result.CrossesClosure);Assert.Equal(new[] { new Point2(0,10),new Point2(0,0),new Point2(10,0) },result.Piece.Points);
        Assert.Equal(1,result.Piece.Notches!.Single().VertexIndex);Assert.Equal(1,result.OmittedMarks);Assert.False(result.Piece.Closed);
        Assert.Null(result.Piece.Metadata);Assert.Null(result.Piece.Grainline);Assert.NotSame(source.Notches![0],result.Piece.Notches[0]);
        result.Piece.Notches[0].Position=new(99,99);Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[source] },new ProjectDocument { Pieces=[before] }));
    }
    [Fact]
    public void NormalizesClosureAliasAndRejectsAmbiguousOrBitDifferentClosure()
    {
        var source=Source(true);var result=ContourPathExtraction.Extract(source,4,2);
        Assert.Equal(0,result.StartIndex);Assert.Equal(2,result.EndIndex);Assert.False(result.CrossesClosure);
        Assert.Equal(new[] { 0,2 },result.Piece.Notches!.Select(mark=>mark.VertexIndex));
        Assert.Throws<ArgumentException>(()=>ContourPathExtraction.Extract(source,0,4));
        source.Points[^1]=new Point2(BitConverter.Int64BitsToDouble(long.MinValue),0);
        Assert.Throws<ArgumentException>(()=>ContourPathExtraction.Extract(source,0,2));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ContourPathExtraction.Extract(source,0,2,cancelled.Token));
    }
    [Fact]
    public void RejectsInvalidContoursIndicesAndUnmatchedMarkers()
    {
        var source=Source(false);Assert.Throws<ArgumentOutOfRangeException>(()=>ContourPathExtraction.Extract(source,-1,2));
        source.Notches![0].Position=new Point2(1,0);Assert.Throws<ArgumentException>(()=>ContourPathExtraction.Extract(source,0,2));
        source=Source(false);source.Points=[new(0,0),new(10,10),new(0,10),new(10,0)];source.Notches=null;
        Assert.Throws<ArgumentException>(()=>ContourPathExtraction.Extract(source,0,2));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExplicitRuleMappingPreservesIndexedIncrementsAcrossClosure(bool repeated)
    {
        var source=Source(repeated);
        var deltas=new List<Point2> { new(0,1),new(1,2),new(2,3),new(3,4) };
        if(repeated)deltas.Add(deltas[0]);
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],Deltas=deltas };
        var result=ContourPathExtraction.Extract(source,3,1);
        var mapped=ContourPathExtraction.ExtractGrading(source,rule,result)!;
        Assert.Equal(result.Piece.Id,mapped.SourcePieceId);Assert.False(mapped.SourceClosed);
        Assert.Equal(new[] { deltas[3],deltas[0],deltas[1] },mapped.Deltas);
        Assert.Equal(result.Piece.Points,mapped.SourcePoints);
        mapped.Deltas[0]=new(99,99);Assert.Equal(new Point2(3,4),rule.Deltas[3]);
    }
    [Fact]
    public void RuleMappingUsesCanonicalClosureAliasAndForwardIndices()
    {
        var source=Source(true);
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],Deltas=[new(0,1),new(1,2),new(2,3),new(3,4),new(0,1)] };
        var result=ContourPathExtraction.Extract(source,4,2);
        var mapped=ContourPathExtraction.ExtractGrading(source,rule,result)!;
        Assert.Equal(new[] { rule.Deltas[0],rule.Deltas[1],rule.Deltas[2] },mapped.Deltas);
    }
    [Fact]
    public void RuleMappingRejectsStaleBitsConflictingClosureAndAlteredResult()
    {
        var source=Source(true);
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],Deltas=Enumerable.Repeat(new Point2(0,0),source.Points.Count).ToList() };
        var result=ContourPathExtraction.Extract(source,3,1);
        rule.Deltas[^1]=new Point2(-0.0,0);Assert.Null(ContourPathExtraction.ExtractGrading(source,rule,result));
        rule.Deltas[^1]=rule.Deltas[0];rule.SourcePoints[0]=new Point2(-0.0,0);Assert.Null(ContourPathExtraction.ExtractGrading(source,rule,result));
        rule.SourcePoints[0]=source.Points[0];result.Piece.Points[0]=new(1,1);Assert.Null(ContourPathExtraction.ExtractGrading(source,rule,result));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ContourPathExtraction.ExtractGrading(source,rule,result,cancellation.Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReversingChosenIntervalPreservesMarksAndReversesSavedRule(bool repeated)
    {
        var source=Source(repeated);var before=source.Clone();
        var deltas=new List<Point2> { new(0,1),new(1,2),new(2,3),new(3,4) };if(repeated)deltas.Add(deltas[0]);
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],Deltas=deltas };
        var result=ContourPathExtraction.Extract(source,0,2,true);
        Assert.True(result.Reversed);Assert.False(result.CrossesClosure);
        Assert.Equal(new[] { source.Points[2],source.Points[1],source.Points[0] },result.Piece.Points);
        Assert.Equal(2,result.Piece.Notches!.Single(mark=>mark.Position==source.Points[0]).VertexIndex);
        Assert.Equal(0,result.Piece.Notches.Single(mark=>mark.Position==source.Points[2]).VertexIndex);
        var mapped=ContourPathExtraction.ExtractGrading(source,rule,result)!;
        Assert.Equal(new[] { deltas[2],deltas[1],deltas[0] },mapped.Deltas);
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces=[source] },new ProjectDocument { Pieces=[before] }));
        result.Piece.Points[0]=new(55,55);Assert.Null(ContourPathExtraction.ExtractGrading(source,rule,result));
    }
    [Fact]
    public void ReversedWrappingIntervalRetainsCanonicalClosureOrder()
    {
        var source=Source(true);var result=ContourPathExtraction.Extract(source,3,1,true);
        Assert.True(result.Reversed && result.CrossesClosure);
        Assert.Equal(new[] { source.Points[1],source.Points[0],source.Points[3] },result.Piece.Points);
        Assert.Equal(1,result.Piece.Notches!.Single().VertexIndex);
    }

}
