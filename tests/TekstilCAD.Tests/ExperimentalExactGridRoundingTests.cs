using System.Diagnostics;
using System.Numerics;
using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using System.Reflection;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class ExperimentalExactGridRoundingTests(ITestOutputHelper log)
{
    private const long U=1_000_000;
    // Independent oracle represents each double as an integer in 2^-1074 units.
    private static long Oracle(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int exp=(int)((bits>>52)&0x7ff);
        long frac=bits&0x000f_ffff_ffff_ffffL;BigInteger integer=exp==0?frac:frac|(1L<<52);
        integer<<=exp==0?0:exp-1;if(bits<0)integer=-integer;integer*=U;
        var denominator=BigInteger.One<<1074;var q=(BigInteger.Abs(integer)+denominator/2)/denominator;
        return checked((long)(integer.Sign<0?-q:q));
    }
    [Fact]
    public void BoundariesTiesSubnormalsAndRandomBitsMatchIntegerOracle()
    {
        double[] fixedValues=[0d,-0d,double.Epsilon,-double.Epsilon,.0078125,-.0078125,
            Math.BitDecrement(.0078125),Math.BitIncrement(.0078125),
            Math.BitDecrement(-.0078125),Math.BitIncrement(-.0078125),100_000_000,-100_000_000,
            Math.BitDecrement(100_000_000),Math.BitIncrement(-100_000_000),50_000_000.1234565,-50_000_000.1234565];
        foreach(double x in fixedValues){Assert.True(ExperimentalExactGridRounding.TryRound(x,out long actual));Assert.Equal(Oracle(x),actual);}
        Assert.True(ExperimentalExactGridRounding.TryRound(.0078125,out var pos));Assert.Equal(7813,pos);
        Assert.True(ExperimentalExactGridRounding.TryRound(-.0078125,out var neg));Assert.Equal(-7813,neg);
        foreach(double x in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,Math.BitIncrement(100_000_000d),Math.BitDecrement(-100_000_000d)})
            Assert.False(ExperimentalExactGridRounding.TryRound(x,out _));
        var rng=new Random(85372);var bytes=new byte[8];int checkedCount=0;
        for(int i=0;i<25_000;i++)
        {
            rng.NextBytes(bytes);double x=BitConverter.Int64BitsToDouble(BitConverter.ToInt64(bytes));
            if(!double.IsFinite(x) || Math.Abs(x)>Geometry.MaxCoordinate)continue;
            Assert.True(ExperimentalExactGridRounding.TryRound(x,out long actual));Assert.Equal(Oracle(x),actual);checkedCount++;
        }
        Assert.True(checkedCount>10_000);
    }
    private static GridPoint[] ExactGrid(IReadOnlyList<Point2> points)=>points.Select(p=>
    {
        Assert.True(ExperimentalExactGridRounding.TryRound(p.X,out var x));Assert.True(ExperimentalExactGridRounding.TryRound(p.Y,out var y));
        return new GridPoint(x,y);
    }).ToArray();
    private static (bool Ok,IReadOnlyList<GridPoint> Candidate,string Reason) Evaluate(PatternPiece piece,double mm)
    {
        try{_ = PatternTools.Offset(piece,mm);return(false,Array.Empty<GridPoint>(),"Production already succeeded.");}
        catch(OffsetLocalDetailUnverifiedException){}
        catch(ArgumentException ex){return(false,Array.Empty<GridPoint>(),"Original veto: "+ex.Message);}
        var points=piece.Points.ToArray();var grid=ExactGrid(points);
        var assembly=typeof(PatternTools).Assembly;
        var turn=assembly.GetType("TekstilCAD.Patterns.OffsetSourceGridTurnGuard",true)!.GetMethod("PreservesTurns",BindingFlags.NonPublic|BindingFlags.Static)!;
        if(!(bool)turn.Invoke(null,[points,grid.Select(p=>(p.X,p.Y)).ToArray(),CancellationToken.None])!)return(false,Array.Empty<GridPoint>(),"Source grid turn changed.");
        var physical=assembly.GetType("TekstilCAD.Patterns.OffsetBinaryGridPhysicalBudget",true)!.GetMethod("TryVerify",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] budget=[points,grid,.00001,.00002,.00002,null,CancellationToken.None];
        if(!(bool)physical.Invoke(null,budget)!)return(false,Array.Empty<GridPoint>(),(string)budget[5]!);
        long delta=checked((long)Math.Round(mm*U,MidpointRounding.AwayFromZero));
        var indexed=assembly.GetType("TekstilCAD.Patterns.OffsetIndexedSweepCertificate",true)!.GetMethod("TryCreate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args=[grid,delta,null,null,CancellationToken.None];
        if(!(bool)indexed.Invoke(null,args)!)return(false,Array.Empty<GridPoint>(),(string)args[3]!);
        var candidate=(IReadOnlyList<GridPoint>)args[2]!;
        var path=new Path64(grid.Select(p=>new Point64(p.X,p.Y)));var output=new Path64(candidate.Select(p=>new Point64(p.X,p.Y)));
        var recovered=Clipper.InflatePaths(new Paths64{output},-delta,JoinType.Miter,EndType.Polygon,2);
        var local=typeof(PatternTools).GetMethod("LocallyMatchesWithCancellation",BindingFlags.NonPublic|BindingFlags.Static)!
            .CreateDelegate<Func<Path64,Path64,double,CancellationToken,bool>>();
        if(recovered.Count!=1 || !local(path,recovered[0],8,CancellationToken.None) || !local(recovered[0],path,8,CancellationToken.None))
            return(false,Array.Empty<GridPoint>(),"Existing inverse local guard failed.");
        var xor=Clipper.Xor(new Paths64{path},recovered,FillRule.NonZero);double area=xor.Sum(p=>Math.Abs(Clipper.Area(p))),perimeter=0;
        for(int i=0;i<grid.Length;i++)
        {
            var a=grid[i];var b=grid[(i+1)%grid.Length];
            double dx=(double)(b.X-a.X),dy=(double)(b.Y-a.Y);perimeter+=Math.Sqrt(dx*dx+dy*dy);
        }
        if(area>perimeter*8+64)return(false,Array.Empty<GridPoint>(),"Existing inverse area guard failed.");
        return(true,candidate,"Experimental checks passed; features and production not certified.");
    }
    [Theory]
    [InlineData(false,0d)]
    [InlineData(true,0d)]
    [InlineData(false,50000000d)]
    [InlineData(true,50000000d)]
    [InlineData(false,99998000d)]
    [InlineData(true,99998000d)]
    public void LobedExactGridAndAllExperimentalChecks(bool reverse,double origin)
    {
        var m=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var piece=(PatternPiece)m.Invoke(null,[512,true])!;piece.Points=piece.Points.Select(p=>new Point2(p.X+origin,p.Y+origin)).ToList();
        if(reverse)piece.Points.Reverse();var before=piece.Points.ToArray();
        int different=before.Sum(p=>(ExperimentalExactGridRounding.TryRound(p.X,out var x) && x!=(long)Math.Round(p.X*U,MidpointRounding.AwayFromZero)?1:0)+
            (ExperimentalExactGridRounding.TryRound(p.Y,out var y) && y!=(long)Math.Round(p.Y*U,MidpointRounding.AwayFromZero)?1:0));
        var result=Evaluate(piece,1);log.WriteLine($"origin={origin} reverse={reverse} differences={different} experimental={result.Ok} reason={result.Reason}");
        Assert.Equal(before,piece.Points);Assert.Equal(origin==0?0:origin==50_000_000?3:4,different);
        if(origin==0)Assert.True(result.Ok,result.Reason);
        if(!result.Ok)Assert.Empty(result.Candidate);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactRoundingDoesNotBypassNotchAndChannelVetoes(bool reverse)
    {
        var notch=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(50.000002,100),
            new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)]};
        var channel=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)]};
        if(reverse){notch.Points.Reverse();channel.Points.Reverse();}
        var n=Evaluate(notch,1);var c=Evaluate(channel,5);
        log.WriteLine($"reverse={reverse}; notch={n.Reason}; channel={c.Reason}");
        Assert.False(n.Ok);Assert.False(c.Ok);Assert.Empty(n.Candidate);Assert.Empty(c.Candidate);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactRoundingDoesNotSupplyAnUnspecifiedVFeatureTolerance(bool reverse)
    {
        var method=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var p=PatternTools.InsertNotch((PatternPiece)method.Invoke(null,[512,true])!,146,.5,6,.5);
        int a=147,b=149;if(reverse){p.Points.Reverse();(a,b)=(p.Points.Count-1-b,p.Points.Count-1-a);}
        var result=Evaluate(p,1);Assert.True(result.Ok,result.Reason);
        var ca=result.Candidate[a];var cb=result.Candidate[b];
        double sourceWidth=Geometry.Distance(p.Points[a],p.Points[b]);
        double outputWidth=Geometry.Distance(new Point2((double)ca.X/U,(double)ca.Y/U),new Point2((double)cb.X/U,(double)cb.Y/U));
        Assert.InRange(outputWidth-sourceWidth,-.166,-.165);
        log.WriteLine($"reverse={reverse}; V source width={sourceWidth:G17}, candidate width={outputWidth:G17}; no feature tolerance was supplied");
    }
    [Fact]
    public void ReportRoundingPerformanceWithoutPromisingAThreshold()
    {
        var values=Enumerable.Range(0,1024).Select(i=>50_000_000+Math.Sin(i)*1000).ToArray();
        long checksum=0;
        void Current(int passes){for(int pass=0;pass<passes;pass++)foreach(var x in values)checksum+=(long)Math.Round(x*U,MidpointRounding.AwayFromZero)%17;}
        void Exact(int passes){for(int pass=0;pass<passes;pass++)foreach(var x in values){if(!ExperimentalExactGridRounding.TryRound(x,out var q))throw new InvalidOperationException();checksum+=q%17;}}
        Current(50);Exact(50);double current=double.PositiveInfinity,exact=double.PositiveInfinity;
        for(int trial=0;trial<3;trial++)
        {
            var sw=Stopwatch.StartNew();Current(1000);current=Math.Min(current,sw.Elapsed.TotalMilliseconds);
            sw.Restart();Exact(1000);exact=Math.Min(exact,sw.Elapsed.TotalMilliseconds);
        }
        log.WriteLine($"warmed best of 3, 1024000 values: production arithmetic={current:F3}ms, exact UInt128={exact:F3}ms, checksum={checksum}; no performance guarantee");
    }
}
