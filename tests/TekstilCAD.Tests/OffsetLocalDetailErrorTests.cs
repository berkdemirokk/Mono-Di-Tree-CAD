using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class OffsetLocalDetailErrorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnverifiedLocalDetailHasSpecificArgumentExceptionWithoutChangingSource(bool reverse)
    {
        var source = new PatternPiece
        {
            Name = "Loblu kaynak", Closed = true,
            Points = Enumerable.Range(0, 512).Select(i =>
            {
                double angle = Math.Tau * i / 512;
                double radius = 1000 + 200 * Math.Cos(3 * angle);
                return new Point2(radius * Math.Cos(angle), radius * Math.Sin(angle));
            }).ToList(),
            Grainline = new Grainline { Start = new(0, 0), End = new(0, 20), NeedsReview = false }
        };
        if (reverse) source.Points.Reverse();
        var before = new ProjectDocument { Pieces = [source.Clone()] };
        var current = new ProjectDocument { Pieces = [source] };

        var error = Assert.Throws<OffsetLocalDetailUnverifiedException>(() => PatternTools.Offset(source, 1));

        Assert.IsAssignableFrom<ArgumentException>(error);
        Assert.True(DocumentSession.ContentEquals(before, current));
    }

    [Fact]
    public void InvalidAmountAndOpenContourAreNotMisreportedAsLocalDetailFailure()
    {
        var open = new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0), new(10, 10)] };
        var openError = Assert.Throws<ArgumentException>(() => PatternTools.Offset(open, 1));
        Assert.IsNotType<OffsetLocalDetailUnverifiedException>(openError);
        var amountError = Assert.Throws<ArgumentOutOfRangeException>(() => PatternTools.Offset(open, -1));
        Assert.IsNotType<OffsetLocalDetailUnverifiedException>(amountError);
    }
}
