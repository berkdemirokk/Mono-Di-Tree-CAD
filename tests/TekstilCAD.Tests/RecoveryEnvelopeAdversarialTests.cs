using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryEnvelopeAdversarialTests
{
    private static string DirectoryForTest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-envelope-adversarial", Guid.NewGuid().ToString("N"));
    }

    private static ProjectDocument Document() => new()
    {
        Name = "Çalışma",
        Pieces = [new PatternPiece { Name = "Ön", Points = [new(0, 0), new(10, 0), new(0, 10)] }]
    };

    private static (string Directory, string Path, RecoveryPropertyDraft Draft) Snapshot()
    {
        var directory = DirectoryForTest();
        var document = Document();
        var draft = new RecoveryPropertyDraft(document.Pieces[0].Id, "Taslak", "?", "0", "Ön", 0, 0);
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(document, [draft]);
            path = writer.DraftRecoveryPath;
        }
        return (directory, path, draft);
    }

    [Theory]
    [InlineData("unsupported-version")]
    [InlineData("unknown-field")]
    [InlineData("missing-field")]
    [InlineData("invalid-base64")]
    [InlineData("odd-utf16")]
    [InlineData("empty-id")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-root")]
    public void CorruptEnvelopeIsRejectedWithoutChangingOriginal(string corruption)
    {
        var (directory, path, _) = Snapshot();
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var drafts = root["Drafts"]!.AsArray();
        var item = drafts[0]!.AsObject();
        switch (corruption)
        {
            case "unsupported-version": root["Version"] = 2; break;
            case "unknown-field": root["Unexpected"] = true; break;
            case "missing-field": item.Remove("BaseX"); break;
            case "invalid-base64": item["Name"] = "%%%"; break;
            case "odd-utf16": item["Name"] = Convert.ToBase64String([0x41]); break;
            case "empty-id": item["PieceId"] = Guid.Empty.ToString(); break;
            case "duplicate-id": drafts.Add(item.DeepClone()); break;
        }
        var text = root.ToJsonString();
        if (corruption == "duplicate-root") text = text.Insert(1, "\"Version\":1,");
        File.WriteAllText(path, text);
        var corruptBytes = File.ReadAllBytes(path);

        using var reader = new RecoveryStore(directory);
        var candidate = Assert.Single(reader.Scan().Candidates);
        Assert.False(candidate.IsRecoverable);
        Assert.Throws<InvalidDataException>(() => reader.LoadCandidate(path));
        Assert.Equal(corruptBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void OverlongDraftDoesNotReplacePreviouslyRecoverableSnapshot()
    {
        var directory = DirectoryForTest();
        var document = Document();
        var draft = new RecoveryPropertyDraft(document.Pieces[0].Id, "İlk", "?", "0", "Ön", 0, 0);
        string path;
        byte[] before;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(document, [draft]);
            path = writer.DraftRecoveryPath;
            before = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => writer.Save(document, [draft with { Name = new string('a', 16385) }]));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        using var reader = new RecoveryStore(directory);
        Assert.Equal(draft, Assert.Single(reader.LoadCandidate(path).Drafts));
    }

    [Fact]
    public void MaximumLengthDraftRoundTripsIncompleteSurrogateWithoutTruncation()
    {
        var directory = DirectoryForTest();
        var document = Document();
        var text = new string('x', 16383) + '\ud800';
        var draft = new RecoveryPropertyDraft(document.Pieces[0].Id, text, "?", "0", "Ön", 0, 0);
        string path;
        using (var writer = new RecoveryStore(directory)) { writer.Save(document, [draft]); path = writer.DraftRecoveryPath; }
        using var reader = new RecoveryStore(directory);
        Assert.Equal(text, Assert.Single(reader.LoadCandidate(path).Drafts).Name);
    }
}
