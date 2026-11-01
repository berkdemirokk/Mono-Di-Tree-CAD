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

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ApexMustRemainPerpendicularWhenLargeOriginRoundsAwayOneComponent(bool reverse)
    {
        const double x = 90_000_000, rise = 0.0000007;
        var points = new List<Point2> { new(x,0),new(x+100,rise),new(x+100,100+rise),new(x,100) };
        if (reverse) points.Reverse();
        int edge = reverse ? 2 : 0;
        var source = new PatternPiece { Points = points };
        var before = points.ToArray();
        // The nominal normal has an X component of about 7e-9 mm, which
        // rounds away at this X origin while the one-millimetre length survives.
        Assert.Throws<ArgumentException>(() => PatternTools.NotchPoints(points[edge], points[edge+1], .5, 4, 1));
        Assert.Throws<ArgumentException>(() => PatternTools.InsertNotch(source, edge, .5, 4, 1));
        Assert.Equal(before, source.Points);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void MouthEndpointsMustRemainOnSelectedEdgeEvenWhenWidthSurvives(bool reverse)
    {
        const double origin = 90_000_000;
        var rise = Math.Sqrt(9900);
        var a = new Point2(origin, 0); var b = new Point2(origin + 10, rise);
        if (reverse) (a,b) = (b,a);
        // The 4 mm mouth still measures within the existing relative width
        // tolerance, but each endpoint drifts about 2.97e-9 mm off the edge.
        var error = Assert.Throws<ArgumentException>(() => PatternTools.NotchPoints(a,b,.5,4,100/rise));
        Assert.Contains("hassasiyet",error.Message);
        var localA = new Point2(a.X - origin, a.Y); var localB = new Point2(b.X - origin, b.Y);
        Assert.Equal(3, PatternTools.NotchPoints(localA,localB,.5,4,100/rise).Count);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void RequestedFractionIsCheckedBeforeWorldCoordinateRounding(bool reverse)
    {
        const double x = 90_000_000;
        var points = new List<Point2> { new(x,0),new(x+100,0),new(x+100,100),new(x,100) };
        if (reverse) points.Reverse();
        int edge = reverse ? 2 : 0;
        var source = new PatternPiece { Points = points };
        var before = points.ToArray();
        // For one third of this edge, adding the local position to the world
        // origin loses about 4.97e-9 mm; recomputing the same rounded center
        // cannot reveal that loss. Width and depth here remain exactly 4 and 1.
        var error = Assert.Throws<ArgumentException>(() => PatternTools.InsertNotch(source,edge,1.0/3,4,1));
        Assert.Contains("konumu", error.Message);
        Assert.Equal(before,source.Points);
        var valid = PatternTools.InsertNotch(source,edge,.5,4,1);
        Assert.Equal(7,valid.Points.Count);
    }

    [Fact]
    public void AlreadyCancelledNotchInsertionOnMarkedPieceReportsCancellation()
    {
        var source = Rectangle();
        source.Notches = [new NotchMark { VertexIndex = 1, Position = source.Points[1] }];
        var before = source.Clone();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PatternTools.InsertNotch(source,0,.5,4,1,cancellation.Token));
        // Without cancellation the existing protection against losing marks remains.
        Assert.Throws<ArgumentException>(() => PatternTools.InsertNotch(source,0,.5,4,1));
        Assert.True(DocumentSession.ContentEquals(new ProjectDocument { Pieces = [before] },
            new ProjectDocument { Pieces = [source] }));
    }
}
