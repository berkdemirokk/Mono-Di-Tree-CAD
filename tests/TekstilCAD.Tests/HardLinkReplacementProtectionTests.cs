using System.Runtime.InteropServices;
using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HardLinkReplacementProtectionTests
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingHardLinkCannotBeReplacedAndBothNamesRetainBytes(bool conditional)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "hardlink-replacement-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "original.tcad");
        var alias = Path.Combine(directory, "alias.tcad");
        File.WriteAllText(original, "unchanged original bytes");
        Assert.True(CreateHardLink(alias, original, IntPtr.Zero));
        var before = File.ReadAllBytes(original);
        var version = ProjectStorage.CaptureVersion(alias);
        var project = new ProjectDocument { Name = "Replacement" };
        Assert.ThrowsAny<IOException>(() =>
        {
            if (conditional) ProjectStorage.SaveIfUnchanged(project, alias, version);
            else ProjectStorage.Save(project, alias);
        });
        Assert.Equal(before, File.ReadAllBytes(original));
        Assert.Equal(before, File.ReadAllBytes(alias));
        Assert.True(FileIdentity.SameExistingFile(original, alias));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void OrdinarySingleLinkAndNewDestinationRemainWritable()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "hardlink-replacement-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "project.tcad");
        ProjectStorage.Save(new ProjectDocument { Name="First" }, path);
        ProjectStorage.Save(new ProjectDocument { Name="Second" }, path);
        Assert.Equal("Second", ProjectStorage.Load(path).Name);
    }

    private static void AtomicWriteAtBoundaries(string target, Action? afterCheck, Action? afterCommit)
    {
        var method=typeof(ProjectStorage).GetMethod("AtomicWriteCore",BindingFlags.Static|BindingFlags.NonPublic);
        Assert.NotNull(method);
        try { method!.Invoke(null,[target,"new contents",CancellationToken.None,null,afterCheck,afterCommit]); }
        catch(TargetInvocationException ex) when(ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    [Fact]
    public void MultiplyLinkedTemporarySourceIsNotMovedOrDeleted()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var directory=Path.Combine(root!.FullName,"work","hardlink-replacement-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target=Path.Combine(directory,"out.tcad"); File.WriteAllText(target,"old contents");
        var alias=Path.Combine(directory,"temp-alias");
        Assert.ThrowsAny<IOException>(()=>AtomicWriteAtBoundaries(target,()=>
        {
            var temporary=Assert.Single(Directory.GetFiles(directory,"out.tcad.*.tmp"));
            Assert.True(CreateHardLink(alias,temporary,IntPtr.Zero));
        },null));
        Assert.Equal("old contents",File.ReadAllText(target));
        var temp=Assert.Single(Directory.GetFiles(directory,"out.tcad.*.tmp"));
        Assert.Equal("new contents",File.ReadAllText(alias));
        Assert.True(FileIdentity.SameExistingFile(temp,alias));
    }

    [Fact]
    public void FileAppearingAtTemporaryNameAfterCommitIsNotDeletedOrReportedAsFailure()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var directory=Path.Combine(root!.FullName,"work","hardlink-replacement-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target=Path.Combine(directory,"out.tcad"); File.WriteAllText(target,"old contents");
        var foreign=Path.Combine(directory,"foreign"); File.WriteAllText(foreign,"foreign bytes");
        string? temporary=null;
        AtomicWriteAtBoundaries(target,()=>temporary=Assert.Single(Directory.GetFiles(directory,"out.tcad.*.tmp")),()=>
        {
            Assert.NotNull(temporary);
            Assert.True(CreateHardLink(temporary!,foreign,IntPtr.Zero));
        });
        Assert.Equal("new contents",File.ReadAllText(target));
        Assert.Equal("foreign bytes",File.ReadAllText(foreign));
        Assert.True(FileIdentity.SameExistingFile(temporary!,foreign));
    }

    [Fact]
    public void ReplacedSingleLinkTemporaryIsNeitherPublishedNorDeleted()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var directory=Path.Combine(root!.FullName,"work","hardlink-replacement-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target=Path.Combine(directory,"out.tcad"); File.WriteAllText(target,"old contents");
        string? temporary=null;
        Assert.ThrowsAny<IOException>(()=>AtomicWriteAtBoundaries(target,()=>
        {
            temporary=Assert.Single(Directory.GetFiles(directory,"out.tcad.*.tmp"));
            File.Move(temporary,Path.Combine(directory,"our-original-temp"));
            File.WriteAllText(temporary,"foreign contents");
        },null));
        Assert.Equal("old contents",File.ReadAllText(target));
        Assert.Equal("foreign contents",File.ReadAllText(temporary!));
        Assert.Equal("new contents",File.ReadAllText(Path.Combine(directory,"our-original-temp")));
    }

    [Fact]
    public void CleanupOwnershipFailureDoesNotMaskOriginalException()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var directory=Path.Combine(root!.FullName,"work","hardlink-replacement-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target=Path.Combine(directory,"out.tcad"); File.WriteAllText(target,"old contents");
        var alias=Path.Combine(directory,"alias");
        var error=Assert.Throws<InvalidOperationException>(()=>AtomicWriteAtBoundaries(target,()=>
        {
            var temporary=Assert.Single(Directory.GetFiles(directory,"out.tcad.*.tmp"));
            Assert.True(CreateHardLink(alias,temporary,IntPtr.Zero));
            throw new InvalidOperationException("original failure");
        },null));
        Assert.Equal("original failure",error.Message);
        Assert.Equal("old contents",File.ReadAllText(target));
        Assert.Equal("new contents",File.ReadAllText(alias));
    }

    [Fact]
    public void SymlinkToMovedOriginalTemporaryIsNotPublishedOrDeleted()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var directory=Path.Combine(root!.FullName,"work","hardlink-replacement-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // Windows may prohibit symbolic-link creation for this account. Probe
        // only within the isolated workspace and skip this platform scenario if so.
        var probeTarget=Path.Combine(directory,"probe-target"); File.WriteAllText(probeTarget,"probe");
        var probeLink=Path.Combine(directory,"probe-link");
        try { File.CreateSymbolicLink(probeLink,probeTarget); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 1314 or 50) { return; }
        var target=Path.Combine(directory,"out.tcad"); File.WriteAllText(target,"old contents");
        string? temporary=null;
        var moved=Path.Combine(directory,"moved-original-temp");
        Assert.ThrowsAny<IOException>(()=>AtomicWriteAtBoundaries(target,()=>
        {
            temporary=Assert.Single(Directory.GetFiles(directory,"out.tcad.*.tmp"));
            File.Move(temporary,moved);
            File.CreateSymbolicLink(temporary,moved);
        },null));
        Assert.Equal("old contents",File.ReadAllText(target));
        Assert.Equal("new contents",File.ReadAllText(moved));
        Assert.True((File.GetAttributes(temporary!) & FileAttributes.ReparsePoint)!=0);
    }
}
