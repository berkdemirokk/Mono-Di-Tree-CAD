using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Explicit local witnesses only; these tests do not establish global notch preservation.</summary>
public sealed class OffsetFeatureCorrespondenceTests
{
    private static (bool Accepted, int Count, string Reason) Evaluate(
        IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate, IReadOnlyList<int> vertices,
        long sourceDepth = 2, long candidateDepth = 2, long edgeLength = 2, CancellationToken token = default)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetFeatureCorrespondence", true)!;
        var method = type.GetMethod("TryEvaluate", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, candidate, vertices, sourceDepth, candidateDepth, edgeLength, null, null, token];
        try
        {
            var accepted = (bool)method.Invoke(null, args)!;
            var evidence = ((IEnumerable)args[6]!).Cast<object>().Count();
            return (accepted, evidence, (string)args[7]!);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static GridPoint[] TwoNotches(long origin = 0, long shallowDepth = 10) =>
    [
        new(origin, origin), new(origin + 100, origin), new(origin + 100, origin + 100),
        new(origin + 80, origin + 100), new(origin + 80, origin + 70),
        new(origin + 65, origin + 70), new(origin + 65, origin + 100),
        new(origin + 45, origin + 100), new(origin + 45, origin + 100 - shallowDepth),
        new(origin + 30, origin + 100 - shallowDepth), new(origin + 30, origin + 100),
        new(origin, origin + 100)
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryExplicitConcaveVertexHasItsOwnEvidenceInEitherWinding(bool reverse)
    {
        var source = TwoNotches(); var candidate = TwoNotches();
        var selected = new[] { 4, 5, 8, 9 };
        if (reverse)
        {
            Array.Reverse(source); Array.Reverse(candidate);
            selected = selected.Select(i => source.Length - 1 - i).ToArray();
        }
        var sourceBefore = source.ToArray(); var candidateBefore = candidate.ToArray();
        var result = Evaluate(source, candidate, selected);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(selected.Length, result.Count);
        Assert.Equal(sourceBefore, source);
        Assert.Equal(candidateBefore, candidate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeepNeighborCannotHideShallowSelectedVertices(bool reverse)
    {
        var source = TwoNotches(); var candidate = TwoNotches(shallowDepth: 1);
        var selected = new[] { 4, 5, 8, 9 };
        if (reverse)
        {
            Array.Reverse(source); Array.Reverse(candidate);
            selected = selected.Select(i => source.Length - 1 - i).ToArray();
        }
        var result = Evaluate(source, candidate, selected, sourceDepth: 2, candidateDepth: 2);
        Assert.False(result.Accepted);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void ACollapsedSelectedEdgeCannotProduceAnApproval()
    {
        var source = TwoNotches(); var candidate = TwoNotches();
        candidate[5] = candidate[4];
        Assert.False(Evaluate(source, candidate, [4, 5]).Accepted);
    }

    [Fact]
    public void PassingIndependentMinimumsDoesNotProveOriginalDepthWasPreserved()
    {
        // The first notch is 30 units deep in the source but only 3 in the
        // candidate. Both local corners still exceed the independently chosen
        // two-unit minimum. This acceptance demonstrates the diagnostic's
        // explicit limit: it is not a proof of equal feature dimensions.
        var source = TwoNotches();
        var candidate = TwoNotches();
        candidate[4] = new GridPoint(80, 97);
        candidate[5] = new GridPoint(65, 97);
        var result = Evaluate(source, candidate, [4, 5], sourceDepth: 2, candidateDepth: 2);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(9000000000000000000L)]
    [InlineData(-9000000000000000000L)]
    public void LargeOriginsUseDifferencesWithoutIntegerOverflow(long origin)
    {
        var source = TwoNotches(origin); var candidate = TwoNotches(origin);
        var result = Evaluate(source, candidate, [4, 5, 8, 9]);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void InvalidSelectionThresholdsAndCancellationCannotApprove()
    {
        var contour = TwoNotches();
        Assert.False(Evaluate(contour, contour, [-1]).Accepted);
        Assert.False(Evaluate(contour, contour, [contour.Length]).Accepted);
        Assert.False(Evaluate(contour, contour, [4], sourceDepth: 0).Accepted);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Evaluate(contour, contour, [4], token: cancellation.Token));
    }
}
