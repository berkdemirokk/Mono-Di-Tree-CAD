using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class AtomicWriteUnicodeIntegrityTests
{
    [Theory]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void InvalidUnicodeDoesNotReplaceExistingExport(int invalid)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var directory = Path.Combine(root.FullName, "work", "atomic-unicode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "export.txt");
        try
        {
            File.WriteAllText(path, "original");
            var before = File.ReadAllBytes(path);
            var version = ProjectStorage.CaptureVersion(path);
            Assert.Throws<EncoderFallbackException>(() => ProjectStorage.AtomicWriteIfUnchanged(path, "prefix" + (char)invalid, version));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
