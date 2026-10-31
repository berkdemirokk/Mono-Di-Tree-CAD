using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryScanFairnessTests
{
    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-fairness-tests", Guid.NewGuid().ToString("N"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BacklogOfOneFormatDoesNotHideOtherFormatAtCandidateLimit(bool draftRecord)
    {
        var directory = TestDirectory();
        Directory.CreateDirectory(directory);
        string validPath;
        using (var session = new RecoveryStore(directory))
        {
            var project = new ProjectDocument { Name = "Kurtarılabilir" };
            if (draftRecord)
            {
                session.Save(project, Array.Empty<RecoveryPropertyDraft>());
                validPath = session.DraftRecoveryPath;
            }
            else
            {
                session.Save(project);
                validPath = session.RecoveryPath;
            }
        }
        var otherExtension = draftRecord ? "tcad" : "tcr";
        for (var i = 0; i < 1024; i++)
            File.WriteAllText(Path.Combine(directory, $"session-{Guid.NewGuid():N}.{otherExtension}"), "{}");

        var scan = RecoveryStore.ScanDirectory(directory);
        Assert.Contains(scan.Candidates, c => c.Path == validPath && c.IsRecoverable && c.ProjectName == "Kurtarılabilir");
        Assert.Contains(scan.Errors, e => e.Message.Contains("kayıt sayısı sınırına", StringComparison.Ordinal));
        Assert.True(File.Exists(validPath));
    }
}
