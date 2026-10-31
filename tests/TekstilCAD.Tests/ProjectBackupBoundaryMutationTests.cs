using System.Reflection;
using System.Security.Cryptography;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ProjectBackupBoundaryMutationTests
{
    private static string Hash(Stream stream, long limit)
    {
        var method = typeof(ProjectBackup).GetMethod("HashBounded", BindingFlags.NonPublic | BindingFlags.Static)!;
        try { return (string)method.Invoke(null, [stream, limit])!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    // A non-seekable producer returns one byte per read and can outgrow its initial payload,
    // without allocating a 64 MB fixture or depending on timing between OS processes.
    private sealed class GrowingStream(byte[] prefix, byte[] suffix) : Stream
    {
        private readonly byte[] bytes = [..prefix, ..suffix];
        private int position;
        public int BytesReturned => position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0 || position == bytes.Length) return 0;
            buffer[offset] = bytes[position++]; return 1;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void BoundedHashHandlesShortReadsAndRejectsGrowthBeforeReadingRemainder()
    {
        byte[] original = [1, 2, 3, 4];
        using var exact = new GrowingStream(original, []);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), Hash(exact, original.Length));
        using var grows = new GrowingStream(original, [5, 6, 7, 8]);
        Assert.Throws<IOException>(() => Hash(grows, original.Length));
        Assert.Equal(original.Length + 1, grows.BytesReturned);
        using var empty = new GrowingStream([], []);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([])), Hash(empty, 0));
    }

    [Fact]
    public void FinalInventoryRejectsSameLengthMutationAndDeletedFile()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "backup-boundary-mutation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "a.tcad");
        ProjectStorage.Save(new ProjectDocument { Name = "Alpha" }, path);
        var original = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(original));
        var method = typeof(ProjectBackup).GetMethod("VerifySourceUnchanged", BindingFlags.NonPublic | BindingFlags.Static)!;
        void Verify() => method.Invoke(null, [directory, new[] { "a.tcad" }, new[] { hash }]);
        Verify();
        var changed = (byte[])original.Clone();
        var at = Array.IndexOf(changed, (byte)'A');
        Assert.True(at >= 0); changed[at] = (byte)'B';
        File.WriteAllBytes(path, changed);
        var error = Assert.Throws<TargetInvocationException>(Verify);
        Assert.IsAssignableFrom<IOException>(error.InnerException);
        File.Delete(path);
        error = Assert.Throws<TargetInvocationException>(Verify);
        Assert.IsAssignableFrom<IOException>(error.InnerException);
    }
}
