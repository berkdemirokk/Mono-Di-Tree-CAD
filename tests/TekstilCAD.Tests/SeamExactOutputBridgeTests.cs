using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class SeamExactOutputBridgeTests
{
    private static readonly GridPoint[] Nested = [new(0,0), new(100,0), new(100,100),
        new(80,100), new(80,60), new(60,60), new(60,55), new(50,55),
        new(50,60), new(20,60), new(20,100), new(0,100)];

    [Fact]
    public void ExactRotatedBoundaryMayContainOrderedCollinearSubdivisions()
    {
        var output = Nested.ToList();
        output.Insert(1, new GridPoint(50, 0));
        output = output.Skip(5).Concat(output.Take(5)).ToList();
        Assert.True(SeamExactOutputBridge.Matches(Nested, output, 1000, CancellationToken.None));
    }

    [Fact]
    public void LostNestedFeatureAndBacktrackingSubdivisionAreRejected()
    {
        var flattened = Nested.ToArray();
        flattened[6] = new(60,60); flattened[7] = new(50,60);
        Assert.False(SeamExactOutputBridge.Matches(Nested, flattened, 1000, CancellationToken.None));
        var backtracking = Nested.ToList();
        backtracking.InsertRange(1, [new GridPoint(75,0), new GridPoint(25,0)]);
        Assert.False(SeamExactOutputBridge.Matches(Nested, backtracking, 1000, CancellationToken.None));
    }

    [Fact]
    public void WorkLimitAndCancellationFailClosed()
    {
        Assert.False(SeamExactOutputBridge.Matches(Nested, Nested, 2, CancellationToken.None));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => SeamExactOutputBridge.Matches(Nested, Nested, 1000, cts.Token));
    }
}
