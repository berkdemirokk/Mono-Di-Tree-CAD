using TekstilCAD.Core;

namespace TekstilCAD.Tests;

/// <summary>
/// Checks the large-contour production path against the independent, full-pair
/// Geometry validator while inputs remain inside its 4096-vertex limit.
/// </summary>
public sealed class LargeContourProductionEquivalenceTests
{
    private static void Compare(IReadOnlyList<Point2> points)
    {
        var before = points.ToArray();
        var oracle = Record.Exception(() => Geometry.ValidateContour(points));
        var production = Record.Exception(() => LargeContourValidator.Validate(points));
        Assert.True((oracle is null) == (production is null),
            $"Oracle: {oracle?.GetType().Name ?? "accepted"}; production: {production?.GetType().Name ?? "accepted"}");
        if (oracle is not null) Assert.Equal(oracle.GetType(), production!.GetType());
        Assert.Equal(before, points);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50000000.0)]
    [InlineData(-99980000.0)]
    public void SeededSimpleAndCrossedContoursAgreeWithFullPairOracle(double origin)
    {
        for (var seed = 0; seed < 32; seed++)
        {
            var random = new Random(9841 + seed);
            var count = 65 + seed * 3;
            var contour = Enumerable.Range(0, count).Select(i =>
            {
                var angle = Math.Tau * i / count;
                var radius = 1200 + random.NextDouble() * 250;
                return new Point2(origin + radius * Math.Cos(angle), origin + radius * Math.Sin(angle));
            }).ToList();
            Compare(contour);
            var crossed = contour.ToList();
            (crossed[2], crossed[count / 2]) = (crossed[count / 2], crossed[2]);
            Compare(crossed);
            crossed.Reverse();
            Compare(crossed);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(50000000.0)]
    [InlineData(-99980000.0)]
    public void EpsilonNeighborsAndNearCollinearCrossingsAgree(double origin)
    {
        var edge = origin + Geometry.Epsilon;
        foreach (var y in new[] { Math.BitDecrement(edge), edge, Math.BitIncrement(edge),
                                  Math.BitDecrement(origin), origin, Math.BitIncrement(origin) })
        {
            List<Point2> contour = [new(origin, origin), new(origin+1000, origin),
                new(origin+1000, origin+100), new(origin+501, origin+100),
                new(origin+501, y), new(origin+499, y),
                new(origin+499, origin+100), new(origin, origin+100)];
            Compare(contour);
            contour.Reverse();
            contour.Add(contour[0]);
            Compare(contour);
            Compare(contour.Select(p => new Point2(p.Y, p.X)).ToArray());
        }
        var near = origin + 0.0001;
        foreach (var y in new[] { Math.BitDecrement(near), near, Math.BitIncrement(near) })
            Compare([new(origin,origin), new(origin+5000,origin+0.00005),
                new(origin+5000,y), new(origin+0.1,origin+0.00006),
                new(origin+0.1,origin+1), new(origin,origin+1)]);
    }

    [Fact]
    public void BoundarySizedContourAndRepeatedClosureAgree()
    {
        var points = Enumerable.Range(0, Geometry.MaxValidatedContourVertices).Select(i =>
        {
            var angle = Math.Tau * i / Geometry.MaxValidatedContourVertices;
            return new Point2(50_000_000 + 1000 * Math.Cos(angle), 50_000_000 + 1000 * Math.Sin(angle));
        }).ToList();
        Compare(points);
        points.Add(points[0]);
        Compare(points);
    }

    [Fact]
    public void CancellationDoesNotMutateSourceAndInputRemainsUsable()
    {
        List<Point2> points = [new(0,0), new(100,0), new(100,100), new(0,100)];
        var before = points.ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => LargeContourValidator.Validate(points, cancellation.Token));
        Assert.Equal(before, points);
        Compare(points);
    }
}
