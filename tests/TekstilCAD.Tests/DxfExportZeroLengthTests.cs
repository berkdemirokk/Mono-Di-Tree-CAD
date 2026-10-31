using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfExportZeroLengthTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdjacentDuplicateRejectsBeforeReplacingDestination(bool closed)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "dxf-zero-length-tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            File.WriteAllText(path, "original");
            var project = new ProjectDocument { Pieces = [new PatternPiece { Closed = closed, Points = [new(0, 0), new(1, 0), new(1, 0), new(1, 1)] }] };
            Assert.Throws<InvalidDataException>(() => DxfFormat.Export(project, path));
            Assert.Equal("original", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExplicitSingleClosureRemainsAccepted()
    {
        var project = new ProjectDocument { Pieces = [new PatternPiece { Closed = true, Points = [new(0, 0), new(1, 0), new(1, 1), new(0, 0)] }] };
        Assert.Single(DxfFormat.PlanLayers(project));
    }
}
