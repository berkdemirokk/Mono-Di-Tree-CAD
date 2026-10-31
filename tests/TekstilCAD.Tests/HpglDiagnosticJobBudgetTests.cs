using TekstilCAD.FileFormats;
using P = TekstilCAD.FileFormats.HpglIntersectionDiagnostic.Point;
using Path = TekstilCAD.FileFormats.HpglIntersectionDiagnostic.Path;

namespace TekstilCAD.Tests;

public class HpglDiagnosticJobBudgetTests
{
    [Fact]
    public void BudgetCountsPairsAcrossPathsWithoutChangingInputs()
    {
        var first = new[] { new P(0, 0), new P(10, 0), new P(10, 10) };
        var second = new[] { new P(20, 0), new P(30, 0), new P(30, 10) };
        var job = new[] { new Path(first, true), new Path(second, true) };
        var original = first.ToArray();
        Assert.Throws<InvalidDataException>(() => HpglIntersectionDiagnostic.ValidateJob(job, 5));
        Assert.Equal(original, first);
        var result = HpglIntersectionDiagnostic.ValidateJob(job, 6);
        Assert.Equal(6, result.PairCount);
    }

    [Fact]
    public void PrecancelledJobDoesNotMutateInput()
    {
        var points = new[] { new P(0, 0), new P(10, 0), new P(10, 10) };
        var original = points.ToArray();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => HpglIntersectionDiagnostic.ValidateJob(
            [new Path(points, true)], cancellationToken: cancel.Token));
        Assert.Equal(original, points);
    }
}
