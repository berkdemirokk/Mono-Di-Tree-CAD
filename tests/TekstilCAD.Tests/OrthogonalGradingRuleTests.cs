using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public class OrthogonalGradingRuleTests
{
    [Theory]
    [InlineData(OrthogonalGradingTransform.RotateLeft)]
    [InlineData(OrthogonalGradingTransform.RotateRight)]
    [InlineData(OrthogonalGradingTransform.MirrorVertical)]
    [InlineData(OrthogonalGradingTransform.MirrorHorizontal)]
    public void GradingThenTransformMatchesTransformThenGrading(OrthogonalGradingTransform operation)
    {
        var source=new PatternPiece { Points=[new(0,0),new(20,0),new(0,10)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],Deltas=[new(0,0),new(2,0),new(0,2)] };
        var center=Geometry.Bounds(source.Points).Center;
        List<Point2> Apply(IReadOnlyList<Point2> points)=>operation switch {
            OrthogonalGradingTransform.RotateLeft=>Geometry.Rotate(points,90,center),
            OrthogonalGradingTransform.RotateRight=>Geometry.Rotate(points,-90,center),
            OrthogonalGradingTransform.MirrorVertical=>Geometry.Mirror(points,true,center.X),
            _=>Geometry.Mirror(points,false,center.Y)
        };
        var transformed=Apply(source.Points);var mapped=rule.OrthogonalFor(source,transformed,operation)!;
        var piece=source.Clone();piece.Points=transformed;
        var expected=Apply(GradingEngine.Generate(source,rule.Deltas,1,"L").Points);
        Assert.Equal(expected,GradingEngine.Generate(piece,mapped.Deltas,1,"L").Points);
        Assert.Equal(source.Points,rule.SourcePoints);
        Assert.Equal(new Point2(2,0),rule.Deltas[1]);
    }

    [Fact]
    public void StaleRuleAndInexactGeometryCannotBeReassigned()
    {
        var source=new PatternPiece { Points=[new(0,0),new(20,0),new(0,10)] };
        var rule=new GradingRuleSet { SourcePieceId=source.Id,SourceClosed=true,SourcePoints=[..source.Points],Deltas=[new(0,0),new(2,0),new(0,2)] };
        var result=Geometry.Rotate(source.Points,90,Geometry.Bounds(source.Points).Center);
        Assert.NotNull(rule.OrthogonalFor(source,result,OrthogonalGradingTransform.RotateLeft));
        result[1]=new Point2(double.BitIncrement(result[1].X),result[1].Y);
        Assert.Null(rule.OrthogonalFor(source,result,OrthogonalGradingTransform.RotateLeft));
        source.Points[1]=new(21,0);
        Assert.Null(rule.OrthogonalFor(source,result,OrthogonalGradingTransform.RotateLeft));
    }
}
