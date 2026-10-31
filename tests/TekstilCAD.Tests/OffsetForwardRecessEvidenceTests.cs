using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class OffsetForwardRecessEvidenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardOffsetFillsARealNarrowRecessAndProductionRejectsIt(bool reverse)
    {
        // The recess is 4 mm wide. Offsetting each vertical wall outward by
        // 3 mm crosses the centerline, so (50,50) must become interior.
        const long g = 1_000_000;
        Point64 P(long x, long y) => new(x * g, y * g);
        var path = new Path64 { P(0,0), P(100,0), P(100,100), P(52,100),
            P(52,20), P(48,20), P(48,100), P(0,100) };
        if (reverse) path.Reverse();
        var probe = P(50,50);
        Assert.Equal(PointInPolygonResult.IsOutside, Clipper.PointInPolygon(probe, path));
        var forward = Assert.Single(Clipper.InflatePaths(new Paths64 { path }, 3 * g,
            JoinType.Miter, EndType.Polygon, 2.0));
        Assert.Equal(PointInPolygonResult.IsInside, Clipper.PointInPolygon(probe, forward));
        var piece = new PatternPiece { Points = path.Select(p => new Point2(p.X / (double)g, p.Y / (double)g)).ToList() };
        Assert.ThrowsAny<ArgumentException>(() => PatternTools.Offset(piece, 3));
    }
}
