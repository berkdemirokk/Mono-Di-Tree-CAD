using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryOversizedCandidateTests
{
    [Theory]
    [InlineData("tcad", 64L * 1024 * 1024 + 1)]
    [InlineData("tcr", 80L * 1024 * 1024 + 1)]
    [InlineData("tcad", 128L * 1024 * 1024 + 1)]
    public void OversizedCandidateIsReportedAndOtherFormatRemainsRecoverable(string extension, long length)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        var directory = Path.Combine(root.FullName, "work", "recovery-oversized-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var oversizedPath = Path.Combine(directory, $"session-{Guid.NewGuid():N}.{extension}");
        try
        {
            using (var stream = new FileStream(oversizedPath, FileMode.CreateNew, FileAccess.Write))
                stream.SetLength(length);
            string validPath;
            using (var session = new RecoveryStore(directory))
            {
                var project = new ProjectDocument { Name = "Kurtarılabilir" };
                if (extension == "tcad")
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

            var scan = RecoveryStore.ScanDirectory(directory);
            Assert.Contains(scan.Candidates, c => c.Path == oversizedPath && !c.IsRecoverable);
            Assert.Contains(scan.Errors, e => e.Path == oversizedPath && e.Message.Contains("boyut sınırını aşıyor", StringComparison.Ordinal));
            Assert.Contains(scan.Candidates, c => c.Path == validPath && c.IsRecoverable && c.ProjectName == "Kurtarılabilir");
            Assert.True(File.Exists(oversizedPath));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
