using System.Security.Cryptography;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectBackupPortabilityTests
{
    [Fact]
    public void VersionedGradingProjectRestoresExactlyToDifferentDirectoryWithoutTouchingOtherData()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var testRoot = Path.Combine(root!.FullName, "work", "backup-portability-tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(testRoot, "source-account", "Projects");
        var destinationParent = Path.Combine(testRoot, "different-account", "different-location");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destinationParent);
        var sentinel = Path.Combine(destinationParent, "unrelated.txt");
        File.WriteAllText(sentinel, "leave untouched");
        var ignored = Path.Combine(source, "notes.txt");
        File.WriteAllText(ignored, "not part of project backup");
        var nested = Path.Combine(source, "subfolder");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "unrelated.txt"), "also excluded");

        var piece = new PatternPiece { Name = "Ön", Points = [new(0, 0), new(100, 0), new(0, 80)] };
        var project = new ProjectDocument { Name = "Serilemeli", Pieces = [piece], GradingRules = [new()
        {
            SourcePieceId = piece.Id, SourceClosed = piece.Closed, SourcePoints = [.. piece.Points],
            Deltas = [new(1, 2), new(0, 0), new(-1, 3)]
        }] };
        var sourceFile = Path.Combine(source, "model.tcad");
        ProjectStorage.Save(project, sourceFile);
        var sourceBytes = File.ReadAllBytes(sourceFile);
        Assert.StartsWith("[", System.Text.Encoding.UTF8.GetString(sourceBytes).TrimStart('\uFEFF', ' ', '\r', '\n'));
        var originalHash = SHA256.HashData(sourceBytes);
        var archive = Path.Combine(testRoot, "projects.zip");
        ProjectBackup.Create(source, archive);

        var destination = Path.Combine(destinationParent, "restored-projects");
        ProjectBackup.Restore(archive, destination);
        var restored = Path.Combine(destination, "model.tcad");
        Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(restored)));
        Assert.True(DocumentSession.ContentEquals(project, ProjectStorage.Load(restored)));
        Assert.Equal(sourceBytes, File.ReadAllBytes(sourceFile));
        Assert.Equal("leave untouched", File.ReadAllText(sentinel));
        Assert.Equal("not part of project backup", File.ReadAllText(ignored));
        Assert.False(File.Exists(Path.Combine(destination, "notes.txt")));
        Assert.False(Directory.Exists(Path.Combine(destination, "subfolder")));
    }
}
