using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class AtomicJobDirectoryTests
{
    private static string Fixture()
    {
        var root=Path.Combine(@"C:\Projects\TekstilCAD\work","atomic-job-directory-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);return root;
    }
    private static void Clean(string root) { if(Directory.Exists(root))Directory.Delete(root,true); }

    [Fact]
    public void ExistingGuidDirectoryIsRejectedAndContentsRemainIntact()
    {
        if(!OperatingSystem.IsWindows())return;
        var root=Fixture();
        try
        {
            var id=Guid.NewGuid();var existing=Path.Combine(root,id.ToString("N"));Directory.CreateDirectory(existing);
            var file=Path.Combine(existing,"original.bin");byte[] bytes=[1,2,3,4];File.WriteAllBytes(file,bytes);
            Assert.Throws<IOException>(()=>AtomicJobDirectory.Create(root,id));
            Assert.Equal(bytes,File.ReadAllBytes(file));Assert.Single(Directory.GetFiles(existing));
        }
        finally { Clean(root); }
    }

    [Fact]
    public void OpenDirectoryRejectsRenameAndDeleteAndIsReleasedByDispose()
    {
        if(!OperatingSystem.IsWindows())return;
        var root=Fixture();
        try
        {
            var id=Guid.NewGuid();var path=Path.Combine(root,id.ToString("N"));var moved=Path.Combine(root,"moved");
            var owned=AtomicJobDirectory.Create(root,id);
            try
            {
                Assert.True(owned.IsOpen);Assert.Equal(path,owned.DirectoryPath);Assert.True(Directory.Exists(path));
                Assert.ThrowsAny<IOException>(()=>Directory.Move(path,moved));
                Assert.ThrowsAny<IOException>(()=>Directory.Delete(path));
                Assert.True(Directory.Exists(path));Assert.False(Directory.Exists(moved));
            }
            finally { owned.Dispose(); }
            Assert.False(owned.IsOpen);owned.Dispose();
            Directory.Move(path,moved);Assert.True(Directory.Exists(moved));
        }
        finally { Clean(root); }
    }

    [Fact]
    public void TwoCreationAttemptsCannotClaimSameGuid()
    {
        if(!OperatingSystem.IsWindows())return;
        var root=Fixture();
        try
        {
            var id=Guid.NewGuid();string path;
            using(var first=AtomicJobDirectory.Create(root,id))
            {
                path=first.DirectoryPath;Assert.True(first.IsOpen);
                Assert.Throws<IOException>(()=>AtomicJobDirectory.Create(root,id));
                Assert.True(first.IsOpen);Assert.True(Directory.Exists(path));
            }
            // A second attempt also fails after the first handle is released;
            // disposal must never remove the existing directory.
            Assert.Throws<IOException>(()=>AtomicJobDirectory.Create(root,id));
            Assert.True(Directory.Exists(path));
        }
        finally { Clean(root); }
    }

    [Fact]
    public void EmptyGuidFailsWithoutCreatingAChild()
    {
        if(!OperatingSystem.IsWindows())return;
        var root=Fixture();
        try
        {
            Assert.Throws<ArgumentException>(()=>AtomicJobDirectory.Create(root,Guid.Empty));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally { Clean(root); }
    }

    [Fact]
    public void LongLocalParentCreatesAndRetainsGuidChild()
    {
        if(!OperatingSystem.IsWindows())return;
        var root=Fixture();
        try
        {
            var parent=Path.Combine(root,new string('a',100),new string('b',100));Directory.CreateDirectory(parent);
            var id=Guid.NewGuid();var expected=Path.Combine(parent,id.ToString("N"));
            Assert.True(Path.GetFullPath(expected).Length>260);
            using(var owned=AtomicJobDirectory.Create(parent,id))
            { Assert.True(owned.IsOpen);Assert.Equal(expected,owned.DirectoryPath);Assert.True(Directory.Exists(expected)); }
            Assert.True(Directory.Exists(expected));
        }
        finally { Clean(root); }
    }
}
