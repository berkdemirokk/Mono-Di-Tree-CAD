using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit;

namespace TekstilCAD.Tests;
public class IndependentNotchWidthTests
{
    private static PatternPiece Rectangle(bool reverse=false, bool closedDuplicate=false)
    {
        List<Point2> p=[new(0,0),new(20,0),new(20,10),new(0,10)];
        if(reverse)p.Reverse(); if(closedDuplicate)p.Add(p[0]);
        return new PatternPiece { Points=p, Closed=true };
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void IndependentDimensionsCutInward(bool reverse)
    {
        var p=Rectangle(reverse);var result=PatternTools.InsertNotch(p,0,.5,4,1);
        Assert.Equal(4,Geometry.Distance(result.Points[1],result.Points[3]),8);
        Assert.Equal(1,Geometry.Distance((result.Points[1]+result.Points[3])*.5,result.Points[2]),8);
        Assert.True(Math.Abs(Geometry.SignedArea(result.Points))<Math.Abs(Geometry.SignedArea(p.Points)));
    }
    [Fact] public void EdgeBoundaryAndSelfIntersectionRejectWithoutMutation()
    {
        var p=Rectangle();var before=p.Points.ToList();
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertNotch(p,0,.1,4,1));
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertNotch(p,0,.5,4,11));
        Assert.Equal(before,p.Points);
    }
    [Fact] public void ExplicitClosureAndLegacyCallRemainCompatible()
    {
        var p=Rectangle(closedDuplicate:true);
        Assert.Equal(PatternTools.InsertNotch(p,0,.5,2).Points,PatternTools.InsertNotch(p,4,.5,4,2).Points);
    }
    [Fact] public void ClockwiseLargeOriginRejectsLostDepth()
    {
        var p=new PatternPiece { Closed=true, Points=[new(1000000,1000000),new(1000000,1000010),new(1000020,1000010),new(1000020,1000000)] };
        Assert.Throws<ArgumentException>(()=>PatternTools.InsertNotch(p,0,.5,4,1e-12));
    }
    [Fact] public void UnrepresentableDimensionsAndCancellationReject()
    {
        Assert.Throws<ArgumentException>(()=>PatternTools.NotchPoints(new(1000000,0),new(1000020,0),.5,1e-12,1));
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>PatternTools.InsertNotch(Rectangle(),0,.5,4,1,c.Token));
    }
}
