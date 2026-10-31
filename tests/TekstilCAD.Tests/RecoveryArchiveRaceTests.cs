using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class RecoveryArchiveRaceTests
{
    private static string TestDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        return Path.Combine(root.FullName, "work", "recovery-archive-race-tests", Guid.NewGuid().ToString("N"));
    }

    private static (RecoveryStore Reader, RecoveredDocument Recovery, byte[] Bytes) Prepare(string directory)
    {
        string path;
        using (var writer = new RecoveryStore(directory))
        {
            writer.Save(new ProjectDocument { Name = "Korunacak kurtarma", Pieces = [new PatternPiece { Name = "Ön", Points = [new(0,0),new(10,0),new(0,10)] }] }, []);
            path = writer.DraftRecoveryPath;
        }
        var reader = new RecoveryStore(directory);
        return (reader, reader.LoadCandidate(path), File.ReadAllBytes(path));
    }

    [Fact]
    public void ArchivePreservesContentsWhenDestinationExceedsLegacyPathLimit()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(TestDirectory(), new string('a', 100), new string('b', 100));
        var (reader, recovery, bytes) = Prepare(directory);
        using (reader)
        {
            var archived = reader.ArchiveRecovered(recovery);
            Assert.True(archived.Length > 260);
            Assert.False(File.Exists(recovery.SourcePath));
            Assert.Equal(bytes, File.ReadAllBytes(archived));
        }
    }

    [Fact]
    public void LockedSnapshotRejectsConcurrentWriteDeleteAndReplacementBeforeArchive()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = TestDirectory();
        var (reader,recovery,bytes) = Prepare(directory);
        using (reader)
        {
            var replacement=Path.Combine(directory,"replacement.tmp");
            var displaced=Path.Combine(directory,"displaced.tmp");
            File.WriteAllText(replacement,"newer independent content");
            var called=false;
            var archived=reader.ArchiveRecovered(recovery, source =>
            {
                called=true;
                Assert.Equal(recovery.SourcePath,source);
                var writeError=Record.Exception(() => File.WriteAllText(source,"changed"));
                Assert.True(writeError is IOException or UnauthorizedAccessException, writeError?.ToString() ?? "Concurrent write unexpectedly succeeded.");
                var deleteError=Record.Exception(() => File.Delete(source));
                Assert.True(deleteError is IOException or UnauthorizedAccessException, deleteError?.ToString() ?? "Concurrent delete unexpectedly succeeded.");
                var moveError=Record.Exception(() => File.Move(source,displaced));
                Assert.True(moveError is IOException or UnauthorizedAccessException, moveError?.ToString() ?? "Concurrent rename unexpectedly succeeded.");
                var replaceError=Record.Exception(() => File.Replace(replacement,source,null));
                Assert.True(replaceError is IOException or UnauthorizedAccessException, replaceError?.ToString() ?? "Concurrent replacement unexpectedly succeeded.");
            });
            Assert.True(called);
            Assert.False(File.Exists(recovery.SourcePath));
            Assert.False(File.Exists(displaced));
            Assert.Equal("newer independent content",File.ReadAllText(replacement));
            Assert.Equal(bytes,File.ReadAllBytes(archived));
        }
    }

    [Fact]
    public void FailureAfterLockedVerificationLeavesSnapshotAtOriginalPath()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (reader,recovery,bytes)=Prepare(TestDirectory());
        using (reader)
        {
            Assert.Throws<InvalidOperationException>(() => reader.ArchiveRecovered(recovery, _ => throw new InvalidOperationException("injected failure")));
            Assert.True(File.Exists(recovery.SourcePath));
            Assert.Equal(bytes,File.ReadAllBytes(recovery.SourcePath));
            Assert.Equal(recovery.ContentHash,reader.LoadCandidate(recovery.SourcePath).ContentHash);
        }
    }

    [Fact]
    public void ChangedSnapshotIsRejectedBeforeLockedVerificationCallback()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (reader,recovery,_)=Prepare(TestDirectory());
        using (reader)
        {
            File.AppendAllText(recovery.SourcePath," ");
            var changedBytes=File.ReadAllBytes(recovery.SourcePath);
            var called=false;
            Assert.Throws<IOException>(() => reader.ArchiveRecovered(recovery, _ => called=true));
            Assert.False(called);
            Assert.Equal(changedBytes,File.ReadAllBytes(recovery.SourcePath));
        }
    }
    [Fact]
    public void LockedAncestorDirectoriesCannotBeRenamedDuringArchive()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sandbox=TestDirectory();
        var owner=Path.Combine(sandbox,"owner");var directory=Path.Combine(owner,"recovery");
        var (reader,recovery,bytes)=Prepare(directory);
        using(reader)
        {
            var called=false;
            var archived=reader.ArchiveRecovered(recovery, _ =>
            {
                called=true;
                var sourceError=Record.Exception(()=>Directory.Move(directory,Path.Combine(owner,"moved")));
                Assert.True(sourceError is IOException or UnauthorizedAccessException,sourceError?.ToString() ?? "Recovery directory moved during verification.");
                var ancestorError=Record.Exception(()=>Directory.Move(owner,Path.Combine(sandbox,"moved-owner")));
                Assert.True(ancestorError is IOException or UnauthorizedAccessException,ancestorError?.ToString() ?? "Ancestor moved during verification.");
            });
            Assert.True(called);Assert.Equal(bytes,File.ReadAllBytes(archived));
        }
    }
    [Fact]
    public void OversizedReplacementIsRejectedWithoutHashingOrMoving()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (reader,recovery,_)=Prepare(TestDirectory());
        using(reader)
        {
            using(var file=new FileStream(recovery.SourcePath,FileMode.Open,FileAccess.Write,FileShare.None))file.SetLength(RecoveryEnvelope.MaximumBytes+1L);
            var called=false;
            Assert.Throws<InvalidDataException>(()=>reader.ArchiveRecovered(recovery,_=>called=true));
            Assert.False(called);Assert.True(File.Exists(recovery.SourcePath));
            Assert.Equal(RecoveryEnvelope.MaximumBytes+1L,new FileInfo(recovery.SourcePath).Length);
        }
    }
    [Fact]
    public void RecoveryAncestorsAreLockedBeforeArchiveDirectoryCreation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var sandbox=TestDirectory();var owner=Path.Combine(sandbox,"owner");var directory=Path.Combine(owner,"recovery");
        var (reader,recovery,bytes)=Prepare(directory);
        using(reader)
        {
            var called=false;
            var archived=reader.ArchiveRecovered(recovery,null,archiveDirectory=>
            {
                called=true;Assert.False(Directory.Exists(archiveDirectory));
                var e=Record.Exception(()=>Directory.Move(owner,Path.Combine(sandbox,"owner-moved")));
                Assert.True(e is IOException or UnauthorizedAccessException,e?.ToString() ?? "Ancestor moved before archive creation.");
            });
            Assert.True(called);Assert.Equal(bytes,File.ReadAllBytes(archived));
        }
    }
    [Fact]
    public void FailureBeforeArchiveCreationPreservesOriginal()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (reader,recovery,bytes)=Prepare(TestDirectory());
        using(reader)
        {
            Assert.Throws<InvalidOperationException>(()=>reader.ArchiveRecovered(recovery,null,_=>throw new InvalidOperationException("injected")));
            Assert.Equal(bytes,File.ReadAllBytes(recovery.SourcePath));
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(recovery.SourcePath)!,"archived")));
        }
    }
}
