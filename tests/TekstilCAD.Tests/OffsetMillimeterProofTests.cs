using System.Reflection;
using System.Runtime.ExceptionServices;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Checks an experimental validator; it does not authorize production geometry.</summary>
public sealed class OffsetMillimeterProofTests
{
    private static Point2 Mm(GridPoint p) => new(p.X / 1_000_000.0, p.Y / 1_000_000.0);
    private static readonly GridPoint[] Source = [new(0,0), new(100,0), new(100,100), new(0,100)];
    private static readonly GridPoint[] Outer = [new(-10,-10), new(110,-10), new(110,110), new(-10,110)];

    private static (bool Accepted, string Reason) Validate(IReadOnlyList<GridPoint> source,
        IReadOnlyList<GridPoint> candidate, IReadOnlyList<Point2> sourceMm, IReadOnlyList<Point2> candidateMm,
        CancellationToken cancellationToken = default)
    {
        var type = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetMillimeterProof", true)!;
        var method = type.GetMethod("TryValidate", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] args = [source, candidate, sourceMm, candidateMm, null, cancellationToken];
        try { return ((bool)method.Invoke(null, args)!, (string)args[4]!); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static (bool Accepted, string Reason) Validate(GridPoint[] source, GridPoint[] candidate,
        CancellationToken token = default) => Validate(source, candidate, source.Select(Mm).ToArray(), candidate.Select(Mm).ToArray(), token);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AxisAlignedSquareHasMatchingOrientationAndStrictContainment(bool reverse)
    {
        var source = Source.ToArray(); var outer = Outer.ToArray();
        if (reverse) { Array.Reverse(source); Array.Reverse(outer); }
        var beforeSource = source.ToArray(); var beforeOuter = outer.ToArray();
        var result = Validate(source, outer);
        Assert.True(result.Accepted, result.Reason);
        Assert.Equal(beforeSource, source); Assert.Equal(beforeOuter, outer);
    }

    [Fact]
    public void MappingMustMatchTheGivenGridCoordinatesExactly()
    {
        var source = Source.Select(Mm).ToArray(); var candidate = Outer.Select(Mm).ToArray();
        candidate[1] = new Point2(double.BitIncrement(candidate[1].X), candidate[1].Y);
        Assert.False(Validate(Source, Outer, source, candidate).Accepted);
        candidate = Outer.Select(Mm).ToArray();
        candidate[1] = new Point2(double.NaN, candidate[1].Y);
        Assert.False(Validate(Source, Outer, source, candidate).Accepted);
    }

    [Fact]
    public void CandidateWithReversedOrientationIsRejected()
    {
        var reversed = Outer.Reverse().ToArray();
        Assert.False(Validate(Source, reversed).Accepted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CrossingTouchingOrNonContainingContoursAreRejected(int kind)
    {
        GridPoint[] candidate = kind switch
        {
            0 => [new(-10,-10), new(110,110), new(-10,110), new(110,-10)],
            1 => [new(0,0), new(110,0), new(110,110), new(0,110)],
            _ => [new(10,10), new(90,10), new(90,90), new(10,90)]
        };
        Assert.False(Validate(Source, candidate).Accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcavePolygonAndItsOuterBoundaryAreAcceptedWhenStrictlySeparated(bool reverse)
    {
        GridPoint[] source = [new(0,0), new(100,0), new(100,100), new(60,100), new(60,60), new(40,60), new(40,100), new(0,100)];
        GridPoint[] outer = [new(-5,-5), new(105,-5), new(105,105), new(55,105), new(55,65), new(45,65), new(45,105), new(-5,105)];
        var collapsed = outer.ToArray(); collapsed[5] = collapsed[4];
        if (reverse) { Array.Reverse(source); Array.Reverse(outer); Array.Reverse(collapsed); }
        Assert.False(Validate(source, collapsed).Accepted);
        var valid = Validate(source, outer);
        Assert.True(valid.Accepted, valid.Reason);
    }

    [Fact]
    public void NearLargeCoordinateMappingCannotHideChangedPointOrCrossing()
    {
        const long origin = 99_999_999_000_000;
        GridPoint[] source = [new(origin,origin), new(origin+100,origin), new(origin+100,origin+100), new(origin,origin+100)];
        GridPoint[] outer = [new(origin-20,origin-20), new(origin+120,origin-20), new(origin+120,origin+120), new(origin-20,origin+120)];
        var result = Validate(source, outer);
        Assert.True(result.Accepted, result.Reason);
        var mapped = outer.Select(Mm).ToArray();
        mapped[0] = mapped[1];
        Assert.False(Validate(source, outer, source.Select(Mm).ToArray(), mapped).Accepted);
    }

    [Fact]
    public void CancellationAndNullInputsAreNotReportedAsSuccessfulProofs()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Validate(Source, Outer, cts.Token));
        Assert.Throws<ArgumentNullException>(() => Validate(null!, Outer, [], []));
    }

    [Fact]
    public void OversizedWorkIsRejectedBeforeGeometryIsAccepted()
    {
        var source = Enumerable.Range(0, 1025).Select(i => new GridPoint(i, i * (long)i)).ToArray();
        var candidate = source.Select(p => new GridPoint(p.X + 1, p.Y + 1)).ToArray();
        var result = Validate(source, candidate);
        Assert.False(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
}
