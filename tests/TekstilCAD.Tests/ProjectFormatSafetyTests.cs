using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectFormatSafetyTests
{
    private static string NewPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var directory = Path.Combine(root.FullName, "work", "project-format-safety-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tcad");
    }

    private static ProjectDocument Sample(bool rules)
    {
        var piece = new PatternPiece { Points = [new(0, 0), new(10, 0), new(0, 10)] };
        var doc = new ProjectDocument { Pieces = [piece] };
        if (rules) doc.GradingRules.Add(new GradingRuleSet { SourcePieceId = piece.Id, SourceClosed = true, SourcePoints = [..piece.Points], Deltas = [new(1, 1), new(0, 0), new(0, 0)] });
        return doc;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentAndLegacyShapesRoundTrip(bool rules)
    {
        var path = NewPath();
        try
        {
            var project = Sample(rules);
            ProjectStorage.Save(project, path);
            Assert.True(DocumentSession.ContentEquals(project, ProjectStorage.Load(path)));
            if (!rules)
            {
                File.WriteAllText(path, "{\"Name\":\"Eski\",\"Pieces\":[]}");
                Assert.Equal("Eski", ProjectStorage.Load(path).Name);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false, "project")]
    [InlineData(false, "piece")]
    [InlineData(false, "point")]
    [InlineData(true, "rule")]
    [InlineData(true, "delta")]
    public void UnknownFieldsAreRejectedWithoutChangingFile(bool rules, string level)
    {
        var path = NewPath();
        try
        {
            ProjectStorage.Save(Sample(rules), path);
            var parsed = JsonNode.Parse(File.ReadAllText(path))!;
            var root = rules ? parsed[1]! : parsed;
            JsonObject target = level switch
            {
                "project" => root.AsObject(),
                "piece" => root["Pieces"]![0]!.AsObject(),
                "point" => root["Pieces"]![0]!["Points"]![0]!.AsObject(),
                "rule" => root["GradingRules"]![0]!.AsObject(),
                "delta" => root["GradingRules"]![0]!["Deltas"]![0]!.AsObject(),
                _ => throw new InvalidOperationException()
            };
            target["FutureData"] = "important";
            var text = parsed.ToJsonString();
            File.WriteAllText(path, text);
            Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
            Assert.Equal(text, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"Name\":\"A\",\"Name\":\"B\",\"Pieces\":[]}")]
    [InlineData("{\"Name\":\"A\",\"Pieces\":[{\"Points\":[{\"X\":1,\"X\":2,\"Y\":0}]}]}")]
    [InlineData("[1,{\"Name\":\"A\",\"Pieces\":[],\"GradingRules\":[],\"GradingRules\":[]}]")]
    public void DuplicateFieldsAreRejected(string json)
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
