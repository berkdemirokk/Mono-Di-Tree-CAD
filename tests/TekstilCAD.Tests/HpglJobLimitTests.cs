using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class HpglJobLimitTests
{
    private static string NewTarget()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "hpgl-job-limit-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".plt");
    }

    [Fact]
    public void EmptyProjectCannotOverwriteExistingOutput()
    {
        var path = NewTarget();
        try
        {
            File.WriteAllText(path, "existing");
            Assert.Throws<InvalidDataException>(() => HpglFormat.Export(new ProjectDocument(), path));
            Assert.Equal("existing", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AggregateWorkLimitRejectsMultipleIndividuallyBoundedContoursBeforeWriting()
    {
        var path = NewTarget();
        try
        {
            File.WriteAllText(path, "existing");
            static PatternPiece Piece(double offset) => new()
            {
                Closed = false,
                Points = Enumerable.Range(0, 8000).Select(i => new Point2(i, offset)).ToList()
            };
            // Each open contour has 7,999 edges and needs 31,988,001 pairs;
            // their combined 63,976,002 pairs exceed the 50-million job limit.
            var doc = new ProjectDocument { Pieces = [Piece(0), Piece(10)] };
            var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(doc, path));
            Assert.Contains("toplam kontur doğrulama sınırını", error.Message);
            Assert.Equal("existing", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
