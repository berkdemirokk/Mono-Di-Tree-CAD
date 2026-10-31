using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectSizeRoundTripTests
{
    private static string WorkspaceFile()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Workspace not found.");
        var directory = Path.Combine(root.FullName, "work", "project-size-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tcad");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedValidProjectDoesNotReplaceExistingFile(bool conditional)
    {
        var path = WorkspaceFile();
        var original = new ProjectDocument { Name = "Önceki proje" };
        try
        {
            ProjectStorage.Save(original, path);
            var before = File.ReadAllBytes(path);
            var version = ProjectStorage.CaptureVersion(path);
            var project = new ProjectDocument();
            // Default JSON escaping expands each non-ASCII code unit to six bytes.
            var name = new string('ğ', 1024);
            var metadataText = new string('ş', 128);
            for (var i = 0; i < ProjectStorage.MaximumPieces; i++)
                project.Pieces.Add(new PatternPiece { Name = name,
                    Points = [new(0, 0), new(1, 0), new(0, 1)],
                    Metadata = new PieceMetadata { Size = metadataText, Material = metadataText } });
            ProjectStorage.Validate(project);
            Assert.Throws<InvalidDataException>(() =>
            {
                if (conditional) ProjectStorage.SaveIfUnchanged(project, path, version);
                else ProjectStorage.Save(project, path);
            });
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(original.Name, ProjectStorage.Load(path).Name);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void SupportedBomEncodingsPreserveUnicode(int kind)
    {
        var path = WorkspaceFile();
        try
        {
            Encoding encoding = kind switch
            {
                0 => new UTF8Encoding(true, true),
                1 => new UnicodeEncoding(false, true, true),
                2 => new UnicodeEncoding(true, true, true),
                3 => new UTF32Encoding(false, true, true),
                _ => new UTF32Encoding(true, true, true)
            };
            var json = "{\"Name\":\"İplik 🧵\",\"Pieces\":[]}";
            File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes(json)).ToArray());
            Assert.Equal("İplik 🧵", ProjectStorage.Load(path).Name);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
