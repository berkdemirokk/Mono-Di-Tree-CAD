using System.Diagnostics;
using System.Reflection;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetAllTurnsLobedMeasurementTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportAllIndexedTurnEvidenceAtExplicitGridBudgets(bool reverse)
    {
        const long grid=1_000_000;
        var source=Enumerable.Range(0,512).Select(i =>
        {
            var angle=Math.Tau*i/512; var radius=1000+200*Math.Cos(3*angle);
            return new GridPoint((long)Math.Round(radius*Math.Cos(angle)*grid,MidpointRounding.AwayFromZero),
                (long)Math.Round(radius*Math.Sin(angle)*grid,MidpointRounding.AwayFromZero));
        }).ToArray();
        if(reverse) Array.Reverse(source);
        var before=source.ToArray(); var assembly=typeof(PatternTools).Assembly;
        var create=assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!
            .GetMethod("TryCreate",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] createArgs=[source,grid,null,null,CancellationToken.None];
        Assert.True((bool)create.Invoke(null,createArgs)!, (string?)createArgs[3]);
        var candidate=Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(createArgs[2]);
        var verify=assembly.GetType("TekstilCAD.Patterns.OffsetAllTurnsEvidence",true)!
            .GetMethod("TryVerify",BindingFlags.NonPublic|BindingFlags.Static)!;
        foreach(var (chord,height) in new (long,long)[]{(0,0),(1,1),(8,8),(64,64),
            (1_000_000,1),(1_000_000,8),(1_000_000,64),(1_000_000,1_000_000)})
        {
            object?[] args=[source,candidate,chord,height,null,CancellationToken.None];
            var timer=Stopwatch.StartNew(); bool ok=(bool)verify.Invoke(null,args)!;
            output.WriteLine($"reverse={reverse}; vertices={source.Length}; chord_grid={chord}; height_grid={height}; accepted={ok}; ms={timer.Elapsed.TotalMilliseconds:F3}; reason={args[4]}");
            Assert.False(string.IsNullOrWhiteSpace((string?)args[4]));
        }
        Assert.Equal(before,source);
    }
}
