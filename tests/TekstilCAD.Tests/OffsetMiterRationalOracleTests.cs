using System.Numerics;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>
/// Exact rational oracle for corners with integer edge lengths. It derives the
/// miter by intersecting two offset line equations, independently of the
/// normalized-vector interval implementation. It tests arithmetic enclosure,
/// not feature retention, Clipper output, or production safety.
/// </summary>
public sealed class OffsetMiterRationalOracleTests
{
    [Fact]
    public void PythagoreanCornersEncloseExactLineIntersections()
    {
        // Each vector has an integer Euclidean length. Include positive and
        // negative coordinates, both orientations, and a large translation.
        (long X, long Y, long Length)[] vectors = [
            (3,4,5), (4,-3,5), (-3,4,5), (-4,-3,5),
            (5,12,13), (-12,5,13), (12,-5,13), (-5,-12,13),
            (7,24,25), (-24,7,25), (24,-7,25), (-7,-24,25)
        ];
        var tested = 0;
        foreach (var u in vectors)
        foreach (var v in vectors)
        foreach (var orientation in new[] { -1, 1 })
        foreach (var origin in new[] { new GridPoint(0, 0), new GridPoint(1_000_000_000_000, -1_000_000_000_000) })
        {
            var determinant = (BigInteger)u.X * v.Y - (BigInteger)u.Y * v.X;
            if (determinant.IsZero) continue;
            const long delta = 1_000_000;
            var a = new GridPoint(origin.X - u.X, origin.Y - u.Y);
            var c = new GridPoint(origin.X + v.X, origin.Y + v.Y);

            // cross(u, displacement) = -orientation * delta * |u|,
            // and the analogous equation for v. Cramer's rule yields exact
            // rational coordinates, with no square roots or rounding.
            BigInteger k1 = -(BigInteger)orientation * delta * u.Length;
            BigInteger k2 = -(BigInteger)orientation * delta * v.Length;
            var xNumerator = (BigInteger)origin.X * determinant + k1 * v.X - (BigInteger)u.X * k2;
            var yNumerator = (BigInteger)origin.Y * determinant - (BigInteger)u.Y * k2 + k1 * v.Y;
            if (determinant.Sign < 0)
            { determinant = -determinant; xNumerator = -xNumerator; yNumerator = -yNumerator; }

            Assert.True(TryBound(a, origin, c, delta, orientation, out var box));
            Assert.True(box.X.Lower * determinant <= xNumerator * Scale && xNumerator * Scale <= box.X.Upper * determinant,
                $"X enclosure failed for {u}, {v}, orientation {orientation}, origin {origin}.");
            Assert.True(box.Y.Lower * determinant <= yNumerator * Scale && yNumerator * Scale <= box.Y.Upper * determinant,
                $"Y enclosure failed for {u}, {v}, orientation {orientation}, origin {origin}.");
            tested++;
        }
        Assert.True(tested > 400);
    }
}
