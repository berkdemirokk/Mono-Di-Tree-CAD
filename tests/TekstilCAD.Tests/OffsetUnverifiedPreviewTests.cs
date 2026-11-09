using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetUnverifiedPreviewTests
{
    [Fact]
    public void UnverifiedContourIsImmutablePreviewAndSourceIsUnchanged()
    {
        // A 4 mm wide, 80 mm deep recess is sealed by a 5 mm outward offset:
        // the local check cannot verify that the recess survived.
        var source=new PatternPiece { Name="Dar girintili kontur", Closed=true, Points=[new(0,0),new(100,0),new(100,100),new(52,100),
            new(52,20),new(48,20),new(48,100),new(0,100)] };
        var before=source.Points.ToArray();
        var failure=Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(source,5));
        Assert.False(failure.Preview.IsDefaultOrEmpty);
        var captured=failure.Preview.ToArray();
        Assert.All(captured,p=>{ Assert.True(double.IsFinite(p.X)); Assert.True(double.IsFinite(p.Y)); });
        source.Points[0]=new Point2(0,0);
        Assert.Equal(captured,failure.Preview.ToArray());
        Assert.Equal(before.Skip(1),source.Points.Skip(1));
        Assert.True(typeof(OffsetLocalDetailUnverifiedException).GetProperty(nameof(failure.Preview))!.SetMethod is null);
    }

    [Fact]
    public void ClosedNarrowRecessRemainsRejected()
    {
        var source=new PatternPiece { Points=[new(0,0),new(100,0),new(100,100),new(52,100),
            new(52,20),new(48,20),new(48,100),new(0,100)] };
        var before=source.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(source,5));
        Assert.Equal(before,source.Points);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffsetBeyondSupportedCoordinateRangeIsRejectedBeforePreview(bool reverse)
    {
        const double x = Geometry.MaxCoordinate;
        var source = new PatternPiece { Points = [new(x - 100, 0), new(x, 0), new(x, 100), new(x - 100, 100)] };
        if (reverse) source.Points.Reverse();
        var before = source.Points.ToArray();
        var failure = Assert.Throws<ArgumentException>(() => PatternTools.Offset(source, 1));
        Assert.Contains("koordinat sınırını", failure.Message);
        Assert.IsNotType<OffsetLocalDetailUnverifiedException>(failure);
        Assert.Equal(before, source.Points);
    }
}
