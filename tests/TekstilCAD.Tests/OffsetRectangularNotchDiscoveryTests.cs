using System.Collections;
using System.Numerics;
using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetRectangularNotchDiscoveryTests
{
    private static (bool Ok, object[] Features, string Reason) Discover(GridPoint[] source)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetRectangularNotchDiscovery", true)!
            .GetMethod("TryDiscover", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, null, null, CancellationToken.None];
        bool ok = (bool)method.Invoke(null, args)!;
        return (ok, ((IEnumerable)args[1]!).Cast<object>().ToArray(), (string)args[2]!);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 5)]
    public void OneRectangleIsDiscoveredWithExactDimensions(bool reverse, int start)
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(70,100),
            new(70,60),new(30,60),new(30,100),new(0,100)];
        if (reverse) Array.Reverse(source);
        source = source.Skip(start).Concat(source.Take(start)).ToArray();
        var before = source.ToArray();
        var result = Discover(source);
        Assert.True(result.Ok, result.Reason);
        var feature = Assert.Single(result.Features);
        Assert.Equal(new BigInteger(1600), (BigInteger)feature.GetType().GetProperty("WidthSquared")!.GetValue(feature)!);
        Assert.Equal(new BigInteger(1600), (BigInteger)feature.GetType().GetProperty("DepthSquared")!.GetValue(feature)!);
        Assert.Equal(before, source);
    }

    [Fact]
    public void NestedNotchIsAmbiguousAndIsRejected()
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(80,100),new(80,60),
            new(65,60),new(65,50),new(55,50),new(55,60),new(20,60),new(20,100),new(0,100)];
        var result = Discover(source);
        Assert.False(result.Ok);
        Assert.Empty(result.Features);
    }

    [Fact]
    public void OverlappingAndSelfIntersectingContoursCannotBeAccepted()
    {
        GridPoint[] bowtie = [new(0,0),new(100,100),new(0,100),new(100,0)];
        GridPoint[] collinear = [new(0,0),new(50,0),new(100,0),new(100,100),new(0,100)];
        Assert.False(Discover(bowtie).Ok);
        Assert.False(Discover(collinear).Ok);
    }

    [Fact]
    public void DistinctRectangularNotchesAreEachReportedOnce()
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(80,100),new(80,70),new(70,70),
            new(70,100),new(50,100),new(50,80),new(40,80),new(40,100),new(0,100)];
        var result = Discover(source);
        Assert.True(result.Ok, result.Reason);
        Assert.Equal(2, result.Features.Length);
    }

    [Fact]
    public void RotatedTranslatedRectangleRetainsExactSquaredDimensions()
    {
        GridPoint[] original = [new(0,0),new(100,0),new(100,100),new(70,100),
            new(70,60),new(30,60),new(30,100),new(0,100)];
        const long origin = 50_000_000_000_000;
        var source = original.Select(p => new GridPoint(origin+3*p.X-4*p.Y, -origin+4*p.X+3*p.Y)).ToArray();
        var result = Discover(source);
        Assert.True(result.Ok, result.Reason);
        var feature = Assert.Single(result.Features);
        Assert.Equal(new BigInteger(40_000), (BigInteger)feature.GetType().GetProperty("WidthSquared")!.GetValue(feature)!);
        Assert.Equal(new BigInteger(40_000), (BigInteger)feature.GetType().GetProperty("DepthSquared")!.GetValue(feature)!);
    }

    [Fact]
    public void UnsupportedVRecessCannotBeReportedAsNoFeature()
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(70,100),
            new(50,80),new(30,100),new(0,100)];
        Assert.False(Discover(source).Ok);
    }

    [Fact]
    public void OneGridPocketDoesNotDisappearDuringDiscovery()
    {
        GridPoint[] source = [new(0,0),new(100,0),new(100,100),new(51,100),
            new(51,99),new(50,99),new(50,100),new(0,100)];
        var result = Discover(source);
        Assert.True(result.Ok, result.Reason);
        var feature = Assert.Single(result.Features);
        Assert.Equal(BigInteger.One, (BigInteger)feature.GetType().GetProperty("WidthSquared")!.GetValue(feature)!);
        Assert.Equal(BigInteger.One, (BigInteger)feature.GetType().GetProperty("DepthSquared")!.GetValue(feature)!);
    }

    [Fact]
    public void VertexBudgetAndCancellationFailClosed()
    {
        var oversized = Enumerable.Range(0, 708).Select(i => new GridPoint(i, (long)i*i)).ToArray();
        var result = Discover(oversized);
        Assert.False(result.Ok);
        Assert.Empty(result.Features);
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetRectangularNotchDiscovery", true)!
            .GetMethod("TryDiscover", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null,
            [new GridPoint[] { new(0,0),new(10,0),new(10,10),new(0,10) }, null, null, cancellation.Token]));
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
    }
}
