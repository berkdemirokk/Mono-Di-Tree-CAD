using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Experimental assessment checks; they do not certify production geometry.</summary>
public sealed class OffsetCandidateAssessmentTests
{
    private static (bool Accepted, IReadOnlyList<Point2> Points, string Reason) Assess(
        IReadOnlyList<GridPoint> source, long delta, CancellationToken token = default)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateAssessment", true)!;
        var method = type.GetMethod("TryAssess", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] arguments = [source, delta, null, null, token];
        try
        {
            var accepted = (bool)method.Invoke(null, arguments)!;
            return (accepted, (IReadOnlyList<Point2>)arguments[2]!, (string)arguments[3]!);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SquareProducesIndependentlyCalculatedCoordinatesWithoutChangingSource(bool reverse)
    {
        var source = new List<GridPoint> { new(0,0), new(1_000_000,0), new(1_000_000,1_000_000), new(0,1_000_000) };
        if (reverse) source.Reverse();
        var original = source.ToArray();
        var result = Assess(source, 100_000);
        Assert.True(result.Accepted, result.Reason);
        var expected = new[] { new Point2(-.1,-.1), new Point2(1.1,-.1), new Point2(1.1,1.1), new Point2(-.1,1.1) };
        if (reverse) Array.Reverse(expected);
        Assert.Equal(expected, result.Points);
        var list = Assert.IsAssignableFrom<IList<Point2>>(result.Points);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = new Point2(9, 9));
        Assert.Equal(original, source);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseSmoothCandidateIsGeometricallyValidOrReportsSpecificUncertainty(bool reverse)
    {
        var source = Enumerable.Range(0, 512).Select(i =>
        {
            double angle = Math.Tau * i / 512, radius = 1000 + 200 * Math.Cos(3 * angle);
            return new GridPoint((long)Math.Round(radius * Math.Cos(angle) * 1_000_000, MidpointRounding.AwayFromZero),
                (long)Math.Round(radius * Math.Sin(angle) * 1_000_000, MidpointRounding.AwayFromZero));
        }).ToList();
        if (reverse) source.Reverse();
        var original = source.ToArray();
        var result = Assess(source, 1_000_000);
        if (result.Accepted)
        {
            Assert.Equal(source.Count, result.Points.Count);
            Assert.False(Geometry.HasSelfIntersections(result.Points));
        }
        else
        {
            Assert.Empty(result.Points);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }
        Assert.Equal(original, source);
    }

    [Fact]
    public void CollapsedRecessAndSelfIntersectingSourceDoNotYieldAnAssessment()
    {
        GridPoint[][] inputs = [
            [new(0,0),new(100_000_000,0),new(100_000_000,100_000_000),new(52_000_000,100_000_000),new(52_000_000,20_000_000),new(48_000_000,20_000_000),new(48_000_000,100_000_000),new(0,100_000_000)],
            [new(0,0),new(10_000_000,10_000_000),new(0,10_000_000),new(10_000_000,0)]
        ];
        foreach (var source in inputs)
        {
            var original = source.ToArray();
            var result = Assess(source, 5_000_000);
            Assert.False(result.Accepted);
            Assert.Empty(result.Points);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
            Assert.Equal(original, source);
        }
    }

    [Theory]
    [InlineData(50_000_000_000_000L)]
    [InlineData(-50_000_000_000_000L)]
    public void LargeOriginStillProducesAValidBoundedContourOrExplainsUncertainty(long origin)
    {
        var source = new[] { new GridPoint(origin,origin), new GridPoint(origin+10_000_000,origin),
            new GridPoint(origin+10_000_000,origin+10_000_000), new GridPoint(origin,origin+10_000_000) };
        var before = source.ToArray();
        var result = Assess(source, 1_000_000);
        if (result.Accepted)
        {
            Assert.Equal(4, result.Points.Count);
            Assert.All(result.Points, Geometry.Validate);
            Assert.False(Geometry.HasSelfIntersections(result.Points));
        }
        else
        {
            Assert.Empty(result.Points);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }
        Assert.Equal(before, source);
    }

    [Fact]
    public void InvalidAllowanceAndOversizedInputAreRejectedWithoutMutatingSource()
    {
        var square = new[] { new GridPoint(0,0), new GridPoint(1_000_000,0), new GridPoint(1_000_000,1_000_000), new GridPoint(0,1_000_000) };
        foreach (long delta in new long[] { 0, -1 })
        {
            var result = Assess(square, delta);
            Assert.False(result.Accepted);
            Assert.Empty(result.Points);
        }
        var tooMany = Enumerable.Range(0, 1025).Select(i => new GridPoint(i, i)).ToArray();
        var before = tooMany.ToArray();
        var oversized = Assess(tooMany, 1);
        Assert.False(oversized.Accepted);
        Assert.Empty(oversized.Points);
        Assert.Equal(before, tooMany);
    }

    [Fact]
    public void CancellationNeverReturnsAnAssessmentOrChangesSource()
    {
        var source = new[] { new GridPoint(0,0), new GridPoint(1_000_000,0), new GridPoint(1_000_000,1_000_000), new GridPoint(0,1_000_000) };
        var original = source.ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Assess(source, 100_000, cancellation.Token));
        Assert.Equal(original, source);
    }
}
