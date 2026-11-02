using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ExperimentalNotchContourBindingTests
{
    private static (PatternPiece Source,PatternPiece Output,ExperimentalPhysicalNotchV1 Spec) Fixture(bool reverse,int edge)
    {
        var p=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(0,100)]};
        if(reverse)p.Points.Reverse();
        var spec=new ExperimentalPhysicalNotchV1(1,p.Id,ExperimentalPhysicalNotchValidation.Fingerprint(p),edge,
            p.Points[edge],p.Points[(edge+1)%p.Points.Count],"straight",.5,reverse?"right":"left","mm",4,2,0,0,0);
        return(p,PatternTools.InsertNotch(p,edge,.5,4,2),spec);
    }
    [Theory]
    [InlineData(false,0)]
    [InlineData(true,0)]
    [InlineData(false,3)]
    [InlineData(true,3)]
    public void SourceEdgeIsReplacedByOrderedMouthTipMouth(bool reverse,int edge)
    {
        var (p,o,s)=Fixture(reverse,edge);var before=p.Points.ToArray();var output=o.Points.ToArray();
        Assert.True(ExperimentalNotchContourBinding.Verify(p,o,o.Id,s,out var reason),reason);
        o.Points=o.Points.Skip(2).Concat(o.Points.Take(2)).ToList();
        Assert.True(ExperimentalNotchContourBinding.Verify(p,o,o.Id,s,out reason),reason);
        Assert.Equal(before,p.Points);Assert.Equal(output.OrderBy(x=>x.X).ThenBy(x=>x.Y),o.Points.OrderBy(x=>x.X).ThenBy(x=>x.Y));
    }
    [Fact]
    public void WrongContourIdentityMissingPointAndWrongOrderFail()
    {
        var (p,o,s)=Fixture(false,0);
        Assert.False(ExperimentalNotchContourBinding.Verify(p,o,Guid.NewGuid(),s,out _));
        var missing=o.Clone();missing.Points.RemoveAt(2);Assert.False(ExperimentalNotchContourBinding.Verify(p,missing,missing.Id,s,out _));
        var changed=o.Clone();changed.Points[4]=new(101,0);Assert.False(ExperimentalNotchContourBinding.Verify(p,changed,changed.Id,s,out _));
        var swapped=o.Clone();(swapped.Points[1],swapped.Points[3])=(swapped.Points[3],swapped.Points[1]);
        Assert.False(ExperimentalNotchContourBinding.Verify(p,swapped,swapped.Id,s,out _));
        var stale=p.Clone();stale.Points[2]=new(101,100);Assert.False(ExperimentalNotchContourBinding.Verify(stale,o,o.Id,s,out _));
        Assert.False(ExperimentalNotchContourBinding.Verify(p,o,o.Id,s with {SourcePieceId=Guid.NewGuid()},out _));
        Assert.False(ExperimentalNotchContourBinding.Verify(p,o,o.Id,s with {EdgeKind="curve"},out _));
    }
    [Fact]
    public void SelfIntersectionFailsEvenWhenVMeasurementsPass()
    {
        var (p,o,s)=Fixture(false,0);o.Points[2]=new Point2(50,120);s=s with {Depth=120};
        Point2[] v=[o.Points[1],o.Points[2],o.Points[3]];
        Assert.True(ExperimentalPhysicalNotchValidation.Verify(p,s,v,out var measurement),measurement);
        Assert.True(Geometry.HasSelfIntersections(o.Points));
        Assert.False(ExperimentalNotchContourBinding.Verify(p,o,o.Id,s,out var reason));
        Assert.Contains("intersects",reason);
    }
    [Fact]
    public void ApproximateMouthOffSourceEdgeCannotClaimReplacement()
    {
        var (p,o,s)=Fixture(false,0);o.Points[1]=new Point2(48,.1);s=s with {PositionTolerance=.2,WidthTolerance=.1,DepthTolerance=.1};
        Assert.False(ExperimentalNotchContourBinding.Verify(p,o,o.Id,s,out _));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeSquareWithHalfSquareMillimeterNotchKeepsExactAreaOrdering(bool reverse)
    {
        const double size=100_000_000;
        var p=new PatternPiece{Closed=true,Points=[new(0,0),new(size,0),new(size,size),new(0,size)]};
        var o=new PatternPiece{Closed=true,Points=[new(0,0),new(size/2-.5,0),new(size/2,1),new(size/2+.5,0),
            new(size,0),new(size,size),new(0,size)]};
        if(reverse){p.Points.Reverse();o.Points.Reverse();}
        int edge=reverse?2:0;
        var s=new ExperimentalPhysicalNotchV1(1,p.Id,ExperimentalPhysicalNotchValidation.Fingerprint(p),edge,p.Points[edge],p.Points[(edge+1)%p.Points.Count],
            "straight",.5,reverse?"right":"left","mm",1,1,0,0,0);
        Assert.Equal(Geometry.SignedArea(p.Points),Geometry.SignedArea(o.Points));
        Assert.True(ExperimentalNotchContourBinding.Verify(p,o,o.Id,s,out var reason),reason);
    }
}
