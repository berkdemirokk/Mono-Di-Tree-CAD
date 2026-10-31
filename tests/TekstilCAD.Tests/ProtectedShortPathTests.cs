using System.Runtime.InteropServices;
using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProtectedShortPathTests
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true, EntryPoint="GetShortPathNameW")]
    private static extern uint GetShortPathName(string path, StringBuilder result, uint capacity);

    private static string ShortName(string path)
    {
        var buffer=new StringBuilder(32768);
        var length=GetShortPathName(path,buffer,(uint)buffer.Capacity);
        Assert.True(length>0 && length<buffer.Capacity,"Could not inspect short path for workspace fixture.");
        return buffer.ToString();
    }

    [Fact]
    public void ProtectedDirectoryIsRejectedThroughExistingShortAncestorAndMissingLeaf()
    {
        if(!OperatingSystem.IsWindows())return;
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);
        var parent=Path.Combine(root!.FullName,"work","short-path-tests",Guid.NewGuid().ToString("N"));
        var protectedDirectory=Path.Combine(parent,"Long Protected Directory For Alias");
        var allowedDirectory=Path.Combine(parent,"Other Directory");
        Directory.CreateDirectory(protectedDirectory);Directory.CreateDirectory(allowedDirectory);
        var roots=new[]{protectedDirectory};
        Assert.Throws<InvalidOperationException>(()=>ProjectStorage.EnsureWritableDestinationCore(Path.Combine(protectedDirectory,"future","file.tcad"),roots));
        ProjectStorage.EnsureWritableDestinationCore(Path.Combine(allowedDirectory,"future","file.tcad"),roots);
        var shortDirectory=ShortName(protectedDirectory);
        // Some filesystems disable creation of 8.3 aliases; direct path assertions
        // still run there. The separate real-installation test checks if present.
        if(!string.Equals(shortDirectory,protectedDirectory,StringComparison.OrdinalIgnoreCase))
        {
            Assert.Throws<InvalidOperationException>(()=>ProjectStorage.EnsureWritableDestinationCore(Path.Combine(shortDirectory,"future","file.tcad"),roots));
            Assert.Throws<InvalidOperationException>(()=>ProjectStorage.EnsureWritableDestinationCore(shortDirectory,roots));
        }
    }

    [Fact]
    public void InstalledWearCadShortAliasIsRejectedWithoutWriting()
    {
        if(!OperatingSystem.IsWindows() || !Directory.Exists(@"C:\WearCadfe"))return;
        var alias=ShortName(@"C:\WearCadfe");
        if(string.Equals(alias,@"C:\WearCadfe",StringComparison.OrdinalIgnoreCase))return;
        Assert.Throws<InvalidOperationException>(()=>ProjectStorage.EnsureWritableDestination(Path.Combine(alias,"missing-test-folder","never-created.tcad")));
    }

    [Theory]
    [InlineData(@"\Device\HarddiskVolume3", true)]
    [InlineData(@"\??\C:\WearCadfe", false)]
    [InlineData(@"\??\UNC\server\share", false)]
    [InlineData(@"\Device\HarddiskVolume3\WearCadfe", false)]
    [InlineData(@"\Device\Mup\server\share", false)]
    public void DosDriveMappingAllowsOnlyDirectVolumeDevices(string mapping, bool allowed)
        => Assert.Equal(allowed,WindowsLongPath.IsOrdinaryVolumeMapping(mapping));

    [Fact]
    public void MultipleDriveLettersForSameVolumeCannotBypassProtectedRoot()
    {
        if(!OperatingSystem.IsWindows())return;
        const string first=@"\Device\HarddiskVolume17", second=@"\Device\HarddiskVolume18";
        Assert.True(WindowsLongPath.IsProtectedOnSameVolume(@"D:\WearCadfe\missing\file.tcad",first,@"C:\WearCadfe",first));
        Assert.True(WindowsLongPath.IsProtectedOnSameVolume(@"D:\WEARCADFE",first,@"C:\WearCadfe",first));
        Assert.False(WindowsLongPath.IsProtectedOnSameVolume(@"D:\WearCadfe2\file.tcad",first,@"C:\WearCadfe",first));
        Assert.False(WindowsLongPath.IsProtectedOnSameVolume(@"D:\WearCadfe\file.tcad",second,@"C:\WearCadfe",first));
    }
}
