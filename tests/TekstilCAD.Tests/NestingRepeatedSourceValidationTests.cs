using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class NestingRepeatedSourceValidationTests
{
    private static PatternPiece Circle(int count, int quantity) => new()
    {
        Name = "Kalıp", Closed = true,
        Points = Enumerable.Range(0, count).Select(i =>
            new Point2(10 * Math.Cos(2 * Math.PI * i / count), 10 * Math.Sin(2 * Math.PI * i / count))).ToList(),
        Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = quantity }
    };

    [Fact]
    public void ManyIdenticalCopiesShareSourceValidationButRemainIndependent()
    {
        var source = Circle(512, 96);
        var document = new ProjectDocument { Pieces = [source] };
        var result = ProductionNestingPlan.Arrange(document, [source.Id], 2000, 1);
        Assert.Equal(96, result.Nesting.Layout.Pieces.Count);
        var first = result.Nesting.Layout.Pieces[0];
        var second = result.Nesting.Layout.Pieces[1];
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(Geometry.HasSelfIntersections(first.Points, true));
        Assert.False(Geometry.HasSelfIntersections(second.Points, true));
        var original = source.Points[0];
        first.Points[0] = new Point2(0, 0);
        Assert.Equal(original, source.Points[0]);
        Assert.NotEqual(first.Points[0], second.Points[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrthogonalPlanningSharesValidationForRepeatedCopies(bool perSourceAngles)
    {
        var source = Circle(512, 96);
        source.Grainline = new() { Start = new(0, 0), End = new(0, 1) };
        var document = new ProjectDocument { Pieces = [source] };
        var angles = new Dictionary<Guid, IReadOnlyList<int>> { [source.Id] = [0] };
        var result = perSourceAngles
            ? OrthogonalGrainNesting.ArrangeWithPieceRotations(document, [source.Id], 2000, 1,
                double.PositiveInfinity, 0, angles, GrainDirectionPolicy.Direction.OneWay, 0)
            : OrthogonalGrainNesting.Arrange(document, [source.Id], 2000, 1,
                double.PositiveInfinity, 0, [0], GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.Equal(96, result.Plan.Nesting.Layout.Pieces.Count);
        Assert.Equal(0, result.SourceRotations[source.Id]);
        Assert.Equal(96, result.Plan.Nesting.Layout.Pieces.Select(p => p.Id).Distinct().Count());
        ShelfLayoutVerifier.Verify(result.Plan.Nesting);
    }

    [Fact]
    public void OrthogonalPlanningStillRejectsExcessivePerCopyValidation()
    {
        var source = Circle(1000, 50);
        source.Grainline = new() { Start = new(0, 0), End = new(0, 1) };
        var error = Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.Arrange(
            new ProjectDocument { Pieces = [source] }, [source.Id], 2000, 1,
            double.PositiveInfinity, 0, [0], GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.Contains("sınır", error.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void InvalidSourceCannotBeAcceptedThroughSharedValidation()
    {
        var source = Circle(16, 3);
        // A bow-tie with nonzero signed area: area alone must not admit it.
        source.Points = [new(0,0), new(4,4), new(0,4), new(3,0)];
        Assert.True(Geometry.HasSelfIntersections(source.Points, true));
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(new ProjectDocument { Pieces = [source] }, [source.Id], 100, 0));
    }

    [Fact]
    public void UniqueSourceContoursAreChargedSeparately()
    {
        var pieces = Enumerable.Range(0, 7).Select(i =>
        {
            var piece = Circle(2000, 1);
            piece.Points[0] = new Point2(10 + i * 0.01, 0);
            return piece;
        }).ToList();
        var error = Assert.Throws<ArgumentException>(() => NestingEngine.Arrange(pieces, 1000, 0));
        Assert.Contains("sınır", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreCancelledRepeatedCopyJobLeavesSourceUntouched()
    {
        var source = Circle(512, 96);
        var before = source.Points.ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProductionNestingPlan.Arrange(
            new ProjectDocument { Pieces = [source] }, [source.Id], 2000, 1, double.PositiveInfinity, 0,
            cancellationToken: cancellation.Token));
        Assert.Equal(before, source.Points);
    }
}

