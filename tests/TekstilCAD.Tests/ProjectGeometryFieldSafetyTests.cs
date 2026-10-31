using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectGeometryFieldSafetyTests
{
    [Theory]
    [InlineData("X")]
    [InlineData("Y")]
    [InlineData("Closed")]
    [InlineData("SourceClosed")]
    public void MissingGeometryFieldIsRejectedWithoutChangingFile(string field)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "geometry-field-safety-tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tcad");
        try
        {
            var piece = new PatternPiece { Points = [new(0, 0), new(10, 0), new(0, 10)] };
            var doc = new ProjectDocument { Pieces = [piece], GradingRules = [new() { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [.. piece.Points], Deltas = [new(1, 2), new(0, 0), new(0, 0)] }] };
            ProjectStorage.Save(doc, path);
            Assert.True(DocumentSession.ContentEquals(doc, ProjectStorage.Load(path)));
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            var project = json[1]!;
            if (field == "SourceClosed") project["GradingRules"]![0]!.AsObject().Remove(field);
            else if (field == "Closed") project["Pieces"]![0]!.AsObject().Remove(field);
            else project["Pieces"]![0]!["Points"]![1]!.AsObject().Remove(field);
            var text = json.ToJsonString(); File.WriteAllText(path, text);
            Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
            Assert.Equal(text, File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
