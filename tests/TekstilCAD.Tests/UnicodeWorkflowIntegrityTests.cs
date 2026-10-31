using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class UnicodeWorkflowIntegrityTests
{
    private static string TestPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        var directory = Path.Combine(root.FullName, "work", "unicode-workflow", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "project.tcad");
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    public void InvalidEncodedProjectNameIsRejectedWithoutChangingSource(string encodingName)
    {
        var path = TestPath();
        ProjectStorage.Save(new ProjectDocument { Name = "before~after" }, path);
        var original = File.ReadAllText(path, Encoding.UTF8);
        byte[] bytes;
        if (encodingName == "utf8")
        {
            bytes = Encoding.UTF8.GetBytes(original);
            var index = Array.IndexOf(bytes, (byte)'~');
            Assert.True(index >= 0);
            bytes[index] = 0xff;
        }
        else
        {
            var bigEndian = encodingName == "utf16be";
            var encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
            var payload = encoding.GetBytes(original);
            var marker = bigEndian ? new byte[] { 0, 0x7e } : new byte[] { 0x7e, 0 };
            var index = Enumerable.Range(0, payload.Length - 1).First(i => payload[i] == marker[0] && payload[i + 1] == marker[1]);
            // An unpaired high surrogate, followed by the ordinary next character.
            payload[index] = bigEndian ? (byte)0xd8 : (byte)0;
            payload[index + 1] = bigEndian ? (byte)0 : (byte)0xd8;
            bytes = encoding.GetPreamble().Concat(payload).ToArray();
        }
        File.WriteAllBytes(path, bytes);

        Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    public void ValidBomEncodedProjectRemainsReadable(string encodingName)
    {
        var path = TestPath();
        ProjectStorage.Save(new ProjectDocument { Name = "Türkçe 🧵" }, path);
        var text = File.ReadAllText(path, Encoding.UTF8);
        Encoding encoding = encodingName switch
        {
            "utf8" => new UTF8Encoding(true),
            "utf16le" => Encoding.Unicode,
            _ => Encoding.BigEndianUnicode
        };
        File.WriteAllBytes(path, encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray());
        Assert.Equal("Türkçe 🧵", ProjectStorage.Load(path).Name);
    }
}
