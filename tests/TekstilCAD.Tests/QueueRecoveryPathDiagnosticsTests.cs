using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class QueueRecoveryPathDiagnosticsTests
{
    private static string FixturePath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root missing.");
        return Path.Combine(root.FullName, "work", "queue-recovery-path-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void AbsentQueueDirectoryIsAnEmptyFirstRunWithoutCreatingIt()
    {
        var path = FixturePath();
        var queue = new SimulationQueue(path);
        Assert.Empty(queue.Jobs);
        Assert.Empty(queue.RecoveryWarnings);
        Assert.False(Directory.Exists(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void FileAtQueuePathIsReportedAndLeftUnchanged()
    {
        var path = FixturePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] original = [0x54, 0x65, 0x73, 0x74];
        File.WriteAllBytes(path, original);
        var queue = new SimulationQueue(path);
        Assert.Empty(queue.Jobs);
        Assert.Contains(queue.RecoveryWarnings, warning => warning.Contains("klasör değil"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }
}
