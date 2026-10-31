using TekstilCAD.Core;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;

public sealed class ProjectExternalWriterRaceTests
{
    private static string Target()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root missing.");
        var dir=Path.Combine(root.FullName,"work","external-writer-races",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);return Path.Combine(dir,"project.tcad");
    }
    private static ProjectDocument Document(string name)=>new() { Name=name, Pieces=[new PatternPiece { Name="Ön",Points=[new(0,0),new(20,0),new(0,30)] }] };

    [Fact]
    public void ExistingDestinationModifiedBeforeFinalCheckIsPreserved()
    {
        var path=Target();
        ProjectStorage.Save(Document("İlk proje"),path);
        var expected=ProjectStorage.CaptureVersion(path);
        var externalBytes=System.Text.Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(Document("Harici çalışma")));
        Assert.Throws<ProjectConflictException>(()=>ProjectStorage.SaveIfUnchanged(Document("Bizim çalışma"),path,expected,
            ()=>File.WriteAllBytes(path,externalBytes)));
        Assert.Equal(externalBytes,File.ReadAllBytes(path));
        Assert.Equal("Harici çalışma",ProjectStorage.Load(path).Name);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));
    }

    [Fact]
    public void NewlyCreatedExternalFileAfterVersionCheckIsNeverOverwritten()
    {
        var path=Target();var missing=ProjectStorage.CaptureVersion(path);Assert.Null(missing.Sha256);
        var candidate=Document("Bizim çalışma");var external=Document("Harici çalışma");
        string? committedExternalHash=null;
        var error=Record.Exception(()=>ProjectStorage.SaveIfUnchanged(candidate,path,missing,()=>
        {
            File.WriteAllText(path,ProjectStorage.SerializeProject(external),new System.Text.UTF8Encoding(false));
            committedExternalHash=ProjectStorage.CaptureVersion(path).Sha256;
        }));
        Assert.True(error is IOException,error?.ToString() ?? "External file was overwritten without a conflict.");
        Assert.NotNull(committedExternalHash);
        Assert.Equal(committedExternalHash,ProjectStorage.CaptureVersion(path).Sha256);
        Assert.Equal("Harici çalışma",ProjectStorage.Load(path).Name);
        Assert.Equal("Bizim çalışma",candidate.Name);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));
    }

    [Fact]
    public void MissingDestinationWithoutCompetingWriterIsCreatedNormally()
    {
        var path=Target();var missing=ProjectStorage.CaptureVersion(path);var called=false;
        var result=ProjectStorage.SaveIfUnchanged(Document("Yeni proje"),path,missing,()=>called=true);
        Assert.True(called);Assert.Equal("Yeni proje",ProjectStorage.Load(path).Name);
        Assert.Equal(result,ProjectStorage.CaptureVersion(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));
    }

    [Fact]
    public void FailureBeforeFirstCommitLeavesDestinationAbsent()
    {
        var path=Target();var missing=ProjectStorage.CaptureVersion(path);
        Assert.Throws<InvalidOperationException>(()=>ProjectStorage.SaveIfUnchanged(Document("Yeni proje"),path,missing,()=>throw new InvalidOperationException("injected")));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));
    }
    [Fact]
    public void ConditionalTextExportDoesNotOverwriteAnExternallyCreatedDestination()
    {
        var path=Target();var missing=ProjectStorage.CaptureVersion(path);
        var method=typeof(ProjectStorage).GetMethod("AtomicWriteIfUnchangedCore",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);Action external=()=>File.WriteAllText(path,"external content");
        var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>method!.Invoke(null,[path,"our export",missing,CancellationToken.None,external,null]));
        Assert.IsAssignableFrom<IOException>(error.InnerException);
        Assert.Equal("external content",File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));
    }
}
