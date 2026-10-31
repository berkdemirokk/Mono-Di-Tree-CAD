using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Checks one explicitly selected notch, not production offset approval.</summary>
public sealed class OffsetNotchWitnessTests
{
    private static (bool Accepted, string Reason) Validate(IReadOnlyList<GridPoint> source,
        IReadOnlyList<GridPoint> candidate, int start, int end, long sourceDepth, long candidateDepth,
        CancellationToken token = default)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetNotchWitness", true)!;
        var method = type.GetMethod("TryValidate", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, candidate, start, end, sourceDepth, candidateDepth, null, token];
        try
        {
            var accepted = (bool)method.Invoke(null, args)!;
            return (accepted, (string)args[6]!);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static GridPoint[] Notch(long origin = 0, long depth = 30) =>
    [
        new(origin, origin), new(origin + 100, origin), new(origin + 100, origin + 100),
        new(origin + 70, origin + 100), new(origin + 70, origin + 100 - depth),
        new(origin + 30, origin + 100 - depth), new(origin + 30, origin + 100),
        new(origin, origin + 100)
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedNotchHasIndependentMinimumDepthInBothContours(bool reverse)
    {
        var source = Notch(); var candidate = Notch(depth: 20);
        if (reverse) { Array.Reverse(source); Array.Reverse(candidate); }
        var sourceBefore = source.ToArray(); var candidateBefore = candidate.ToArray();
        int start = reverse ? 1 : 3, end = reverse ? 4 : 6;

        var result = Validate(source, candidate, start, end, 30, 20);
        Assert.True(result.Accepted, result.Reason);
        Assert.Contains("not certified", result.Reason);
        Assert.Equal(sourceBefore, source);
        Assert.Equal(candidateBefore, candidate);
        Assert.False(Validate(source, candidate, start, end, 31, 20).Accepted);
        Assert.False(Validate(source, candidate, start, end, 30, 21).Accepted);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ShallowNotchCannotSatisfyLargerRequestedDepth(long depth)
    {
        var points = Notch(depth: depth);
        Assert.False(Validate(points, points, 3, 6, 3, 3).Accepted);
    }

    [Fact]
    public void InvalidIndicesAndNonPositiveThresholdsAreRejected()
    {
        var points = Notch();
        Assert.False(Validate(points, points, -1, 6, 1, 1).Accepted);
        Assert.False(Validate(points, points, 3, points.Length, 1, 1).Accepted);
        Assert.False(Validate(points, points, 3, 4, 1, 1).Accepted);
        Assert.False(Validate(points, points, 3, 6, 0, 1).Accepted);
        Assert.False(Validate(points, points[..^1], 3, 6, 1, 1).Accepted);
    }

    [Fact]
    public void WrongChainAndInvalidContoursCannotCertifyAFeature()
    {
        var points = Notch();
        Assert.False(Validate(points, points, 6, 3, 1, 1).Accepted);
        var square = new[] { new GridPoint(0, 0), new GridPoint(100, 0), new GridPoint(100, 100), new GridPoint(0, 100) };
        Assert.False(Validate(square, square, 0, 2, 1, 1).Accepted);
        var crossed = new[] { new GridPoint(0, 0), new GridPoint(100, 100), new GridPoint(0, 100), new GridPoint(100, 0) };
        Assert.False(Validate(crossed, crossed, 0, 2, 1, 1).Accepted);
    }

    [Theory]
    [InlineData(9000000000000000000L)]
    [InlineData(-9000000000000000000L)]
    public void LargeCoordinatesUseExactDifferenceArithmetic(long origin)
    {
        var points = Notch(origin);
        var result = Validate(points, points, 3, 6, 30, 30);
        Assert.True(result.Accepted, result.Reason);
    }

    [Fact]
    public void OversizedInputsAndCancellationNeverProduceApproval()
    {
        var points = Enumerable.Range(0, 1025).Select(i => new GridPoint(i, i * (long)i)).ToArray();
        Assert.False(Validate(points, points, 1, 5, 1, 1).Accepted);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Validate(Notch(), Notch(), 3, 6, 1, 1, cancellation.Token));
    }
}
