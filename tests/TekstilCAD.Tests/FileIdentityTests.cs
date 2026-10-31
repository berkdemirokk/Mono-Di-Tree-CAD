using System.Runtime.InteropServices;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class FileIdentityTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);

    [Fact]
    public void DetectsHardLinksAndDoesNotAlterContents()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "file-identity-tests"); Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var source = Path.Combine(directory, id + ".tcad"); var alias = Path.Combine(directory, id + ".dxf"); var other = Path.Combine(directory, id + "-other.dxf");
        try
        {
            File.WriteAllText(source, "original"); File.WriteAllText(other, "original");
            Assert.True(CreateHardLink(alias, source, IntPtr.Zero));
            Assert.True(FileIdentity.SameExistingFile(source, source));
            Assert.True(FileIdentity.SameExistingFile(source, alias));
            Assert.False(FileIdentity.SameExistingFile(source, other));
            Assert.False(FileIdentity.SameExistingFile(source, Path.Combine(directory, id + "-missing.dxf")));
            Assert.Equal("original", File.ReadAllText(source)); Assert.Equal("original", File.ReadAllText(alias));
        }
        finally { foreach (var path in new[] { alias, source, other }) if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void MissingSourceFailsClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.ThrowsAny<IOException>(() => FileIdentity.SameExistingFile(missing, missing + "-other"));
    }
}
