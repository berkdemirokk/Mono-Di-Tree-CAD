using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class OffsetVisualMarkCorrespondenceExperimentTests
{
    private static (bool Ok,string Reason) Verify(PatternPiece s,PatternPiece c)
    {
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetVisualMarkCorrespondenceExperiment",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[s,c,200L,125L,126L,1_000_000L,1_000L,500L,null,CancellationToken.None];
        bool ok=(bool)m.Invoke(null,args)!;return(ok,(string)args[8]!);
    }
    private static (PatternPiece Source,PatternPiece Candidate) Square(bool reverse,bool marked)
    {
        var s=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(0,100)]};
        var c=new PatternPiece{Closed=true,Points=[new(-.0002,-.0002),new(100.0002,-.0002),new(100.0002,100.0002),new(-.0002,100.0002)]};
        if(reverse){s.Points.Reverse();c.Points.Reverse();}
        if(marked)
        {
            int index=reverse?2:1;
            s.Notches=[new NotchMark{VertexIndex=index,Position=s.Points[index]}];
            c.Notches=[new NotchMark{VertexIndex=index,Position=c.Points[index]}];
        }
        return(s,c);
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public void ValidHypotheticalMappingMakesNoDirectionClaim(bool reverse,bool marked)
    {
        var (s,c)=Square(reverse,marked);var before=s.Points.ToArray();var result=Verify(s,c);
        Assert.True(result.Ok,result.Reason);Assert.Contains("direction",result.Reason);Assert.Contains("not certified",result.Reason);
        Assert.Equal(before,s.Points);
        if(marked)Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(s,.0002));
        else Assert.NotNull(PatternTools.Offset(s,.0002));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RotationRequiresCorrectMappedMarkerIndex(bool reverse)
    {
        var (s,c)=Square(reverse,true);int old=c.Notches![0].VertexIndex;
        c.Points=[c.Points[2],c.Points[3],c.Points[0],c.Points[1]];
        int mapped=(old+2)%4;c.Notches[0].VertexIndex=mapped;c.Notches[0].Position=c.Points[mapped];
        var ok=Verify(s,c);Assert.True(ok.Ok,ok.Reason);
        int wrong=(mapped+1)%4;c.Notches[0].VertexIndex=wrong;c.Notches[0].Position=c.Points[wrong];
        Assert.False(Verify(s,c).Ok);
    }
    [Fact]
    public void MissingUnexpectedInvalidDuplicateOrUnanchoredMarkersFail()
    {
        var (s,c)=Square(false,true);c.Notches=null;Assert.False(Verify(s,c).Ok);
        (s,c)=Square(false,false);c.Notches=[new NotchMark{VertexIndex=1,Position=c.Points[1]}];Assert.False(Verify(s,c).Ok);
        (s,c)=Square(false,true);s.Notches=[];Assert.False(Verify(s,c).Ok);
        (s,c)=Square(false,true);s.Notches![0].Position=new(100,1);Assert.False(Verify(s,c).Ok);
        (s,c)=Square(false,true);c.Notches![0].VertexIndex=99;Assert.False(Verify(s,c).Ok);
        (s,c)=Square(false,true);s.Notches!.Add(s.Notches[0].Clone());Assert.False(Verify(s,c).Ok);
        (s,c)=Square(false,true);c.Points.Reverse();Assert.False(Verify(s,c).Ok);
    }
    [Fact]
    public void OffGridAndExplicitClosingVertexAreNotSilentlyNormalized()
    {
        var (s,c)=Square(false,false);s.Points[0]=new(.0000004,0);Assert.False(Verify(s,c).Ok);
        (s,c)=Square(false,false);s.Points.Add(s.Points[0]);c.Points.Add(c.Points[0]);Assert.False(Verify(s,c).Ok);
    }
    [Fact]
    public void InteriorEdgePointIsNotAValidVertexAnchoredMark()
    {
        var (source,_)=Square(false,false);
        source.Notches=[new NotchMark{VertexIndex=0,Position=new Point2(50,0)}];
        Assert.Throws<ArgumentException>(()=>MarkedPieceTranslation.Translate(source,0,0));
        Assert.Throws<ArgumentException>(()=>PatternTools.Offset(source,.0002));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhysicalVNotchIsContourGeometryAndIsNotANotchMark(bool reverse)
    {
        var (source,_)=Square(reverse,false);
        var physical=PatternTools.InsertNotch(source,0,.5,4,2);
        Assert.Null(physical.Notches);Assert.Equal(source.Points.Count+3,physical.Points.Count);
        Assert.Equal(4,Geometry.Distance(physical.Points[1],physical.Points[3]),10);
        var middle=physical.Points[1]+(physical.Points[3]-physical.Points[1])*.5;
        Assert.Equal(2,Geometry.Distance(middle,physical.Points[2]),10);
        var visual=source.Clone();visual.Notches=[new NotchMark{VertexIndex=0,Position=source.Points[0]}];
        Assert.Equal(source.Points,visual.Points);Assert.Single(visual.Notches);
        Assert.NotEqual(visual.Points.Count,physical.Points.Count);
    }
}
