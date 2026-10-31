using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectRequiredFieldsTests
{
    private static string NewPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var directory = Path.Combine(root.FullName, "work", "project-required-fields-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tcad");
    }

    [Theory]
    [InlineData(0, "Name")]
    [InlineData(0, "Pieces")]
    [InlineData(0, "PieceName")]
    [InlineData(0, "Points")]
    [InlineData(1, "Name")]
    [InlineData(1, "Pieces")]
    [InlineData(1, "PieceName")]
    [InlineData(1, "Points")]
    [InlineData(1, "GradingRules")]
    [InlineData(2, "Name")]
    [InlineData(2, "Pieces")]
    [InlineData(2, "PieceName")]
    [InlineData(2, "Points")]
    [InlineData(2, "GradingRules")]
    public void MissingSavedFieldsAreRejectedAndSourceIsNotChanged(int version, string field)
    {
        var path = NewPath();
        try
        {
            var piece = new PatternPiece { Name = "Ön", Points = [new(0,0), new(10,0), new(0,10)] };
            if (version == 2) piece.Metadata = new PieceMetadata { Size = "M" };
            var project = new ProjectDocument { Name = "Model", Pieces = [piece] };
            if (version == 1) project.GradingRules.Add(new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [..piece.Points], Deltas = [new(0,0), new(0,0), new(0,0)] });
            ProjectStorage.Save(project, path);
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            var obj = (version == 0 ? root : root[1]!).AsObject();
            var owner = field is "PieceName" or "Points" ? obj["Pieces"]![0]!.AsObject() : obj;
            Assert.True(owner.Remove(field == "PieceName" ? "Name" : field));
            File.WriteAllText(path, root.ToJsonString());
            var before = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LegacyUnversionedProjectMayOmitGradingRules()
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, "{\"Name\":\"Eski\",\"Pieces\":[]}");
            var project = ProjectStorage.Load(path);
            Assert.Equal("Eski", project.Name);
            Assert.Empty(project.Pieces);
            Assert.Empty(project.GradingRules);
        }
        finally { File.Delete(path); }
    }
}
