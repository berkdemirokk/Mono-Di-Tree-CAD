using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class AlbertApronStrapTests
{
    // FreeSewing Albert, designs/albert/src/strap.mjs at 8a8de5a271d9823bb393735a41dd3993cf5bcf4b.
    // Albert doubles the finished width and adds that doubled width to the length.
    [Fact]
    public void TenMillimetreAllowanceMatchesPublishedStrapCutDimensions()
    {
        var seam = new PatternPiece { Points = [new(0, 0), new(50, 0), new(50, 650), new(0, 650)] };
        var cut = PatternTools.OffsetConvex(seam, 10);
        var bounds = Geometry.Bounds(cut.Points);
        Assert.Equal(70, bounds.Width, 6);
        Assert.Equal(670, bounds.Height, 6);
        foreach (var corner in new Point2[] { new(-10, -10), new(60, -10), new(60, 660), new(-10, 660) })
            Assert.Contains(cut.Points, p => Math.Abs(p.X - corner.X) < 0.01 && Math.Abs(p.Y - corner.Y) < 0.01);
    }
}
