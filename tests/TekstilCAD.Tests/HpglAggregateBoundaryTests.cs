using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HpglAggregateBoundaryTests
{
    private static string NewPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "hpgl-aggregate-boundaries");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".plt");
    }

    private static PatternPiece OpenLine(int edges, double y) => new()
    {
        Closed = false,
        Points = Enumerable.Range(0, edges + 1).Select(x => new Point2(x, y)).ToList()
    };

    [Fact]
    public void MultipleValidPiecesAreAllWrittenWithoutMutatingSource()
    {
        var path = NewPath();
        try
        {
            var source = new ProjectDocument { Pieces = [OpenLine(2, 0), OpenLine(3, 10)] };
            var snapshot = source.Clone();
            HpglFormat.Export(source, path, 1);
            var text = File.ReadAllText(path);
            Assert.Contains("PU0,0;PD1,0,2,0;PU;", text);
            Assert.Contains("PU0,10;PD1,10,2,10,3,10;PU;", text);
            Assert.True(DocumentSession.ContentEquals(snapshot, source));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(173, false)] // 49,985,001 + 14,878 = 49,999,879 comparisons
    [InlineData(174, true)]  // 49,985,001 + 15,051 = 50,000,052 comparisons
    public void AggregateLimitIsAppliedAcrossPiecesBeforeOverwriting(int secondEdges, bool rejected)
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, "previous output");
            var source = new ProjectDocument { Pieces = [OpenLine(9999, 0), OpenLine(secondEdges, 10)] };
            var snapshot = source.Clone();
            if (rejected)
            {
                var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(source, path, 1));
                Assert.Contains("toplam kontur doğrulama sınırını", error.Message);
                Assert.Equal("previous output", File.ReadAllText(path));
            }
            else
            {
                HpglFormat.Export(source, path, 1);
                var output = File.ReadAllText(path);
                Assert.Contains("PU0,0;PD", output);
                Assert.Contains("PU0,10;PD", output);
                Assert.EndsWith("SP0;\n", output);
            }
            Assert.True(DocumentSession.ContentEquals(snapshot, source));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
