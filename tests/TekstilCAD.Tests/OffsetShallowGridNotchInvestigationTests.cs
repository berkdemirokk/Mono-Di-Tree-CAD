using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

public sealed class OffsetShallowGridNotchInvestigationTests(ITestOutputHelper log)
{
    private const long U=1_000_000;
    [Theory]
    [InlineData(1L,1000000L,false)]
    [InlineData(1L,10000000L,false)]
    [InlineData(1L,50000000L,false)]
    [InlineData(2L,10000000L,true)]
    [InlineData(7L,10000000L,false)]
    public void ExamineLongArmedShallowNotch(long depth,long width,bool reverse)
    {
        var grid=new Path64{new(0,0),new(100*U,0),new(100*U,100*U),new(50*U+width/2,100*U),
            new(50*U,100*U-depth),new(50*U-width/2,100*U),new(0,100*U)};
        if(reverse)grid.Reverse();
        var p=new PatternPiece{Closed=true,Points=grid.Select(q=>new Point2((double)q.X/U,(double)q.Y/U)).ToList()};
        var f=Clipper.InflatePaths(new Paths64{grid},U,JoinType.Miter,EndType.Polygon,2);
        var back=f.Count==1?Clipper.InflatePaths(f,-U,JoinType.Miter,EndType.Polygon,2):new Paths64();
        string status;try{var output=PatternTools.Offset(p,1);status=$"accepted output={output.Points.Count}";}
        catch(ArgumentException ex){status=ex.GetType().Name+": "+ex.Message;}
        log.WriteLine($"depth={depth} width={width} reverse={reverse} source={grid.Count} forward={string.Join(',',f.Select(x=>x.Count))} recovered={string.Join(',',back.Select(x=>x.Count))} production={status}");
        if(back.Count==1)log.WriteLine("recovered near notch="+string.Join(" ",back[0].Where(x=>x.X>=45*U && x.X<=55*U && x.Y>=99*U).Select(x=>$"({x.X},{x.Y})")));
        Assert.Equal(7,grid.Count);Assert.StartsWith("accepted",status);
        Assert.Single(f);Assert.Single(back);
        Assert.Contains(back[0],x=>x.X==50*U && x.Y==100*U-depth);
    }
}
