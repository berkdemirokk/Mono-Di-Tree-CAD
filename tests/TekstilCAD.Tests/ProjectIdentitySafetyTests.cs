using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public class ProjectIdentitySafetyTests
{
    [Theory]
    [InlineData("missing-id")]
    [InlineData("false-finite")]
    [InlineData("invalid-finite")]
    public void RejectsInvalidLegacyDataWithoutChangingFile(string mutation)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "identity-safety-tests"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tcad");
        try
        {
            var doc = new ProjectDocument { Pieces = [new PatternPiece { Points = [new(0,0), new(1,0), new(0,1)] }] };
            ProjectStorage.Save(doc, path);
            Assert.True(DocumentSession.ContentEquals(doc, ProjectStorage.Load(path)));
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            var piece = json["Pieces"]![0]!.AsObject();
            if (mutation == "missing-id") piece.Remove("Id");
            else piece["Points"]![0]!["IsFinite"] = mutation == "false-finite" ? JsonValue.Create(false) : JsonValue.Create("not a boolean");
            var text = json.ToJsonString(); File.WriteAllText(path, text);
            Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
            Assert.Equal(text, File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
