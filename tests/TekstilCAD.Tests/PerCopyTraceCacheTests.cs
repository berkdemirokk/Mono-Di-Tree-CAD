using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class PerCopyTraceCacheTests
{
    [Fact]
    public void RepeatedSourceAndAngleVerifyBeyondOldFourPerCopyBudget()
    {
        const int vertices = 400, copies = 80;
        var points = Enumerable.Range(0, vertices).Select(i =>
        {
            double angle = 2 * Math.PI * i / vertices;
            return new Point2(10 * Math.Cos(angle), 10 * Math.Sin(angle));
        }).ToList();
        var piece = new PatternPiece { Points = points, Closed = true, Grainline = new() { Start = new(0, 0), End = new(0, 1) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = copies } };
        var source = new ProjectDocument { Pieces = [piece] };
        var angles = Enumerable.Range(1, copies).ToDictionary(i => (piece.Id, i), _ => 0);
        var result = PerCopyOrthogonalNesting.Arrange(source, [piece.Id], angles, 200, 0, 1000, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);

        bool Verify() => PerCopyTraceVerifier.IsValidForConstraints(source, [piece.Id], result, angles,
            GrainDirectionPolicy.Direction.TwoWay, 90, 200, 0, 0, 1000);
        Assert.True(4L * copies * vertices * vertices > NestingEngine.MaximumValidationWork);
        Assert.True(Verify());

        // Even with a matching fingerprint, one malformed copy must not inherit another's validation.
        var copy = result.Plan.Nesting.Layout.Pieces[1];
        copy.Points[1] = new Point2(copy.Points[1].X + 1, copy.Points[1].Y);
        result = result with { LayoutFingerprint = NestingSnapshot.ComputeLayoutFingerprint(result.Plan.Nesting.Layout) };
        Assert.False(Verify());
    }

    [Fact]
    public void EachCopyStillRequiresItsOwnAngleAndMapping()
    {
        var piece = new PatternPiece { Points = [new(0, 0), new(6, 0), new(6, 4), new(0, 4)], Closed = true, Grainline = new() { Start = new(1, 1), End = new(1, 2) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 3 } };
        var source = new ProjectDocument { Pieces = [piece] };
        var angles = new Dictionary<(Guid, int), int> { [(piece.Id, 1)] = 0, [(piece.Id, 2)] = 90, [(piece.Id, 3)] = 0 };
        var result = PerCopyOrthogonalNesting.Arrange(source, [piece.Id], angles, 20, 0, 100, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        bool Verify() => PerCopyTraceVerifier.IsValidForConstraints(source, [piece.Id], result, angles,
            GrainDirectionPolicy.Direction.TwoWay, 90, 20, 0, 0, 100);
        Assert.True(Verify());
        angles[(piece.Id, 3)] = 90;
        Assert.False(Verify());
    }
}
