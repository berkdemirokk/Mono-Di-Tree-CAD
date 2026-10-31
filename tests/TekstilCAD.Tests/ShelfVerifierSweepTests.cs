using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class ShelfVerifierSweepTests
{
    private static PatternPiece Box(double x, double y, double width = 1, double height = 1) => new()
    {
        Points = [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)]
    };

    private static NestingResult Layout(IEnumerable<PatternPiece> pieces, double gap = 0) =>
        new(new ProjectDocument { Pieces = pieces.ToList() }, 20000, 0, 0)
        { FabricWidth = 20000, Gap = gap, EdgeMargin = 0 };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AcceptsTenThousandTouchingBoxesOnEitherAxis(bool horizontal)
    {
        var boxes = Enumerable.Range(0, 10000)
            .Select(i => Box(horizontal ? i : 0, horizontal ? 0 : i));
        ShelfLayoutVerifier.Verify(Layout(boxes));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsOverlapWithNeighborOrDistantInputIndex(bool distant)
    {
        var boxes = Enumerable.Range(0, 10000).Select(i => Box(i * 2, 0)).ToArray();
        boxes[distant ? 9999 : 1] = Box(0.5, 0);
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout(boxes)));
    }

    [Fact]
    public void HonorsExactGapAndSubnormalBoundaries()
    {
        ShelfLayoutVerifier.Verify(Layout([Box(0, 0), Box(2, 0)], 1));
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout([Box(0, 0), Box(Math.BitDecrement(2), 0)], 1)));
        var tiny = double.Epsilon;
        ShelfLayoutVerifier.Verify(Layout([Box(0, 0, tiny, 1), Box(2 * tiny, 0, tiny, 1)], tiny));
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout([Box(0, 0, tiny, 1), Box(tiny, 0, tiny, 1)], tiny)));
        Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout([Box(-tiny, 0)])));
    }

    [Fact]
    public void RejectsDenseCandidateBudgetAndHonorsCancellation()
    {
        var boxes = Enumerable.Range(0, 2001).Select(_ => Box(0, 0)).ToArray();
        var error = Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(Layout(boxes)));
        Assert.Contains("resource limit", error.Message);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ShelfLayoutVerifier.Verify(Layout(boxes), cancellation.Token));
    }

    [Fact]
    public void MatchesBruteForceForIntegerBoxes()
    {
        var random = new Random(27419);
        for (int trial = 0; trial < 200; trial++)
        {
            var rectangles = Enumerable.Range(0, 30).Select(_ =>
                (X: random.Next(30), Y: random.Next(30), W: random.Next(1, 4), H: random.Next(1, 4))).ToArray();
            int gap = random.Next(3);
            bool valid = true;
            for (int i = 0; i < rectangles.Length; i++)
                for (int j = i + 1; j < rectangles.Length; j++)
                {
                    var a = rectangles[i]; var b = rectangles[j];
                    if (!(b.X - (a.X + a.W) >= gap || a.X - (b.X + b.W) >= gap ||
                          b.Y - (a.Y + a.H) >= gap || a.Y - (b.Y + b.H) >= gap)) valid = false;
                }
            var layout = Layout(rectangles.Select(r => Box(r.X, r.Y, r.W, r.H)), gap);
            if (valid) ShelfLayoutVerifier.Verify(layout);
            else Assert.Throws<InvalidOperationException>(() => ShelfLayoutVerifier.Verify(layout));
        }
    }
}
