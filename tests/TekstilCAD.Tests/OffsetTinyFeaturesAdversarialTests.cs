using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Clipper2Lib;

namespace TekstilCAD.Tests;

public sealed class OffsetTinyFeaturesAdversarialTests
{
    [Theory]
    [InlineData(0d, false, 1d, 0.000001d)]
    [InlineData(0d, true, 5d, 0.000005d)]
    [InlineData(50_000_000d, false, 5d, 0.000001d)]
    [InlineData(50_000_000d, true, 1d, 0.000005d)]
    public void ShallowWideRecessRetainedByInverseOffsetIsAccepted(double origin, bool reverse, double width, double depth)
    {
        // A shallow V has long incident edges and gentle turns, so a guard based
        // only on short edges at sharp concave corners will not protect it.
        var piece = new PatternPiece { Name = "Sığ girinti", Closed = true, Points =
        [
            new(origin, origin), new(origin + 100, origin), new(origin + 100, origin + 100),
            new(origin + 50 + width / 2, origin + 100),
            new(origin + 50, origin + 100 - depth),
            new(origin + 50 - width / 2, origin + 100), new(origin, origin + 100)
        ] };
        if (reverse) piece.Points.Reverse();
        var before = piece.Points.ToArray();
        Geometry.ValidateContour(before); // Invalid input must not create a false pass.
        const double scale = 1_000_000;
        var source = new Path64(before.Select(p => new Point64(
            checked((long)Math.Round(p.X * scale, MidpointRounding.AwayFromZero)),
            checked((long)Math.Round(p.Y * scale, MidpointRounding.AwayFromZero)))));
        var forward = Assert.Single(Clipper.InflatePaths(new Paths64 { source }, scale, JoinType.Miter, EndType.Polygon, 2));
        var restored = Assert.Single(Clipper.InflatePaths(new Paths64 { forward }, -scale, JoinType.Miter, EndType.Polygon, 2));
        var notch = source.Single(p => p.X == checked((long)Math.Round((origin + 50) * scale, MidpointRounding.AwayFromZero)));
        double nearest = double.PositiveInfinity;
        for (var i = 0; i < restored.Count; i++)
        {
            var a = restored[i]; var b = restored[(i + 1) % restored.Count];
            double ax = a.X - notch.X, ay = a.Y - notch.Y, dx = b.X - a.X, dy = b.Y - a.Y;
            var denominator = dx * dx + dy * dy;
            var t = denominator == 0 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / denominator, 0, 1);
            nearest = Math.Min(nearest, Math.Sqrt(Math.Pow(ax + t * dx, 2) + Math.Pow(ay + t * dy, 2)));
        }
        // Establish that this particular feature survives the integer round trip;
        // a shallow feature by itself is not sufficient evidence of loss.
        Assert.True(nearest <= 0.5, $"Reverse offset displaced the notch by {nearest:G9} grid units.");
        var result = PatternTools.Offset(piece, 1);
        Geometry.ValidateContour(result.Points);
        Assert.Equal(before, piece.Points);
    }
}
