using System.Reflection;
using TekstilCAD.Patterns;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

// Adversarial research checks only; passing them does not approve a production bypass.
public sealed class OffsetHomotopyAdversarialTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimpleSameWindingEndpointsCanCollapseAwayFromMidpoint(bool reverse)
    {
        GridPoint[] source = [new(-12, -6), new(12, -6), new(12, 6), new(-12, 6)];
        if (reverse) Array.Reverse(source);
        // A half-turn and scale produce another simple rectangle with the same winding.
        // At t = 1/3, (2 * source + candidate) / 3 is exactly the origin for
        // every vertex. A midpoint-only examination would miss this collapse.
        var candidate = source.Select(p => new GridPoint(-2 * p.X, -2 * p.Y)).ToArray();
        Assert.All(source.Zip(candidate), pair =>
        {
            Assert.Equal(0, 2 * pair.First.X + pair.Second.X);
            Assert.Equal(0, 2 * pair.First.Y + pair.Second.Y);
        });
        Assert.Equal(AreaSign(source), AreaSign(candidate));
        Assert.NotEqual(0, AreaSign(source));
        Assert.False(Validate("OffsetTurnHomotopyProof", source, candidate, CancellationToken.None));
        Assert.False(Validate("OffsetEdgeHomotopyProof", source, candidate, CancellationToken.None));
    }

    [Theory]
    [InlineData("OffsetTurnHomotopyProof")]
    [InlineData("OffsetEdgeHomotopyProof")]
    public void CancelledCertificateDoesNotReturnAcceptance(string proof)
    {
        GridPoint[] square = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var error = Assert.Throws<TargetInvocationException>(() => Validate(proof, square, square, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
    }

    private static long AreaSign(GridPoint[] points)
    {
        long area = 0;
        for (int i = 0; i < points.Length; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - a.Y * b.X;
        }
        return Math.Sign(area);
    }

    private static bool Validate(string proof, GridPoint[] source, GridPoint[] candidate, CancellationToken token)
    {
        var method = typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns." + proof, true)!
            .GetMethod("TryValidate", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] args = [source, candidate, null, token];
        return (bool)method.Invoke(null, args)!;
    }
}
