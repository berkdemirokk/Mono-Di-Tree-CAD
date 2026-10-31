using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class MirrorPrecisionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DistantAxisRejectsCollapsedDetail(bool vertical)
    {
        Point2[] points = vertical
            ? [new(0, 0), new(0.000000001, 0), new(0, 1)]
            : [new(0, 0), new(0, 0.000000001), new(1, 0)];

        Assert.Throws<ArgumentException>(() => Geometry.Mirror(points, vertical, 50_000_000));
        Assert.Equal(vertical ? new Point2(0.000000001, 0) : new Point2(0, 0.000000001), points[1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RepresentableMirrorPreservesShape(bool vertical)
    {
        Point2[] points = [new(0, 0), new(10, 0), new(10, 20)];
        var mirrored = Geometry.Mirror(points, vertical, 50_000_000);
        Assert.Equal(vertical ? new Point2(99_999_990, 20) : new Point2(10, 99_999_980), mirrored[2]);
    }
}
