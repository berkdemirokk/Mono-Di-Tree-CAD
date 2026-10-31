using System.IO.Compression;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectBackupTests
{
    [Fact]
    public void EmptyDirectoryIsRejectedWithoutCreatingArchive()
    {
        var root=DirectoryForTest(); var source=Path.Combine(root,"empty"); Directory.CreateDirectory(source);
        var zip=Path.Combine(root,"backup.zip"); Assert.Throws<InvalidDataException>(()=>ProjectBackup.Create(source,zip)); Assert.False(File.Exists(zip));
    }
    [Fact]
    public void FinalInventoryAndHashCheckRejectsChanges()
    {
        var root=DirectoryForTest(); var source=Path.Combine(root,"source"); Directory.CreateDirectory(source);
        var file=Path.Combine(source,"a.tcad"); ProjectStorage.Save(new ProjectDocument(),file);
        var hash=ProjectStorage.CaptureVersion(file).Sha256!;
        var method=typeof(ProjectBackup).GetMethod("VerifySourceUnchanged",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        void Verify() => method.Invoke(null,[source,new[]{"a.tcad"},new[]{hash}]);
        Verify();
        File.WriteAllText(file,"changed");
        Assert.IsType<IOException>(Assert.Throws<System.Reflection.TargetInvocationException>(Verify).InnerException);
        ProjectStorage.Save(new ProjectDocument(),Path.Combine(source,"b.tcad"));
        Assert.IsType<IOException>(Assert.Throws<System.Reflection.TargetInvocationException>(Verify).InnerException);
    }
    private static string DirectoryForTest()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var directory = Path.Combine(root!.FullName, "work", "project-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); return directory;
    }
    [Fact]
    public void BackupAndRestoreProjectsWithoutOverwriting()
    {
        var root=DirectoryForTest();var source=Path.Combine(root,"source");Directory.CreateDirectory(source);
        var piece=new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] };
        var project=new ProjectDocument { Name="Örnek",Pieces=[piece] };
        ProjectStorage.Save(project,Path.Combine(source,"a.tcad")); File.WriteAllText(Path.Combine(source,"other.txt"),"ignored");
        var zip=Path.Combine(root,"backup.zip");ProjectBackup.Create(source,zip);
        Assert.ThrowsAny<IOException>(()=>ProjectBackup.Create(source,zip));
        var target=Path.Combine(root,"restored");ProjectBackup.Restore(zip,target);
        Assert.True(DocumentSession.ContentEquals(project,ProjectStorage.Load(Path.Combine(target,"a.tcad"))));
        Assert.False(File.Exists(Path.Combine(target,"other.txt")));
        Assert.ThrowsAny<IOException>(()=>ProjectBackup.Restore(zip,target));
    }
    [Fact]
    public void TraversalIsRejectedBeforeAnyDestinationIsCreated()
    {
        var root=DirectoryForTest();var zip=Path.Combine(root,"bad.zip");
        using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)) { using var writer=new StreamWriter(archive.CreateEntry("../escape.tcad").Open());writer.Write("bad"); }
        var target=Path.Combine(root,"restored"); Assert.Throws<InvalidDataException>(()=>ProjectBackup.Restore(zip,target));
        Assert.False(Directory.Exists(target));Assert.False(File.Exists(Path.Combine(root,"escape.tcad")));
    }
    [Fact]
    public void ModifiedArchiveIsRejectedWithoutPartialRestore()
    {
        var root=DirectoryForTest();var source=Path.Combine(root,"source");Directory.CreateDirectory(source);
        ProjectStorage.Save(new ProjectDocument(),Path.Combine(source,"a.tcad"));var zip=Path.Combine(root,"backup.zip");ProjectBackup.Create(source,zip);
        using(var archive=ZipFile.Open(zip,ZipArchiveMode.Update)) { var entry=archive.GetEntry("projects/a.tcad")!;entry.Delete();using var writer=new StreamWriter(archive.CreateEntry("projects/a.tcad").Open());writer.Write("tampered"); }
        var target=Path.Combine(root,"restored");Assert.Throws<InvalidDataException>(()=>ProjectBackup.Restore(zip,target));Assert.False(Directory.Exists(target));
    }
}
