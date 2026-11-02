using System.Numerics;
using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Independent double shifted-line oracle for fixture measurements, not a proof or production offset.</summary>
public sealed class OffsetIndependentMiterOracleInvestigationTests(ITestOutputHelper log)
{
    private const long U=1_000_000;
    private static GridPoint[] Grid(IEnumerable<Point2> p)=>p.Select(x=>new GridPoint(
        checked((long)Math.Round(x.X*U,MidpointRounding.AwayFromZero)),
        checked((long)Math.Round(x.Y*U,MidpointRounding.AwayFromZero)))).ToArray();
    private static GridPoint[] Refined(GridPoint[] source,long delta)
    {
        var origin=source[0];var path=new Path64(source.Select(p=>new Point64(checked((p.X-origin.X)*10),checked((p.Y-origin.Y)*10))));
        var result=Assert.Single(Clipper.InflatePaths(new Paths64{path},10*delta,JoinType.Miter,EndType.Polygon,2));
        static long Round(long value)=>value>=0?(value+5)/10:-((-value+5)/10);
        return result.Select(p=>new GridPoint(checked(Round(p.X)+origin.X),checked(Round(p.Y)+origin.Y))).ToArray();
    }
    private static (bool Ok,IReadOnlyList<GridPoint> Points,string Reason) Indexed(GridPoint[] source,long delta)
    {
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetIndexedSweepCertificate",true)!
            .GetMethod("TryCreate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args=[source,delta,null,null,CancellationToken.None];
        bool ok=(bool)m.Invoke(null,args)!;return(ok,(IReadOnlyList<GridPoint>)args[2]!, (string)args[3]!);
    }
    // Independent intersection of the two source edge lines shifted by delta.
    // Coordinates are subtracted as integers before floating conversion.
    private static (double X,double Y)[] Offsets(GridPoint[] s,long delta)
    {
        BigInteger area=0;for(int i=0;i<s.Length;i++)
        {var a=s[i];var b=s[(i+1)%s.Length];area+=(BigInteger)a.X*b.Y-(BigInteger)a.Y*b.X;}
        int orientation=area.Sign;Assert.NotEqual(0,orientation);
        return Enumerable.Range(0,s.Length).Select(i=>
        {
            var a=s[(i+s.Length-1)%s.Length];var b=s[i];var c=s[(i+1)%s.Length];
            double ux=(double)(b.X-a.X),uy=(double)(b.Y-a.Y),vx=(double)(c.X-b.X),vy=(double)(c.Y-b.Y);
            double lu=Math.Sqrt(ux*ux+uy*uy),lv=Math.Sqrt(vx*vx+vy*vy);
            double n1x=orientation*uy/lu,n1y=-orientation*ux/lu,n2x=orientation*vy/lv,n2y=-orientation*vx/lv;
            double denominator=1+n1x*n2x+n1y*n2y;
            Assert.True(denominator>1e-9);
            return ((n1x+n2x)*delta/denominator,(n1y+n2y)*delta/denominator);
        }).ToArray();
    }
    private static (int Start,double Max,double P95) Compare(GridPoint[] source,IReadOnlyList<GridPoint> actual,long delta)
    {
        var expected=Offsets(source,delta);int n=source.Length;Assert.Equal(n,actual.Count);
        double Distance(int si,int ci)
        {
            double dx=(double)(actual[ci].X-source[si].X)-expected[si].X;
            double dy=(double)(actual[ci].Y-source[si].Y)-expected[si].Y;
            return Math.Sqrt(dx*dx+dy*dy);
        }
        int start=Enumerable.Range(0,n).MinBy(i=>Distance(0,i));
        var d=Enumerable.Range(0,n).Select(i=>Distance(i,(start+i)%n)).Order().ToArray();
        return(start,d[^1],d[(int)(n*.95)]);
    }
    [Theory]
    [InlineData(false,0d)]
    [InlineData(true,0d)]
    [InlineData(false,50000000d)]
    [InlineData(true,50000000d)]
    [InlineData(false,99998000d)]
    [InlineData(true,99998000d)]
    public void LobedIndependentShiftedEdgeResiduals(bool reverse,double origin)
    {
        var fixture=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var p=(PatternPiece)fixture.Invoke(null,[512,true])!;
        p.Points=p.Points.Select(q=>new Point2(q.X+origin,q.Y+origin)).ToList();if(reverse)p.Points.Reverse();
        var s=Grid(p.Points);var high=Refined(s,U);Assert.Equal(s.Length,high.Length);
        var h=Compare(s,high,U);var indexed=Indexed(s,U);
        log.WriteLine($"lobed reverse={reverse}; origin={origin}; high max={h.Max:F6} grid p95={h.P95:F6}; indexed={indexed.Ok} reason={indexed.Reason}");
        Assert.InRange(h.Max,0,200);if(!indexed.Ok){Assert.Empty(indexed.Points);return;}
        var z=Compare(s,indexed.Points,U);log.WriteLine($"indexed independent miter max={z.Max:F6} grid p95={z.P95:F6}");
        Assert.InRange(z.Max,0,1);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedWithPhysicalVCompareWithoutAssumingFeatureSafety(bool reverse)
    {
        var fixture=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var p=PatternTools.InsertNotch((PatternPiece)fixture.Invoke(null,[512,true])!,146,.5,6,.5);
        if(reverse)p.Points.Reverse();var s=Grid(p.Points);var high=Refined(s,U);var indexed=Indexed(s,U);
        log.WriteLine($"notched reverse={reverse}; source={s.Length}; high={high.Length}; indexed={indexed.Ok}; reason={indexed.Reason}");
        Assert.True(indexed.Ok,indexed.Reason);Assert.Equal(s.Length,indexed.Points.Count);
        var z=Compare(s,indexed.Points,U);Assert.InRange(z.Max,0,1);
        if(high.Length==s.Length){var h=Compare(s,high,U);log.WriteLine($"high independent miter max={h.Max:F6} grid; indexed max={z.Max:F6} grid");}
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneGridNotchAndClosingChannelDoNotHaveIndexedCorrespondence(bool reverse)
    {
        GridPoint[] notch=[new(0,0),new(100*U,0),new(100*U,100*U),new(50*U+2,100*U),
            new(50*U+2,100*U-1),new(50*U,100*U-1),new(50*U,100*U),new(0,100*U)];
        GridPoint[] channel=[new(0,0),new(100*U,0),new(100*U,100*U),new(52*U,100*U),
            new(52*U,20*U),new(48*U,20*U),new(48*U,100*U),new(0,100*U)];
        if(reverse){Array.Reverse(notch);Array.Reverse(channel);}
        var notchHigh=Refined(notch,U);var channelHigh=Refined(channel,5*U);
        var ni=Indexed(notch,U);var ci=Indexed(channel,5*U);
        log.WriteLine($"reverse={reverse}; notch vertices {notch.Length}->{notchHigh.Length}, indexed={ni.Ok}; channel vertices {channel.Length}->{channelHigh.Length}, indexed={ci.Ok}");
        Assert.NotEqual(notch.Length,notchHigh.Length);Assert.NotEqual(channel.Length,channelHigh.Length);
        Assert.False(ni.Ok);Assert.False(ci.Ok);
    }
}
