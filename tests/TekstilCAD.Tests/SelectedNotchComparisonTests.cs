using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;

public class SelectedNotchComparisonTests
{
    private static readonly Point2[] Source=[new(0,0),new(100,0),new(100,100),new(60,100),
        new(50,90),new(40,100),new(0,100)];
    [Fact]
    public void ExplicitDifferentIndicesDetectCollapsedNotch()
    {
        Point2[] output=[new(60,100),new(50,99.999999),new(40,100)];
        var result=SelectedNotchComparison.Compare(Source,output,3,4,5,0,1,2,0,0.001);
        Assert.False(result.WithinTolerance);Assert.Equal(10,result.SourceDepth);
        Assert.InRange(result.CandidateDepth,0,0.000002);
    }
    [Fact]
    public void ExplicitDifferentIndicesCanMatchWithinUserTolerance()
    {
        Point2[] output=[new(60,100),new(50,91),new(40,100)];
        var result=SelectedNotchComparison.Compare(Source,output,3,4,5,0,1,2,0,1);
        Assert.True(result.WithinTolerance);Assert.Equal(20,result.SourceWidth);Assert.Equal(9,result.CandidateDepth);
    }
    [Fact]
    public void InvalidOrReversedWitnessIsRejected()
    {
        Point2[] output=[new(60,100),new(50,101),new(40,100)];
        Assert.Throws<ArgumentException>(()=>SelectedNotchComparison.Compare(Source,output,3,4,5,0,1,2,0,1));
        Assert.Throws<ArgumentException>(()=>SelectedNotchComparison.Compare(Source,output,3,4,5,0,0,2,0,1));
        Assert.Throws<ArgumentException>(()=>SelectedNotchComparison.Compare(Source,output,3,4,5,0,1,2,-1,1));
    }
    [Fact]
    public void TinyRepresentableDimensionsDoNotUnderflowToNan()
    {
        Point2[] tiny=[new(0,0),new(1e-200,1e-200),new(2e-200,0)];
        var result=SelectedNotchComparison.Compare(tiny,tiny,0,1,2,0,1,2,0,0);
        Assert.True(result.WithinTolerance);Assert.True(double.IsFinite(result.SourceWidth));
        Assert.True(double.IsFinite(result.SourceDepth));Assert.True(result.SourceWidth>0 && result.SourceDepth>0);
    }
    [Fact]
    public void NonrepresentableDepthIsExplicitlyRejected()
    {
        double e=double.Epsilon;
        Point2[] tiny=[new(0,0),new(e,e),new(3*e,4*e)];
        var ex=Assert.Throws<ArgumentException>(()=>SelectedNotchComparison.Compare(tiny,tiny,0,1,2,0,1,2,0,0));
        Assert.Contains("temsil",ex.Message);
    }}

