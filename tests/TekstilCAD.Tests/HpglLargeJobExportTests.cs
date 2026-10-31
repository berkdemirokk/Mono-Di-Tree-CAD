using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class HpglLargeJobExportTests
{
    private static string NewTarget()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "hpgl-large-export-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".plt");
    }

    // Large radius keeps every distinct integer-quantized vertex apart, even at 10,000 points.
    private static PatternPiece Ring(int count) => new()
    {
        Name = "Büyük kontur", Closed = true,
        Points = Enumerable.Range(0, count).Select(i =>
        {
            var angle = 2 * Math.PI * i / count;
            return new Point2(1_000_000 * Math.Cos(angle), 1_000_000 * Math.Sin(angle));
        }).ToList()
    };

    [Theory]
    [InlineData(4097)]
    [InlineData(10000)]
    public void LargeValidContourExportsAllVerticesWithoutMutatingSource(int count)
    {
        var path = NewTarget();
        var piece = Ring(count);
        var before = piece.Points.ToArray();
        var doc = new ProjectDocument { Pieces = [piece] };
        try
        {
            HpglFormat.Export(doc, path, 1, CancellationToken.None);
            var text = File.ReadAllText(path);
            Assert.StartsWith("IN;SP1;PA;", text);
            Assert.EndsWith("SP0;\n", text.Replace("\r\n", "\n"));
            var pd = text.IndexOf(";PD", StringComparison.Ordinal);
            var end = text.IndexOf(";PU;", pd, StringComparison.Ordinal);
            Assert.True(pd >= 0 && end > pd);
            // PD contains n-1 vertices and the explicit return to the first.
            Assert.Equal(2 * count, text[(pd + 3)..end].Split(',').Length);
            Assert.Equal(before, piece.Points);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LargeContourQuantizationCollapsePreservesExistingFile()
    {
        var path = NewTarget(); var piece = Ring(4097);
        piece.Points[1] = piece.Points[0] + new Point2(.00001, 0);
        var original = Encoding.UTF8.GetBytes("previous destination bytes");
        try
        {
            File.WriteAllBytes(path, original);
            Assert.Throws<InvalidDataException>(() => HpglFormat.Export(new ProjectDocument { Pieces = [piece] }, path, 1, CancellationToken.None));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void QuantizationCreatedCrossingPreservesExistingFile()
    {
        var path = NewTarget();
        var points = new List<Point2> { new(.482528478,1.254648373), new(1.667927417,1.849406483), new(4.328536102,.313431146), new(.200085595,3.447040065) };
        Geometry.ValidateContour(points);
        var original = Encoding.UTF8.GetBytes("previous destination bytes");
        try
        {
            File.WriteAllBytes(path, original);
            Assert.Throws<InvalidDataException>(() => HpglFormat.Export(new ProjectDocument { Pieces = [new PatternPiece { Points = points }] }, path, 1, CancellationToken.None));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ConditionalLargeExportRejectsChangedDestination()
    {
        var path = NewTarget(); var doc = new ProjectDocument { Pieces = [Ring(4097)] };
        var changed = Encoding.UTF8.GetBytes("external edit");
        try
        {
            File.WriteAllText(path, "original");
            var expected = ProjectStorage.CaptureVersion(path);
            File.WriteAllBytes(path, changed);
            Assert.Throws<ProjectConflictException>(() => HpglFormat.Export(doc, path, 1, expected, CancellationToken.None));
            Assert.Equal(changed, File.ReadAllBytes(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void PreCancelledLargeExportPreservesExistingFileAndSource()
    {
        var path = NewTarget(); var piece = Ring(4097); var before = piece.Points.ToArray();
        var original = Encoding.UTF8.GetBytes("previous destination bytes");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try
        {
            File.WriteAllBytes(path, original);
            var expected = ProjectStorage.CaptureVersion(path);
            Assert.ThrowsAny<OperationCanceledException>(() => HpglFormat.Export(new ProjectDocument { Pieces = [piece] }, path, 1, expected, cancelled.Token));
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(before, piece.Points);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
