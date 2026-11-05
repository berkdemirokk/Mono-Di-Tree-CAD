using System.Reflection;
using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class AtomicWriteCancellationBoundaryTests
{
    private static string NewDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "atomic-boundary-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    // Reflection deliberately reaches the same private conditional-write path that
    // production uses, with per-call observers rather than a shared mutable hook.
    private static void Write(string path, string content, ProjectFileVersion expected,
        CancellationToken token, Action beforeCheck, Action afterCheck)
    {
        var method = typeof(ProjectStorage).GetMethod("AtomicWriteIfUnchangedCore", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try { method!.Invoke(null, [path, content, expected, token, beforeCheck, afterCheck]); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    [Fact]
    public void CancellationAfterFlushBeforeFinalCheckPreservesTargetAndRemovesTemporaryFile()
    {
        var directory = NewDirectory(); var path = Path.Combine(directory, "out.plt");
        byte[] original = Encoding.UTF8.GetBytes("old content\n");
        try
        {
            File.WriteAllBytes(path, original);
            var version = ProjectStorage.CaptureVersion(path);
            using var cancellation = new CancellationTokenSource();
            bool afterReached = false;
            Assert.Throws<OperationCanceledException>(() => Write(path, "new content\n", version, cancellation.Token,
                () =>
                {
                    Assert.Equal(original, File.ReadAllBytes(path));
                    var temporary = Assert.Single(Directory.GetFiles(directory, "out.plt.*.tmp"));
                    Assert.Equal("new content\n", File.ReadAllText(temporary));
                    cancellation.Cancel();
                },
                () => afterReached = true));
            Assert.False(afterReached);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(directory, "out.plt.*.tmp"));
        }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
    }

    [Fact]
    public void CancellationAfterFinalCheckDoesNotReportFailureAfterSuccessfulReplacement()
    {
        var directory = NewDirectory(); var path = Path.Combine(directory, "out.plt");
        try
        {
            File.WriteAllText(path, "old content\n");
            var version = ProjectStorage.CaptureVersion(path);
            using var cancellation = new CancellationTokenSource();
            bool beforeReached = false;
            Write(path, "new content\n", version, cancellation.Token,
                () => beforeReached = true,
                () => { Assert.True(beforeReached); cancellation.Cancel(); });
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal("new content\n", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "out.plt.*.tmp"));
        }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
    }

    [Fact]
    public void NewFileCancellationAfterFlushRemovesOwnedTempAndLeavesNoTarget()
    {
        var directory=NewDirectory();var path=Path.Combine(directory,"checkpoint.tdregion");
        try {
            using var cancellation=new CancellationTokenSource();
            var expected=new ProjectFileVersion(Path.GetFullPath(path),null);
            Assert.Throws<OperationCanceledException>(()=>Write(path,"checkpoint data",expected,cancellation.Token,
                ()=> { Assert.False(File.Exists(path));Assert.Single(Directory.GetFiles(directory,"checkpoint.tdregion.*.tmp"));cancellation.Cancel(); },
                ()=>throw new InvalidOperationException("Commit must not be reached.")));
            Assert.False(File.Exists(path));Assert.Empty(Directory.GetFiles(directory,"checkpoint.tdregion.*.tmp"));
        } finally { if(File.Exists(path))File.Delete(path);Directory.Delete(directory); }
    }

    [Fact]
    public void NewFileCancellationAfterFinalCheckReportsCompletedCommit()
    {
        var directory=NewDirectory();var path=Path.Combine(directory,"checkpoint.tdregion");
        try {
            using var cancellation=new CancellationTokenSource();
            var expected=new ProjectFileVersion(Path.GetFullPath(path),null);
            Write(path,"checkpoint data",expected,cancellation.Token,()=>{},()=>cancellation.Cancel());
            Assert.True(cancellation.IsCancellationRequested);Assert.Equal("checkpoint data",File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory,"checkpoint.tdregion.*.tmp"));
        } finally { if(File.Exists(path))File.Delete(path);Directory.Delete(directory); }
    }

    [Fact]
    public void ExternalChangeDuringTemporaryWriteIsPreserved()
    {
        var directory = NewDirectory(); var path = Path.Combine(directory, "out.plt");
        try
        {
            File.WriteAllText(path, "old content");
            var version = ProjectStorage.CaptureVersion(path);
            bool afterReached = false;
            Assert.Throws<ProjectConflictException>(() => Write(path, "new content", version, CancellationToken.None,
                () => { Assert.Single(Directory.GetFiles(directory, "out.plt.*.tmp")); File.WriteAllText(path, "external change"); },
                () => afterReached = true));
            Assert.False(afterReached);
            Assert.Equal("external change", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "out.plt.*.tmp"));
        }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
    }

    [Fact]
    public void VersionConflictNeverReachesBoundaryOrReplacesDestination()
    {
        var directory = NewDirectory(); var path = Path.Combine(directory, "out.plt");
        try
        {
            File.WriteAllText(path, "old content");
            var version = ProjectStorage.CaptureVersion(path);
            File.WriteAllText(path, "external change");
            bool boundaryReached = false;
            Assert.Throws<ProjectConflictException>(() => Write(path, "new content", version, CancellationToken.None,
                () => boundaryReached = true, () => boundaryReached = true));
            Assert.False(boundaryReached);
            Assert.Equal("external change", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "out.plt.*.tmp"));
        }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
    }
}
