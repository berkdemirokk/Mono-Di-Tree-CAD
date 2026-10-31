using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Documents quantization loss that existing export checks do not certify.</summary>
public sealed class HpglLocalTurnPreservationTests
{
    private static string Target()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root not found.");
        var directory=Path.Combine(root.FullName,"work","hpgl-local-turn-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);return Path.Combine(directory,"output.plt");
    }

    // Investigative evidence, not a safety or manufacturing certificate: an
    // accepted path can still lose geometry smaller than one plotter grid unit.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingExportCanFlattenSmallNotchDespitePreservedWinding(bool reverse)
    {
        var points=new List<Point2> { new(0,0),new(10,0),new(10,10),new(6,10),new(5,9.99),new(4,10),new(0,10) };
        if(reverse)points.Reverse();
        var original=points.ToArray();var project=new ProjectDocument { Pieces=[new PatternPiece { Points=points,Closed=true }] };
        var path=Target();HpglFormat.Export(project,path,40);
        var text=File.ReadAllText(path);
        Assert.Contains(reverse ? "160,400,200,400,240,400" : "240,400,200,400,160,400",text);
        Assert.Equal(original,points);
    }

    [Fact]
    public void ExistingExportCanReverseAThinOpenLineTurn()
    {
        var points=new List<Point2> { new(.49,.49),new(1.49,.51),new(1.51,1.49) };
        var project=new ProjectDocument { Pieces=[new PatternPiece { Closed=false,Points=points }] };
        var path=Target();HpglFormat.Export(project,path,1);
        Assert.Contains("PU0,0;PD1,1,2,1;PU;",File.ReadAllText(path));
        Assert.True((points[1].X-points[0].X)*(points[2].Y-points[1].Y)
            -(points[1].Y-points[0].Y)*(points[2].X-points[1].X)>0);
    }
}
