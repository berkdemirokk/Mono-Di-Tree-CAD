using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class AutomaticShortShelfTests
{
    [Fact]
    public void ShorterShelfOrientationsRecoverMarkerWithinLengthLimit()
    {
        PatternPiece Rectangle(int width, int height) => new()
        {
            Points = [new(0, 0), new(width, 0), new(width, height), new(0, height)],
            Grainline = new() { Start = new(0, 0), End = new(0, 10) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var source = new ProjectDocument { Pieces = [Rectangle(500, 500), Rectangle(200, 600),
            Rectangle(700, 200), Rectangle(500, 300), Rectangle(800, 200)] };
        var ids = source.Pieces.Select(p => p.Id).ToArray();
        var angles = ids.ToDictionary(id => id, _ => (IReadOnlyList<int>)[0, 90]);

        var result = AutomaticPerCopyNesting.Arrange(source, ids, angles, 1000, 0, 1100, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);

        Assert.Equal(1100, result.Plan.Nesting.UsedLength);
        Assert.True(PerCopyTraceVerifier.IsValidForAllowedAngles(source, ids, result, angles,
            GrainDirectionPolicy.Direction.TwoWay, 90, 1000, 0, 0, 1100));
        Assert.Throws<InvalidOperationException>(() => AutomaticPerCopyNesting.Arrange(source, ids, angles,
            1000, 0, 1000, 0, GrainDirectionPolicy.Direction.TwoWay, 90));
    }
}
