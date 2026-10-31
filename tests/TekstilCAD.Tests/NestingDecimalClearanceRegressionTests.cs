using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class NestingDecimalClearanceRegressionTests
{
    private static PatternPiece Rectangle(double x, double y, double width, double height) => new()
    {
        Name = "Dikdörtgen",
        Points = [new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)]
    };

    private static void AssertClearances(NestingResult result, double margin, double gap)
    {
        var boxes = result.Layout.Pieces.Select(p => Geometry.Bounds(p.Points)).ToArray();
        foreach (var b in boxes)
        {
            Assert.True(b.MinX >= margin && result.FabricWidth - b.MaxX >= margin);
            Assert.True(b.MinY >= margin && result.UsedLength - b.MaxY >= margin);
        }
        for (var i = 0; i < boxes.Length; i++)
        for (var j = i + 1; j < boxes.Length; j++)
        {
            var a = boxes[i]; var b = boxes[j];
            Assert.True(b.MinX - a.MaxX >= gap || a.MinX - b.MaxX >= gap ||
                        b.MinY - a.MaxY >= gap || a.MinY - b.MaxY >= gap,
                $"Boxes {i} and {j} overlap or violate the requested clearance.");
        }
    }

    [Theory]
    [InlineData(0.1, 0.2)]
    [InlineData(0.2, 0.1)]
    [InlineData(0.5, 0.25)]
    [InlineData(1.5, 2.5)]
    public void CommonDecimalMarginsAndGapsAreAcceptedAndActuallyRespected(double margin, double gap)
    {
        var pieces = Enumerable.Range(0, 3).Select(_ => Rectangle(0, 0, 10, 20)).ToArray();
        var before = pieces.Select(p => p.Points.ToArray()).ToArray();
        var result = NestingEngine.Arrange(pieces, 100, gap: gap, edgeMargin: margin);
        AssertClearances(result, margin, gap);
        for (var i = 0; i < pieces.Length; i++) Assert.Equal(before[i], pieces[i].Points);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    public void ExactFitAtBothFabricEdgesIsAccepted(double margin)
    {
        var result = NestingEngine.Arrange([Rectangle(0, 0, 10, 20)], 10 + 2 * margin,
            gap: 0, maxLength: 20 + 2 * margin, edgeMargin: margin);
        AssertClearances(result, margin, 0);
        Assert.Equal(20 + 2 * margin, result.UsedLength);
    }

    [Theory]
    [InlineData(10000000.0)]
    [InlineData(-10000000.0)]
    public void FarSourceOriginDoesNotChangeRowClearances(double origin)
    {
        var pieces = Enumerable.Range(0, 3).Select(_ => Rectangle(origin, origin, 10, 20)).ToArray();
        var result = NestingEngine.Arrange(pieces, 21, gap: .5, edgeMargin: .25);
        AssertClearances(result, .25, .5);
        Assert.True(result.UsedLength > 40);
        Assert.All(pieces, p => Assert.Equal(new Point2(origin, origin), p.Points[0]));
    }
}
