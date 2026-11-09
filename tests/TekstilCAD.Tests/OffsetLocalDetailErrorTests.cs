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
            Name = "Dar girintili kaynak", Closed = true,
            // 4 mm wide, 80 mm deep recess: a 5 mm allowance seals it, so the
            // local detail check cannot verify preservation.
            Points = [new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)],
            Grainline = new Grainline { Start = new(0, 0), End = new(0, 20), NeedsReview = false }
        };
        if (reverse) source.Points.Reverse();
        var before = new ProjectDocument { Pieces = [source.Clone()] };
        var current = new ProjectDocument { Pieces = [source] };

        var error = Assert.Throws<OffsetLocalDetailUnverifiedException>(() => PatternTools.Offset(source, 5));

        Assert.IsAssignableFrom<ArgumentException>(error);
        Assert.Equal(error.SourceIndex.HasValue,error.Sample.HasValue);
        if(error.SourceIndex is int index)Assert.InRange(index,0,source.Points.Count-1);
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
