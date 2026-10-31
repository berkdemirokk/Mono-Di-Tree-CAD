using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class HpglQuantizationLargeAdversarialTests
{
    private static string NewTarget()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "hpgl-large-quantization-adversarial");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".plt");
    }

    private static PatternPiece RingNearCoordinateLimit() => new()
    {
        Name = "Büyük sınır konturu", Closed = true,
        Points = Enumerable.Range(0, 4097).Select(i =>
        {
            var angle = Math.Tau * i / 4097;
            return new Point2(53_000_000 + 600_000 * Math.Cos(angle), 600_000 * Math.Sin(angle));
        }).ToList()
    };

    [Fact]
    public void NonadjacentQuantizedCollisionInLargePathPreservesDestinationAndSource()
    {
        var path = NewTarget(); var piece = RingNearCoordinateLimit();
        // Distinct nonadjacent vertices map to the same plot coordinate; neither edge collapses.
        piece.Points[2000] = piece.Points[0] + new Point2(.001, 0);
        var before = piece.Points.ToArray(); var original = Encoding.UTF8.GetBytes("previous destination");
        try
        {
            File.WriteAllBytes(path, original);
            var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(new ProjectDocument { Pieces = [piece] }, path, 40));
            Assert.Contains("birleşiyor", error.Message);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(before, piece.Points);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LargeContourWhoseQuantizedAreaIsZeroDoesNotReplaceDestination()
    {
        var path = NewTarget();
        var piece = new PatternPiece { Name = "İnce kontur", Closed = true,
            Points = Enumerable.Range(0, 4097).Select(i => new Point2(i, .2 * Math.Sin(Math.PI * i / 4096))).ToList() };
        Assert.NotEqual(0, Geometry.SignedArea(piece.Points));
        var before = piece.Points.ToArray(); var original = Encoding.UTF8.GetBytes("previous destination");
        try
        {
            File.WriteAllBytes(path, original);
            var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(new ProjectDocument { Pieces = [piece] }, path, 1));
            Assert.Contains("alan oluşturmuyor", error.Message);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(before, piece.Points);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void QuantizationCrossingInsideLargeContourDoesNotReplaceDestination()
    {
        var path = NewTarget();
        List<Point2> originalFour = [new(.482528478,1.254648373), new(1.667927417,1.849406483),
            new(4.328536102,.313431146), new(.200085595,3.447040065)];
        Geometry.ValidateContour(originalFour);
        var points = originalFour.ToList();
        // Additional well-separated quantized points put this beyond the former 4096-edge threshold.
        points.AddRange(Enumerable.Range(0, 4093).Select(i => new Point2(100 + i * 2, 5000)));
        var piece = new PatternPiece { Name = "Dönüşüm kesişmesi", Closed = true, Points = points };
        var before = piece.Points.ToArray(); var original = Encoding.UTF8.GetBytes("previous destination");
        try
        {
            File.WriteAllBytes(path, original);
            var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(new ProjectDocument { Pieces = [piece] }, path, 1));
            Assert.Contains("kesişen", error.Message);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(before, piece.Points);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LargeContourNearIntLimitExportsButOutOfRangePointPreservesExistingOutput()
    {
        var path = NewTarget(); var piece = RingNearCoordinateLimit();
        var before = piece.Points.ToArray();
        try
        {
            HpglFormat.Export(new ProjectDocument { Pieces = [piece] }, path, 40);
            var validBytes = File.ReadAllBytes(path);
            Assert.NotEmpty(validBytes);
            Assert.Equal(before, piece.Points);
            piece.Points[0] = new Point2(53_687_092, 0); // multiplied by 40 exceeds Int32.MaxValue.
            var invalid = piece.Points.ToArray();
            var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(new ProjectDocument { Pieces = [piece] }, path, 40));
            Assert.Contains("koordinat sınırı", error.Message);
            Assert.Equal(validBytes, File.ReadAllBytes(path));
            Assert.Equal(invalid, piece.Points);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LargeConditionalExportCannotOverwriteChangedTarget()
    {
        var path = NewTarget(); var piece = RingNearCoordinateLimit(); var before = piece.Points.ToArray();
        try
        {
            File.WriteAllText(path, "first version");
            var expected = ProjectStorage.CaptureVersion(path);
            var external = Encoding.UTF8.GetBytes("external revision");
            File.WriteAllBytes(path, external);
            Assert.Throws<ProjectConflictException>(() => HpglFormat.Export(new ProjectDocument { Pieces = [piece] }, path, 40, expected));
            Assert.Equal(external, File.ReadAllBytes(path));
            Assert.Equal(before, piece.Points);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
