using TekstilCAD.Core;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public class DxfLayerTruncationTests
{
    private static PatternPiece Piece(string name) => new() { Name=name, Closed=false, Points=[new(0,0),new(1,1)] };
    [Fact] public void TruncationWhitespaceCannotCollideAfterImport()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root);
        var directory=Path.Combine(root!.FullName,"work","dxf-truncation-tests"); Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".dxf");
        try
        {
            var prefix=new string('a',254);
            var project=new ProjectDocument { Pieces=[Piece(prefix+" b"),Piece(prefix)] };
            var names=DxfFormat.PlanLayers(project).Select(p=>p.LayerName).ToArray();
            Assert.All(names,n=>Assert.Equal(n,n.Trim()));
            Assert.Equal(2,names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            DxfFormat.Export(project,path);
            var imported=DxfFormat.Import(path).Project;
            Assert.Equal(names,imported.Pieces.Select(p=>p.Name));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }
    [Fact] public void SuffixCounterRespectsExistingNamesAndDeterministicOrder()
    {
        var project=new ProjectDocument { Pieces=[Piece("a"),Piece("a_2"),Piece("a"),Piece("A"),Piece("a_3")] };
        var first=DxfFormat.PlanLayers(project).Select(p=>p.LayerName).ToArray();
        Assert.Equal(new[]{"a","a_2","a_3","A_4","a_3_2"},first);
        Assert.Equal(first,DxfFormat.PlanLayers(project).Select(p=>p.LayerName));
    }
}
