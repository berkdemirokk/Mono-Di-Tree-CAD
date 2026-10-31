using System.Reflection;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// These tests concern a topological diagnostic, never an offset approval.
public sealed class OffsetMorphDiagnosticTests(ITestOutputHelper output)
{
    private static (bool Ok, string Reason) Verify(GridPoint[] source, GridPoint[] target)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetMorphDiagnostic",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,target,null,CancellationToken.None];
        bool ok=(bool)method.Invoke(null,args)!;
        return (ok,(string)args[2]!);
    }
    private static GridPoint[] Candidate(GridPoint[] source)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!
            .GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,1_000_000L,null,null,CancellationToken.None];
        Assert.True((bool)method.Invoke(null,args)!, (string?)args[3]);
        return ((IReadOnlyList<GridPoint>)args[2]!).ToArray();
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportsLobedCandidateMorph(bool reverse)
    {
        var s=Enumerable.Range(0,512).Select(i=>{
            var a=Math.Tau*i/512; var r=1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(r*Math.Cos(a)*1_000_000,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*1_000_000,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse) Array.Reverse(s);
        var result=Verify(s,Candidate(s));
        output.WriteLine($"reverse={reverse}; accepted={result.Ok}; reason={result.Reason}");
        // Diagnostic measurement: rejection is valid and does not authorize production.
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
    [Fact]
    public void FixedTurnsAndIsotopyDoNotPreserveNotchDepth()
    {
        GridPoint[] source=[new(0,0),new(100,0),new(100,100),new(80,100),new(80,70),new(65,70),new(65,100),new(45,100),new(45,90),new(30,90),new(30,100),new(0,100)];
        var target=source.ToArray(); target[4]=new(80,97); target[5]=new(65,97);
        var result=Verify(source,target);
        Assert.True(result.Ok,result.Reason);
        Assert.Equal(30,source[3].Y-source[4].Y);
        Assert.Equal(3,target[3].Y-target[4].Y);
    }
    [Fact]
    public void TurnCollapseIsRejected()
    {
        GridPoint[] s=[new(0,0),new(100,0),new(100,100),new(0,100)];
        var t=s.ToArray(); t[1]=new(0,100);
        Assert.False(Verify(s,t).Ok);
    }
}
