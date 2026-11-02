using System.Reflection;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ExperimentalPhysicalNotchContractTests
{
    private static (PatternPiece Source,ExperimentalPhysicalNotchV1 Spec,Point2[] V) Fixture(bool reverse=false)
    {
        var p=new PatternPiece{Id=Guid.Parse("8c9670b6-d5d1-4c8f-957d-13ee6025d190"),Closed=true,
            Points=[new(0,0),new(100,0),new(100,100),new(0,100)]};
        if(reverse)p.Points.Reverse();
        var spec=new ExperimentalPhysicalNotchV1(1,p.Id,ExperimentalPhysicalNotchValidation.Fingerprint(p),0,p.Points[0],p.Points[1],
            "straight",.5,reverse?"right":"left","mm",4,2,0,0,0);
        var result=PatternTools.InsertNotch(p,0,.5,4,2);
        return(p,spec,[result.Points[1],result.Points[2],result.Points[3]]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitStraightEdgeAndOrientationValidate(bool reverse)
    {
        var (p,s,v)=Fixture(reverse);
        Assert.True(ExperimentalPhysicalNotchValidation.Verify(p,s,v,out var reason),reason);
        Assert.Contains("not certified",reason);
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,s with {Direction=reverse?"left":"right"},v,out _));
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,s with {EdgeKind="curve"},v,out _));
    }
    [Fact]
    public void TranslationAndReflectionRequireAnExplicitUpdatedSnapshotAndDirection()
    {
        var (p,s,v)=Fixture();
        static Point2 Reflect(Point2 x)=>new(-x.X+20,x.Y+10);
        var transformed=p.Clone();transformed.Points=transformed.Points.Select(Reflect).ToList();
        var transformedV=v.Select(Reflect).ToArray();
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(transformed,s,transformedV,out _));
        var rebound=s with {SnapshotHash=ExperimentalPhysicalNotchValidation.Fingerprint(transformed),
            EdgeStart=transformed.Points[0],EdgeEnd=transformed.Points[1],Direction="right"};
        Assert.True(ExperimentalPhysicalNotchValidation.Verify(transformed,rebound,transformedV,out var reason),reason);
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(transformed,rebound with {Direction="left"},transformedV,out _));
    }
    [Fact]
    public void MalformedOrStaleRecordsAndAlteredGeometryFailClosed()
    {
        var (p,s,v)=Fixture();
        foreach(var bad in new[]{s with {Version=2},s with {SourcePieceId=Guid.NewGuid()},s with {SourceEdgeIndex=1},
            s with {SnapshotHash=""},s with {Unit="inch"},s with {Fraction=0},s with {Fraction=double.NaN},
            s with {Width=-1},s with {Depth=0},s with {PositionTolerance=-1},s with {DepthTolerance=double.NaN},
            s with {EdgeStart=new Point2(1,0)}})
            Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,bad,v,out _));
        var wrong=v.ToArray();wrong[1]=wrong[1] with {Y=wrong[1].Y+.001};
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,s,wrong,out _));
        Assert.True(ExperimentalPhysicalNotchValidation.Verify(p,s with {PositionTolerance=.002,DepthTolerance=.002},wrong,out _));
        p.Points[2]=new(101,100);Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,s,v,out _));
    }
    [Fact]
    public void ExistingVersionOneDocumentDoesNotInventAPhysicalNotch()
    {
        const string json="[1,{\"Name\":\"legacy project\",\"Pieces\":[{\"Id\":\"8c9670b6-d5d1-4c8f-957d-13ee6025d190\",\"Name\":\"legacy\",\"Closed\":true,\"Points\":[{\"X\":0,\"Y\":0},{\"X\":100,\"Y\":0},{\"X\":100,\"Y\":100},{\"X\":0,\"Y\":100}]}],\"GradingRules\":[]}]";
        var m=typeof(ProjectStorage).GetMethod("DeserializeProject",BindingFlags.NonPublic|BindingFlags.Static)!;
        var project=(ProjectDocument)m.Invoke(null,[Encoding.UTF8.GetBytes(json),CancellationToken.None])!;
        var p=Assert.Single(project.Pieces);Assert.Null(p.Notches);Assert.Null(ExperimentalPhysicalNotchValidation.FromLegacy(p));
        p.Notches=[new NotchMark{VertexIndex=0,Position=p.Points[0]}];
        Assert.Null(ExperimentalPhysicalNotchValidation.FromLegacy(p));
    }
    [Fact]
    public void OutwardTipFailsEvenWithLargePositionTolerance()
    {
        var (p,s,_)=Fixture();Point2[] outward=[new(48,0),new(50,-2),new(52,0)];
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,s with {PositionTolerance=4},outward,out _));
    }
    [Fact]
    public void SlantedTipMustMatchPerpendicularDepth()
    {
        var (p,s,_)=Fixture();Point2[] slanted=[new(48,0),new(51.5,2),new(52,0)];
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,s with {Depth=2.5,PositionTolerance=1.6},slanted,out _));
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void ActualDirectionAndPerpendicularToleranceHoldAfterReversalAndReflection(bool reverse,bool reflect)
    {
        var (p,s,v)=Fixture(reverse);
        if(reflect)
        {
            static Point2 Mirror(Point2 x)=>new(-x.X,x.Y);
            p.Points=p.Points.Select(Mirror).ToList();v=v.Select(Mirror).ToArray();
            s=s with {SnapshotHash=ExperimentalPhysicalNotchValidation.Fingerprint(p),EdgeStart=p.Points[0],
                EdgeEnd=p.Points[1],Direction=s.Direction=="left"?"right":"left"};
        }
        var center=(v[0]+v[2])*.5;
        var outward=v.ToArray();outward[1]=center-(v[1]-center);
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,s with {PositionTolerance=4},outward,out _));
        var flat=v.ToArray();flat[1]=center;
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,s with {PositionTolerance=2,DepthTolerance=2},flat,out _));
        var along=(p.Points[1]-p.Points[0])*.015;
        var slanted=v.ToArray();slanted[1]=slanted[1]+along;
        var supplied=s with {Depth=2.5,PositionTolerance=1.6};
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,supplied,slanted,out _));
        Assert.False(ExperimentalPhysicalNotchValidation.Verify(p,supplied with {DepthTolerance=Math.BitDecrement(.5)},slanted,out _));
        Assert.True(ExperimentalPhysicalNotchValidation.Verify(p,supplied with {DepthTolerance=.5},slanted,out var reason),reason);
    }
    [Fact]
    public void DetachedThreePointCheckDoesNotBindAnOutputContour()
    {
        var (p,s,v)=Fixture();var unrelated=p.Clone();
        Assert.DoesNotContain(v[1],unrelated.Points);
        Assert.True(ExperimentalPhysicalNotchValidation.Verify(p,s,v,out _));
        // Verify has no candidate contour parameter: passing the three-point
        // measurement says nothing about membership in this unrelated output.
    }
}
