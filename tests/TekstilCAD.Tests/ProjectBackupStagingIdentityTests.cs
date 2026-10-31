using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ProjectBackupStagingIdentityTests
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName,string existingFileName,IntPtr securityAttributes);

    private static string NewRoot()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);
        var dir=Path.Combine(root!.FullName,"work","backup-staging-identity",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CreateAtBoundary(string source,string destination,Action<string> callback)
    {
        var method=typeof(ProjectBackup).GetMethod("CreateWithSummaryCore",BindingFlags.Static|BindingFlags.NonPublic);
        Assert.NotNull(method);
        try { method!.Invoke(null,[source,destination,callback]); }
        catch(TargetInvocationException ex) when(ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacedStagingFileIsNeitherPublishedNorDeleted(bool useHardLink)
    {
        if(!OperatingSystem.IsWindows())return;
        var root=NewRoot(); var source=Path.Combine(root,"projects"); Directory.CreateDirectory(source);
        var project=Path.Combine(source,"pattern.tcad"); ProjectStorage.Save(new ProjectDocument { Name="Original" },project);
        var original=File.ReadAllBytes(project); var target=Path.Combine(root,"backup.zip");
        var foreign=Path.Combine(root,"foreign.bin"); File.WriteAllText(foreign,"foreign content");
        string? staging=null, displaced=null;
        Assert.ThrowsAny<IOException>(()=>CreateAtBoundary(source,target,path=>
        {
            staging=path; displaced=path+".original"; File.Move(path,displaced);
            if(useHardLink)Assert.True(CreateHardLink(path,foreign,IntPtr.Zero));
            else File.WriteAllText(path,"foreign content");
        }));
        Assert.False(File.Exists(target));
        Assert.NotNull(staging); Assert.NotNull(displaced);
        Assert.Equal("foreign content",File.ReadAllText(staging!));
        Assert.Equal("foreign content",File.ReadAllText(foreign));
        Assert.True(File.Exists(displaced));
        Assert.Equal(original,File.ReadAllBytes(project));
        if(useHardLink)Assert.True(FileIdentity.SameExistingFile(staging!,foreign));
    }
}
