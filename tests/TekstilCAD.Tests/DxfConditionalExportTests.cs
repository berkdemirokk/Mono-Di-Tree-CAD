using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfConditionalExportTests
{
    [Fact]
    public void ChangedDestinationIsPreservedAndExportRejected()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var dir=Path.Combine(root!.FullName,"work","conditional-export-tests"); Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,Guid.NewGuid().ToString("N")+".dxf");
        try
        {
            File.WriteAllText(path,"original"); var token=ProjectStorage.CaptureVersion(path);
            File.WriteAllText(path,"external update");
            var piece=new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] };
            var project=new ProjectDocument { Pieces=[piece] };
            Assert.Throws<ProjectConflictException>(()=>DxfFormat.Export(project,path,token));
            Assert.Equal("external update",File.ReadAllText(path));
            var current=ProjectStorage.CaptureVersion(path);
            DxfFormat.Export(project,path,current);
            Assert.Single(DxfFormat.Import(path).Project.Pieces);
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Fact]
    public void MissingDestinationCreatedOnlyIfStillMissing()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var dir=Path.Combine(root!.FullName,"work","conditional-export-tests");Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,Guid.NewGuid().ToString("N")+".dxf");
        try {
            var token=ProjectStorage.CaptureVersion(path);File.WriteAllText(path,"new file");
            var p=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(1,0)] , Closed=false }] };
            Assert.Throws<ProjectConflictException>(()=>DxfFormat.Export(p,path,token));
            Assert.Equal("new file",File.ReadAllText(path));
        } finally {if(File.Exists(path))File.Delete(path);}
    }
}
