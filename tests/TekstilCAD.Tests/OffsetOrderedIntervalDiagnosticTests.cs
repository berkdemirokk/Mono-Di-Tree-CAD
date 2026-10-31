using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

public sealed class OffsetOrderedIntervalDiagnosticTests(ITestOutputHelper log)
{
    const long Grid = 1_000_000;
    static GridPoint P(long x, long y) => new(x * Grid, y * Grid);
    static GridPoint[] Shift(GridPoint[] path, int start) => path.Skip(start).Concat(path.Take(start)).ToArray();
    static readonly GridPoint[] Square = [P(0,0), P(100,0), P(100,100), P(0,100)];

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SubdivisionAndCyclicStartCoverTheSameDirectedBoundary(bool reverse)
    {
        GridPoint[] split = [P(0,0), P(50,0), P(100,0), P(100,100), P(0,100)];
        var reference = Square.ToArray();
        if (reverse) { Array.Reverse(reference); Array.Reverse(split); }
        var result = OffsetOrderedIntervalDiagnostic.Compare(reference, Shift(split, 2), 0);
        Assert.True(result.Compared);
        Assert.Equal(0, result.ReferenceUncovered, 6); Assert.Equal(0, result.ObservedUncovered, 6);
        Assert.True(result.ReferenceOrderPreserved); Assert.True(result.ObservedOrderPreserved);
    }

    [Fact]
    public void ReversedOrBacktrackingTraversalIsNotAnOrderedDirectedMatch()
    {
        var reversed = Square.Reverse().ToArray();
        var result = OffsetOrderedIntervalDiagnostic.Compare(Square, reversed, 0);
        Assert.True(result.Compared);
        Assert.True(result.ReferenceUncovered > 0);
        GridPoint[] backtrack = [P(0,0), P(75,0), P(25,0), P(100,0), P(100,100), P(0,100)];
        var second = OffsetOrderedIntervalDiagnostic.Compare(Square, backtrack, 0);
        Assert.True(second.ObservedUncovered > 0 || !second.ObservedOrderPreserved);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ClosedRecessLeavesUncoveredIntervalsEvenWhenMostOfThePerimeterMatches(bool reverse)
    {
        GridPoint[] expected = [P(-5,-5),P(105,-5),P(105,105),P(57,105),P(57,25),P(43,25),P(43,105),P(-5,105)];
        GridPoint[] flattened = [P(-5,-5),P(105,-5),P(105,105),P(-5,105)];
        if (reverse) { Array.Reverse(expected); Array.Reverse(flattened); }
        var result = OffsetOrderedIntervalDiagnostic.Compare(expected, flattened, 8);
        Assert.True(result.Compared);
        Assert.True(result.ReferenceUncovered > 100 * Grid);
        Assert.True(result.ObservedUncovered > 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActualForwardOffsetOfNarrowRecessDiffersFromUntrimmedMiterEdges(bool reverse)
    {
        GridPoint[] source = [P(0,0),P(100,0),P(100,100),P(52,100),P(52,20),P(48,20),P(48,100),P(0,100)];
        // Intersections of the shifted incident lines cross in the recess. This
        // untrimmed path is deliberately not asserted to be a valid boundary.
        GridPoint[] untrimmed = [P(-5,-5),P(105,-5),P(105,105),P(47,105),P(47,25),P(53,25),P(53,105),P(-5,105)];
        if (reverse) { Array.Reverse(source); Array.Reverse(untrimmed); }
        var path = new Path64(source.Select(p => new Point64(p.X,p.Y)));
        var actual = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, 5 * Grid, JoinType.Miter, EndType.Polygon, 2))
            .Select(p => new GridPoint(p.X,p.Y)).ToArray();
        var result = OffsetOrderedIntervalDiagnostic.Compare(untrimmed, actual, 8);
        Assert.True(result.Compared);
        Assert.True(result.ReferenceUncovered > 100 * Grid);
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.Offset(new PatternPiece {
            Closed = true, Points = source.Select(p => new TekstilCAD.Core.Point2((double)p.X / Grid, (double)p.Y / Grid)).ToList()
        }, 5));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LobedCandidateAndActualForwardBoundaryProduceReportedMeasurements(bool reverse)
    {
        var source = Enumerable.Range(0, 512).Select(i => {
            double a = Math.Tau * i / 512, r = 1000 + 200 * Math.Cos(3 * a);
            return new GridPoint((long)Math.Round(r * Math.Cos(a) * Grid, MidpointRounding.AwayFromZero),
                (long)Math.Round(r * Math.Sin(a) * Grid, MidpointRounding.AwayFromZero));
        }).ToArray();
        if (reverse) Array.Reverse(source);
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof", true)!
            .GetMethod("TryCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, Grid, null, null, CancellationToken.None];
        Assert.True((bool)method.Invoke(null, args)!, (string?)args[3]);
        var candidate = Assert.IsAssignableFrom<IReadOnlyList<GridPoint>>(args[2]);
        var path = new Path64(source.Select(p => new Point64(p.X,p.Y)));
        var actual = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, Grid, JoinType.Miter, EndType.Polygon, 2))
            .Select(p => new GridPoint(p.X,p.Y)).ToArray();
        var comparison = OffsetOrderedIntervalDiagnostic.Compare(candidate, actual, 8);
        Assert.True(comparison.Compared);
        Assert.True(double.IsFinite(comparison.ReferenceUncovered));
        Assert.True(double.IsFinite(comparison.ObservedUncovered));
        log.WriteLine($"reverse={reverse}; candidate unmatched={comparison.ReferenceUncovered:G17}; forward unmatched={comparison.ObservedUncovered:G17}; candidate order={comparison.ReferenceOrderPreserved}; forward order={comparison.ObservedOrderPreserved}");
    }

    [Fact]
    public void TinyFeatureCanBeHiddenByToleranceSoMeasurementsAreNotACertificate()
    {
        GridPoint[] tiny = [new(0,0),new(1000,0),new(1000,1000),new(501,1000),new(500,999),new(499,1000),new(0,1000)];
        GridPoint[] flat = [new(0,0),new(1000,0),new(1000,1000),new(0,1000)];
        var result = OffsetOrderedIntervalDiagnostic.Compare(tiny, flat, 2);
        Assert.True(result.Compared);
        Assert.Contains("do not certify", result.Reason);
    }
}


