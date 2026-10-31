using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Tests an experimental proof helper, not production offset authorization.</summary>
public sealed class OffsetCandidateProofTests
{
    private static (bool Accepted, IReadOnlyList<GridPoint> Candidate, string Reason) Create(
        IReadOnlyList<GridPoint> points, long delta, CancellationToken token = default)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!;
        var method = type.GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [points, delta, null, null, token];
        try
        {
            var accepted = (bool)method.Invoke(null, args)!;
            return (accepted, (IReadOnlyList<GridPoint>)args[2]!, (string)args[3]!);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AxisAlignedSquareMatchesIndependentExactMiterCoordinates(bool reverse)
    {
        var source = new List<GridPoint> { new(0,0), new(100,0), new(100,100), new(0,100) };
        if (reverse) source.Reverse();
        var before = source.ToArray();
        var result = Create(source, 10);
        Assert.True(result.Accepted, result.Reason);
        var expected = new[] { new GridPoint(-10,-10), new GridPoint(110,-10),
            new GridPoint(110,110), new GridPoint(-10,110) };
        if (reverse) Array.Reverse(expected);
        Assert.Equal(expected, result.Candidate);
        Assert.Equal(before, source);
        Assert.Contains("not certified", result.Reason);
    }

    [Fact]
    public void SelfIntersectionAndZeroAreaNeverProduceAnAcceptedCandidate()
    {
        var bowtie = new[] { new GridPoint(0,0), new GridPoint(100,100), new GridPoint(0,100), new GridPoint(100,0) };
        var flat = new[] { new GridPoint(0,0), new GridPoint(50,0), new GridPoint(100,0) };
        foreach (var input in new[] { bowtie, flat })
        {
            var before = input.ToArray();
            var result = Create(input, 10);
            Assert.False(result.Accepted);
            Assert.Empty(result.Candidate);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
            Assert.Equal(before, input);
        }
    }

    [Fact]
    public void ClosedNarrowRecessCannotSilentlyCollapse()
    {
        var source = new[] { new GridPoint(0,0), new GridPoint(100,0), new GridPoint(100,100),
            new GridPoint(52,100), new GridPoint(52,20), new GridPoint(48,20),
            new GridPoint(48,100), new GridPoint(0,100) };
        var result = Create(source, 5);
        Assert.False(result.Accepted);
        Assert.Empty(result.Candidate);
    }

    [Theory]
    [InlineData(1L, false)]
    [InlineData(1L, true)]
    [InlineData(2L, false)]
    [InlineData(2L, true)]
    public void OneOrTwoGridUnitNotchesCannotBeApprovedByTheExperimentalProof(long depth, bool reverse)
    {
        var source = new List<GridPoint> { new(0,0), new(10_000,0), new(10_000,10_000),
            new(6_000,10_000), new(6_000,10_000-depth), new(4_000,10_000-depth),
            new(4_000,10_000), new(0,10_000) };
        if (reverse) source.Reverse();
        var before = source.ToArray();
        var result = Create(source, 100);
        Assert.False(result.Accepted, "A notch smaller than the local uncertainty must not be certified.");
        Assert.Empty(result.Candidate);
        Assert.Equal(before, source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeRecessRetainsItsIndexedCornersAndDirections(bool reverse)
    {
        var source = new List<GridPoint> { new(0,0), new(10_000,0), new(10_000,10_000),
            new(6_000,10_000), new(6_000,9_000), new(4_000,9_000),
            new(4_000,10_000), new(0,10_000) };
        if (reverse) source.Reverse();
        var before = source.ToArray();
        var result = Create(source, 100);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(source.Count, result.Candidate.Count);
        var expected = new[] { new GridPoint(-100,-100), new GridPoint(10_100,-100),
            new GridPoint(10_100,10_100), new GridPoint(5_900,10_100), new GridPoint(5_900,9_100),
            new GridPoint(4_100,9_100), new GridPoint(4_100,10_100), new GridPoint(-100,10_100) };
        if (reverse) Array.Reverse(expected);
        Assert.Equal(expected, result.Candidate);
        Assert.Equal(before, source);
    }

    [Fact]
    public void InvalidDeltaAndCoordinateOverflowRejectRatherThanWrap()
    {
        var square = new[] { new GridPoint(0,0), new GridPoint(100,0), new GridPoint(100,100), new GridPoint(0,100) };
        Assert.False(Create(square, 0).Accepted);
        Assert.False(Create(square, -1).Accepted);
        var nearLimit = square.Select(p => new GridPoint(long.MaxValue - 100 + p.X, p.Y)).ToArray();
        var result = Create(nearLimit, 10);
        Assert.False(result.Accepted);
        Assert.Empty(result.Candidate);
    }

    [Theory]
    [InlineData(50_000_000_000_000L)]
    [InlineData(-50_000_000_000_000L)]
    public void LargeOriginsKeepExactSmallOffsets(long origin)
    {
        var source = new[] { new GridPoint(origin,origin), new GridPoint(origin+100,origin),
            new GridPoint(origin+100,origin+100), new GridPoint(origin,origin+100) };
        var result = Create(source, 10);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(new[] { new GridPoint(origin-10,origin-10), new GridPoint(origin+110,origin-10),
            new GridPoint(origin+110,origin+110), new GridPoint(origin-10,origin+110) }, result.Candidate);
    }

    [Theory]
    [InlineData(99_999_999_999_000L)]
    [InlineData(-99_999_999_999_000L)]
    public void NearDomainLimitStillRoundTripsExactly(long origin)
    {
        var source = new[] { new GridPoint(origin,origin), new GridPoint(origin+500,origin),
            new GridPoint(origin+500,origin+500), new GridPoint(origin,origin+500) };
        var result = Create(source, 100);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(new[] { new GridPoint(origin-100,origin-100), new GridPoint(origin+600,origin-100),
            new GridPoint(origin+600,origin+600), new GridPoint(origin-100,origin+600) }, result.Candidate);
    }

    [Theory]
    [InlineData(100_000_000_000_000L)]
    [InlineData(-100_000_000_000_000L)]
    [InlineData(9_007_199_254_740_992L)]
    [InlineData(9_007_199_254_740_993L)]
    public void OutOfDomainOrDoubleCollidingGridCoordinatesAreNotApproved(long origin)
    {
        // The last two adjacent integers have the same binary64 value. They
        // also exceed the project coordinate domain, so neither may return a candidate.
        var source = new[] { new GridPoint(origin,0), new GridPoint(origin+500,0),
            new GridPoint(origin+500,500), new GridPoint(origin,500) };
        var result = Create(source, 100);
        Assert.False(result.Accepted);
        Assert.Empty(result.Candidate);
    }

    [Fact]
    public void CancellationIsObservedBeforeAnyResultIsReturned()
    {
        var square = new[] { new GridPoint(0,0), new GridPoint(100,0), new GridPoint(100,100), new GridPoint(0,100) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Create(square, 10, cancellation.Token));
    }

    [Fact]
    public void GridRoundingRejectsAnIntervalCrossingEitherHalfUnitBoundary()
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!;
        var method = type.GetMethod("TryRound", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Round(Interval range, out long value)
        {
            object?[] args = [range, null];
            var result = (bool)method.Invoke(null, args)!;
            value = (long)args[1]!;
            return result;
        }
        var half = Scale / 2;
        Assert.False(Round(new Interval(half - 1, half + 1), out _));
        Assert.False(Round(new Interval(-half - 1, -half + 1), out _));
        Assert.True(Round(new Interval(half, half), out var positive));
        Assert.True(Round(new Interval(-half, -half), out var negative));
        Assert.Equal(1, positive);
        Assert.Equal(-1, negative);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseLobedCandidateEitherRetainsEveryIndexedVertexOrReportsUncertainty(bool reverse)
    {
        const double grid = 1_000_000;
        var points = Enumerable.Range(0, 512).Select(i =>
        {
            double angle = Math.Tau * i / 512;
            double radius = 1000 + 200 * Math.Cos(3 * angle);
            return new GridPoint((long)Math.Round(radius * Math.Cos(angle) * grid, MidpointRounding.AwayFromZero),
                (long)Math.Round(radius * Math.Sin(angle) * grid, MidpointRounding.AwayFromZero));
        }).ToList();
        if (reverse) points.Reverse();
        var before = points.ToArray();
        var result = Create(points, 1_000_000);
        if (result.Accepted)
        {
            Assert.Equal(points.Count, result.Candidate.Count);
            Assert.Equal(points.Count, result.Candidate.Distinct().Count());
        }
        else
        {
            Assert.Empty(result.Candidate);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        }
        Assert.Equal(before, points);
    }
}
