using TekstilCAD.Core;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public class DxfExportSafetyTests
{
    private static string PathForTest()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);var dir=Path.Combine(root!.FullName,"work","dxf-export-safety");Directory.CreateDirectory(dir);
        return Path.Combine(dir,Guid.NewGuid().ToString("N")+".dxf");
    }
    [Fact] public void IncompletePieceRejectsBeforeReplacingExistingFile()
    {
        var path=PathForTest();try { File.WriteAllText(path,"original"); var d=new ProjectDocument { Pieces=[new PatternPiece { Closed=false, Points=[new(0,0)] }] }; Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(d,path)); Assert.Equal("original",File.ReadAllText(path)); } finally { File.Delete(path); }
    }
    [Fact] public void CollidingLayerNamesRemainDistinctAfterRoundTrip()
    {
        var path=PathForTest();try { var d=new ProjectDocument { Pieces=[new PatternPiece { Name="a/b",Closed=false,Points=[new(0,0),new(5,0)] },new PatternPiece { Name="a?b",Closed=false,Points=[new(0,1),new(5,1)] }] };DxfFormat.Export(d,path);var result=DxfFormat.Import(path); Assert.Equal(2,result.Project.Pieces.Count); Assert.Equal(2,result.Project.Pieces.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()); } finally { File.Delete(path); }
    }
}
