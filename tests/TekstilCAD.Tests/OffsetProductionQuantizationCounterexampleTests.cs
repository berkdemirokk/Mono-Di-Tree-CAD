using System.Numerics;
using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Investigates production quantization against exact binary64 rational values.</summary>
public sealed class OffsetProductionQuantizationCounterexampleTests(ITestOutputHelper log)
{
    private const long U=1_000_000;
    private static BigInteger Units(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int e=(int)((bits>>52)&0x7ff);
        long fraction=bits&0x000f_ffff_ffff_ffffL;BigInteger m=e==0?fraction:fraction|(1L<<52);
        if(bits<0)m=-m;return m<<(e==0?0:e-1);
    }
    private static long ExactNearest(double value)
    {
        var v=Units(value)*U;var denominator=BigInteger.One<<1074;
        var rounded=(BigInteger.Abs(v)+denominator/2)/denominator;
        return checked((long)(v.Sign<0?-rounded:rounded));
    }
    [Theory]
    [InlineData(false,0d)]
    [InlineData(true,0d)]
    [InlineData(false,50000000d)]
    [InlineData(true,50000000d)]
    [InlineData(false,99998000d)]
    [InlineData(true,99998000d)]
    public void ActualProductionGridCanDifferFromExactBinary64Nearest(bool reverse,double origin)
    {
        var fixture=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var source=(PatternPiece)fixture.Invoke(null,[512,true])!;
        source.Points=source.Points.Select(p=>new Point2(p.X+origin,p.Y+origin)).ToList();
        if(reverse)source.Points.Reverse();var before=source.Points.ToArray();
        int disagreements=0,maxDifference=0;var examples=new List<string>();
        for(int i=0;i<before.Length;i++)foreach(var (axis,value) in new[]{("X",before[i].X),("Y",before[i].Y)})
        {
            long actual=checked((long)Math.Round(value*U,MidpointRounding.AwayFromZero));
            long exact=ExactNearest(value);long distance=Math.Abs(actual-exact);
            if(distance==0)continue;disagreements++;maxDifference=(int)Math.Max(maxDifference,distance);
            if(examples.Count<3)examples.Add($"{i}{axis}: actual={actual}, exact={exact}");
        }
        string production;
        try{_ = PatternTools.Offset(source,1);production="accepted";}
        catch(ArgumentException ex){production=ex.GetType().Name;}
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetClosedEvaluation",true)!
            .GetMethod("Evaluate",BindingFlags.NonPublic|BindingFlags.Static)!;
        var evaluation=m.Invoke(null,[source,1d,.00001,.00002,.00002,CancellationToken.None])!;
        bool passed=(bool)evaluation.GetType().GetProperty("EvaluationPassed")!.GetValue(evaluation)!;
        string reason=(string)evaluation.GetType().GetProperty("Reason")!.GetValue(evaluation)!;
        log.WriteLine($"origin={origin:G17}; reverse={reverse}; disagreements={disagreements}; max_grid_difference={maxDifference}; production={production}; experimental={passed}; reason={reason}; {string.Join("; ",examples)}");
        Assert.Equal(nameof(OffsetLocalDetailUnverifiedException),production);
        Assert.Equal(before,source.Points);
        if(origin==0){Assert.Equal(0,disagreements);Assert.True(passed,reason);}
        else {Assert.True(disagreements>0);Assert.Equal(1,maxDifference);Assert.False(passed);Assert.Contains("not the exact nearest source grid point",reason);}
    }
}
