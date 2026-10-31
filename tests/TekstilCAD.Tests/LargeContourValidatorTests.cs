using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class LargeContourValidatorTests
{
    private static List<Point2> Ring(int count, double origin = 0) => Enumerable.Range(0, count).Select(i =>
    {
        var a = Math.Tau * i / count;
        return new Point2(origin + 1000 * Math.Cos(a), origin + 1000 * Math.Sin(a));
    }).ToList();

    private static bool QuadraticOracleAccepts(IReadOnlyList<Point2> points)
    {
        try { Geometry.ValidateContour(points); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool LargeValidatorAccepts(IReadOnlyList<Point2> points)
    {
        try { LargeContourValidator.Validate(points); return true; }
        catch (ArgumentException) { return false; }
    }

    [Fact]
    public void SmallContoursAgreeWithExistingQuadraticOracle()
    {
        var cases = new List<List<Point2>>
        {
            new() { new(0,0), new(40,0), new(40,40), new(0,40) },
            new() { new(0,0), new(40,40), new(0,40), new(40,0) },
            new() { new(0,0), new(40,0), new(20,0), new(40,40), new(0,40) },
            new() { new(0,0), new(40,0), new(40,40), new(0,40), new(40,0) },
            new() { new(0,0), new(40,0), new(40,40), new(0,40), new(0,0) }
        };
        var random = new Random(4321);
        for (var n = 4; n <= 44; n += 4)
        {
            var ring = Ring(n, 50_000_000);
            cases.Add(ring);
            var reversed = ring.ToList(); reversed.Reverse(); cases.Add(reversed);
            var crossing = ring.ToList();
            var a = random.Next(1, n / 2); var b = random.Next(n / 2, n);
            (crossing[a], crossing[b]) = (crossing[b], crossing[a]);
            cases.Add(crossing);
        }
        foreach (var points in cases)
        {
            var snapshot = points.ToArray();
            Assert.Equal(QuadraticOracleAccepts(points), LargeValidatorAccepts(points));
            Assert.Equal(snapshot, points);
        }
    }

    [Theory]
    [InlineData(4097, 0, false)]
    [InlineData(4097, 50000000, true)]
    [InlineData(10000, 0, false)]
    [InlineData(10000, 50000000, true)]
    public void DenseSimpleContourIsAcceptedWithoutMutation(int count, double origin, bool reverse)
    {
        var points = Ring(count, origin);
        if (reverse) points.Reverse();
        var snapshot = points.ToArray();
        LargeContourValidator.Validate(points);
        Assert.Equal(snapshot, points);
    }

    [Fact]
    public void ExplicitClosureCountsTowardStoredPointLimit()
    {
        var accepted = Ring(9999); accepted.Add(accepted[0]);
        LargeContourValidator.Validate(accepted);
        var tooMany = Ring(10000); tooMany.Add(tooMany[0]);
        Assert.Throws<ArgumentException>(() => LargeContourValidator.Validate(tooMany));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargeCrossingAndRepeatedVertexAreRejected(bool reverse)
    {
        var points = Ring(4097, 50_000_000);
        if (reverse) points.Reverse();
        var repeated = points.ToList(); repeated[100] = repeated[3000];
        var crossing = points.ToList();
        (crossing[1000], crossing[3000]) = (crossing[3000], crossing[1000]);
        var before = crossing.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => LargeContourValidator.Validate(repeated));
        Assert.ThrowsAny<ArgumentException>(() => LargeContourValidator.Validate(crossing));
        Assert.Equal(before, crossing);
    }

    [Fact]
    public void InvalidCoordinateAndPrecancelledValidationAreRejectedWithoutMutation()
    {
        var points = Ring(4097);
        var snapshot = points.ToArray();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => LargeContourValidator.Validate(points, cancel.Token));
        Assert.Equal(snapshot, points);
        points[42] = new(double.NaN, 0);
        Assert.ThrowsAny<ArgumentException>(() => LargeContourValidator.Validate(points));
    }
}
