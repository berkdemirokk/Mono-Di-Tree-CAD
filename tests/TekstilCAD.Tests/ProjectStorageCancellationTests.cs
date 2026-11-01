using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectStorageCancellationTests
{
    [Fact]
    public void CancellationAtFinalCommitCheckPreservesExistingFile()
    {
        var path=Path.Combine(AppContext.BaseDirectory,$"tekstil-cancel-{Guid.NewGuid():N}.tcad");
        try
        {
            ProjectStorage.Save(new ProjectDocument { Name="Original" },path);
            var bytes=File.ReadAllBytes(path);var version=ProjectStorage.CaptureVersion(path);
            using var cancelled=new CancellationTokenSource();
            Assert.Throws<OperationCanceledException>(()=>ProjectStorage.SaveIfUnchanged(
                new ProjectDocument { Name="Replacement" },path,version,cancelled.Token,()=>cancelled.Cancel()));
            Assert.Equal(bytes,File.ReadAllBytes(path));
            Assert.Equal("Original",ProjectStorage.Load(path).Name);
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }

    [Fact]
    public void CancelledLoadAndSaveDoNotTouchFile()
    {
        var path=Path.Combine(AppContext.BaseDirectory,$"tekstil-cancel-{Guid.NewGuid():N}.tcad");
        try
        {
            ProjectStorage.Save(new ProjectDocument { Name="Original" },path);
            var bytes=File.ReadAllBytes(path);var version=ProjectStorage.CaptureVersion(path);
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(()=>ProjectStorage.LoadWithVersion(path,cancelled.Token));
            Assert.Throws<OperationCanceledException>(()=>ProjectStorage.CaptureVersion(path,cancelled.Token));
            Assert.Throws<OperationCanceledException>(()=>ProjectStorage.SaveIfUnchanged(
                new ProjectDocument { Name="Replacement" },path,version,cancelled.Token));
            Assert.Equal(bytes,File.ReadAllBytes(path));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
}
