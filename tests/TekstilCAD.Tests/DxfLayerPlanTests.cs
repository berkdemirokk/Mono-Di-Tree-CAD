using TekstilCAD.Core;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public class DxfLayerPlanTests
{
    private static PatternPiece Piece(string name) => new() { Name=name, Closed=false, Points=[new(0,0),new(10,0)] };
    [Fact] public void PlanMatchesExportAndWarnsForWhitespaceCollisionsAndLongNames()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory); while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        Assert.NotNull(root); var dir=Path.Combine(root!.FullName,"work","dxf-layer-plan"); Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,Guid.NewGuid().ToString("N")+".dxf");
        try {
            var d=new ProjectDocument { Pieces=[Piece(" Parça "),Piece("PARÇA"),Piece("a/b"),Piece("a?b"),Piece(new string('x',1024)),Piece(new string('X',1024)),Piece("  ")] };
            var plan=DxfFormat.PlanLayers(d);
            Assert.All(plan, p=>Assert.True(p.Changed));
            Assert.All(plan, p=>Assert.InRange(p.LayerName.Length,1,255));
            Assert.Equal(plan.Count,plan.Select(p=>p.LayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            DxfFormat.Export(d,path); var imported=DxfFormat.Import(path);
            Assert.Equal(plan.Select(x=>x.LayerName),imported.Project.Pieces.Select(x=>x.Name));
        } finally { if(File.Exists(path)) File.Delete(path); }
    }
    [Fact] public void PlanPreservesUnchangedNameAndDoesNotMutateSource()
    {
        var p=Piece("Ön beden");var d=new ProjectDocument { Pieces=[p] };
        var item=Assert.Single(DxfFormat.PlanLayers(d)); Assert.False(item.Changed); Assert.Equal("Ön beden",item.LayerName);Assert.Equal("Ön beden",p.Name);
    }
}
