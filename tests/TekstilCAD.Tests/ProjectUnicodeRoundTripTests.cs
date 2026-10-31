using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectUnicodeRoundTripTests
{
    private static string TestPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        var directory = Path.Combine(root.FullName, "work", "project-unicode-roundtrip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "project.tcad");
    }

    private static ProjectDocument Document() => new()
    {
        Name = "Proje",
        Pieces = [new PatternPiece { Name = "Ön", Points = [new(0, 0), new(10, 0), new(0, 10)] }]
    };

    [Theory]
    [InlineData(true, '\ud800')]
    [InlineData(true, '\udc00')]
    [InlineData(false, '\ud800')]
    [InlineData(false, '\udc00')]
    public void UnpairedSurrogateIsEitherPreservedExactlyOrRejectedWithoutReplacingFile(bool projectName, char unpaired)
    {
        var path = TestPath();
        var before = Document();
        ProjectStorage.Save(before, path);
        var original = File.ReadAllBytes(path);
        var candidate = before.Clone();
        var name = "Metin" + unpaired + "son";
        if (projectName) candidate.Name = name;
        else candidate.Pieces[0].Name = name;
        var version = ProjectStorage.CaptureVersion(path);

        var failure = Record.Exception(() => ProjectStorage.SaveIfUnchanged(candidate, path, version));
        if (failure is null)
        {
            var restored = ProjectStorage.Load(path);
            Assert.Equal(name, projectName ? restored.Name : restored.Pieces[0].Name);
        }
        else
        {
            Assert.IsType<InvalidDataException>(failure);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData(true, '\ud800')]
    [InlineData(true, '\udc00')]
    [InlineData(false, '\ud800')]
    [InlineData(false, '\udc00')]
    public void InvalidMetadataIsRejectedWithoutReplacingFile(bool sizeField, char unpaired)
    {
        var path = TestPath();
        var document = Document();
        ProjectStorage.Save(document, path);
        var original = File.ReadAllBytes(path);
        var text = "Metin" + unpaired + "son";
        document.Pieces[0].Metadata = sizeField
            ? new PieceMetadata { Size = text }
            : new PieceMetadata { Material = text };
        Assert.Throws<InvalidDataException>(() => ProjectStorage.SaveIfUnchanged(document, path, ProjectStorage.CaptureVersion(path)));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ValidSurrogatePairsInAllEditableTextFieldsRoundTrip()
    {
        var path = TestPath();
        var document = Document();
        document.Name = "Proje 🧵";
        document.Pieces[0].Name = "Parça ✂️";
        document.Pieces[0].Metadata = new PieceMetadata { Size = "Beden 🧵", Material = "Kumaş 🪡" };
        ProjectStorage.Save(document, path);
        var restored = ProjectStorage.Load(path);
        Assert.Equal(document.Name, restored.Name);
        Assert.Equal(document.Pieces[0].Name, restored.Pieces[0].Name);
        Assert.Equal(document.Pieces[0].Metadata!.Size, restored.Pieces[0].Metadata!.Size);
        Assert.Equal(document.Pieces[0].Metadata!.Material, restored.Pieces[0].Metadata!.Material);
    }
}
