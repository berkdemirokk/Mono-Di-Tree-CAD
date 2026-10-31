using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryLegacyTests
{
    [Fact]
    public void LegacyRecoveryRemainsIdentifiedAfterRepeatedScansAndCannotBeArchived()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);
        var testRoot=Path.GetFullPath(Path.Combine(root.FullName,"work","recovery-legacy-tests"));
        var directory=Path.Combine(testRoot,Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"session-"+Guid.NewGuid().ToString("N")+".tcad");
            ProjectStorage.Save(new ProjectDocument { Name="Eski oturum" },path);
            using(var current=new RecoveryStore(directory))
            {
                Assert.True(Assert.Single(current.Scan().Candidates).IsLegacy);
                Assert.True(Assert.Single(current.Scan().Candidates).IsLegacy);
                var recovered=current.LoadCandidate(path);
                Assert.False(recovered.CanArchive);
                Assert.Throws<InvalidOperationException>(()=>current.ArchiveRecovered(recovered));
                Assert.Equal("Eski oturum",ProjectStorage.Load(path).Name);
            }
        }
        finally
        {
            var resolved=Path.GetFullPath(directory);
            Assert.Equal(testRoot,Path.GetDirectoryName(resolved));
            Directory.Delete(resolved,true);
        }
    }
}
