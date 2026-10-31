using System.Reflection;
using System.Security.Cryptography;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectBackupBoundedHashTests
{
    private static string Hash(Stream stream, long limit)
    {
        var method = typeof(ProjectBackup).GetMethod("HashBounded", BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return (string)method.Invoke(null, [stream, limit])!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    [Fact]
    public void FinalHashReadsAtMostLimitAndRejectsExtraData()
    {
        byte[] bytes = [1, 2, 3, 4];
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), Hash(new MemoryStream(bytes), 4));
        Assert.Throws<IOException>(() => Hash(new MemoryStream(bytes), 3));
    }
}
