using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HpglOrientationPreservationTests
{
    private static string Target()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root not found.");
        var directory=Path.Combine(root.FullName,"work","hpgl-orientation-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory,"existing.plt");
    }

    [Theory]
    [InlineData(false,0.0)]
    [InlineData(true,0.0)]
    [InlineData(false,50000000.0)]
    [InlineData(true,50000000.0)]
    public void QuantizationCannotReverseClosedContour(bool reverse,double origin)
    {
        var points=new List<Point2> { new(origin+.49,origin+.49),new(origin+1.49,origin+.51),new(origin+1.51,origin+1.49) };
        if(reverse)points.Reverse();
        var piece=new PatternPiece { Name="Yön koruması",Closed=true,Points=points };
        var project=new ProjectDocument { Pieces=[piece] };var before=points.ToArray();
        var path=Target();var existing=new byte[] { 3,1,4,1,5 };File.WriteAllBytes(path,existing);
        var error=Assert.Throws<InvalidDataException>(()=>HpglFormat.Export(project,path,1));
        Assert.Contains("yönü",error.Message);
        Assert.Equal(existing,File.ReadAllBytes(path));
        Assert.Equal(before,piece.Points);
    }

    [Fact]
    public void ExactlyCollinearSourceCannotBecomeClosedContourAfterQuantization()
    {
        var project=new ProjectDocument { Pieces=[new PatternPiece { Closed=true,Points=[new(.375,.75),new(.875,1.75),new(1.375,2.75)] }] };
        var path=Target();File.WriteAllText(path,"previous");
        Assert.Throws<InvalidDataException>(()=>HpglFormat.Export(project,path,1));
        Assert.Equal("previous",File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameOrientationInBothDirectionsRemainsExportable(bool reverse)
    {
        var points=new List<Point2> { new(.2,.2),new(10.2,.2),new(10.2,10.2),new(.2,10.2) };
        if(reverse)points.Reverse();
        var project=new ProjectDocument { Pieces=[new PatternPiece { Closed=true,Points=points }] };
        var path=Target();HpglFormat.Export(project,path,1);
        Assert.StartsWith("IN;SP1;PA;",File.ReadAllText(path));
    }
}
