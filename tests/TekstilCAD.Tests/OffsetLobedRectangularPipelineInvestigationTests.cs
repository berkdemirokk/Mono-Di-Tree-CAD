using System.Diagnostics;
using System.Reflection;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Measured fixture behavior, not a benchmark or production certificate.</summary>
public sealed class OffsetLobedRectangularPipelineInvestigationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasureCandidateDiscoveryAndPipelineForActualLobedFixture(bool reverse)
    {
        const long grid = 1_000_000;
        var source = Enumerable.Range(0,512).Select(i =>
        {
            double a = Math.Tau*i/512, radius = 1000+200*Math.Cos(3*a);
            return new GridPoint((long)Math.Round(radius*Math.Cos(a)*grid,MidpointRounding.AwayFromZero),
                (long)Math.Round(radius*Math.Sin(a)*grid,MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var before = source.ToArray(); var assembly = typeof(PatternTools).Assembly;
        MethodInfo Method(string type, string method) => assembly.GetType("TekstilCAD.Patterns."+type,true)!
            .GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static)!;
        var timer = Stopwatch.StartNew();
        object?[] create = [source,grid,null,null,CancellationToken.None];
        bool created = (bool)Method("OffsetCandidateProof","TryCreate").Invoke(null,create)!;
        output.WriteLine($"reverse={reverse}; candidate={created}; ms={timer.Elapsed.TotalMilliseconds:F3}; reason={create[3]}");
        Assert.True(created,(string?)create[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(create[2]);
        timer.Restart();
        object?[] discovery = [source,null,null,CancellationToken.None];
        bool discovered = (bool)Method("OffsetRectangularNotchDiscovery","TryDiscover").Invoke(null,discovery)!;
        output.WriteLine($"reverse={reverse}; source_discovery={discovered}; ms={timer.Elapsed.TotalMilliseconds:F3}; reason={discovery[2]}");
        timer.Restart();
        object?[] ideal = [source,candidate,grid,null,CancellationToken.None];
        bool idealOk = (bool)Method("OffsetIdealBoundaryEvidenceExperiment","TryVerify").Invoke(null,ideal)!;
        output.WriteLine($"reverse={reverse}; ideal_boundary={idealOk}; ms={timer.Elapsed.TotalMilliseconds:F3}; reason={ideal[3]}");
        timer.Restart();
        object?[] pipeline = [source,candidate,grid,2*grid,2*grid,null,CancellationToken.None];
        bool certified = (bool)Method("OffsetRectangularNotchPipeline","TryVerify").Invoke(null,pipeline)!;
        output.WriteLine($"reverse={reverse}; pipeline={certified}; ms={timer.Elapsed.TotalMilliseconds:F3}; reason={pipeline[5]}");
        Assert.False(discovered);
        Assert.False(certified);
        Assert.Equal(before,source);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(512)]
    [InlineData(707)]
    public void MeasureBoundedConvexDiscovery(int count)
    {
        var source = Enumerable.Range(0,count).Select(i =>
        {
            double a = Math.Tau*i/count;
            return new GridPoint((long)Math.Round(1_000_000_000*Math.Cos(a)),
                (long)Math.Round(1_000_000_000*Math.Sin(a)));
        }).ToArray();
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetRectangularNotchDiscovery",true)!
            .GetMethod("TryDiscover",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args = [source,null,null,CancellationToken.None];
        var timer = Stopwatch.StartNew();
        bool result = (bool)method.Invoke(null,args)!;
        output.WriteLine($"vertices={count}; theoretical_nonadjacent_pairs={(long)count*(count-3)/2}; accepted={result}; ms={timer.Elapsed.TotalMilliseconds:F3}; reason={args[2]}");
        Assert.True(result,(string?)args[2]);
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(args[1]).Cast<object>());
    }
}
