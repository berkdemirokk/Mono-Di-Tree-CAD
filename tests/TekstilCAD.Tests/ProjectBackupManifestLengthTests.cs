using System.Reflection;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectBackupManifestLengthTests
{
    private static byte[] Read(Stream stream, long declaredLength)
    {
        var method = typeof(ProjectBackup).GetMethod("ReadManifestBytes", BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return (byte[])method.Invoke(null, new object[] { stream, declaredLength })!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    [Fact]
    public void AcceptsExactlyTheLimitWhenDeclaredLengthMatches()
    {
        var bytes = new byte[1024 * 1024];
        bytes[^1] = 42;
        Assert.Equal(bytes, Read(new MemoryStream(bytes), bytes.Length));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 3)]
    [InlineData(1024 * 1024 + 1, 1024 * 1024)]
    [InlineData(1, 1024 * 1024 + 1)]
    public void RejectsActualOrDeclaredLengthMismatchAndOversize(int actualLength, int declaredLength)
    {
        using var stream = new MemoryStream(new byte[actualLength]);
        Assert.Throws<InvalidDataException>(() => Read(stream, declaredLength));
    }
}
