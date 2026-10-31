using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class HpglIntersectionSafetyTests
{
    [Fact]
    public void QuantizationCannotCreateCrossingAndOverwriteExistingOutput()
    {
        var points = new List<Point2> { new(.482528478,1.254648373), new(1.667927417,1.849406483), new(4.328536102,.313431146), new(.200085595,3.447040065) };
        Geometry.ValidateContour(points);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "hpgl-intersection-tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".plt");
        try
        {
            File.WriteAllText(path, "existing");
            var document = new ProjectDocument { Pieces = [new PatternPiece { Points = points }] };
            Assert.Throws<InvalidDataException>(() => HpglFormat.Export(document, path, 1));
            Assert.Equal("existing", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
