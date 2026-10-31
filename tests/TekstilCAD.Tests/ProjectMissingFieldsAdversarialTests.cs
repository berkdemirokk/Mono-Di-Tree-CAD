using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectMissingFieldsAdversarialTests
{
    private static string NewPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var dir = Path.Combine(root.FullName, "work", "project-missing-fields-adversarial");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tcad");
    }

    [Theory]
    [InlineData("SourcePieceId")]
    [InlineData("SourceClosed")]
    [InlineData("SourcePoints")]
    [InlineData("Deltas")]
    [InlineData("SourcePointX")]
    [InlineData("DeltaY")]
    public void MissingGradingRuleDataIsRejectedWithoutChangingTheFile(string field)
    {
        var path = NewPath();
        try
        {
            var piece = new PatternPiece { Name = "Ön", Points = [new(0, 0), new(10, 0), new(0, 10)] };
            var document = new ProjectDocument { Name = "Model", Pieces = [piece], GradingRules = [new GradingRuleSet
            {
                SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [..piece.Points],
                Deltas = [new(1, 2), new(3, 4), new(5, 6)]
            }] };
            ProjectStorage.Save(document, path);
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            var rule = root[1]!["GradingRules"]![0]!.AsObject();
            var owner = field switch
            {
                "SourcePointX" => rule["SourcePoints"]![0]!.AsObject(),
                "DeltaY" => rule["Deltas"]![0]!.AsObject(),
                _ => rule
            };
            var key = field switch { "SourcePointX" => "X", "DeltaY" => "Y", _ => field };
            Assert.True(owner.Remove(key));
            File.WriteAllText(path, root.ToJsonString());
            var original = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Pieces")]
    [InlineData("GradingRules")]
    public void NullElementsInSavedCollectionsAreRejected(string collection)
    {
        var path = NewPath();
        try
        {
            var json = collection == "Pieces"
                ? "{\"Name\":\"Model\",\"Pieces\":[null]}"
                : "[1,{\"Name\":\"Model\",\"Pieces\":[],\"GradingRules\":[null]}]";
            File.WriteAllText(path, json);
            var original = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }
}
