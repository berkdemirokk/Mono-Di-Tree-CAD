using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class HpglEdgeAdversarialTests
{
    private static string PathInWork()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "hpgl-edge-adversarial");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".plt");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonAdjacentTouchOrCrossCannotReplaceExistingFile(bool closed)
    {
        var path=PathInWork();
        try
        {
            File.WriteAllText(path,"keep");
            var doc=new ProjectDocument { Pieces=[new PatternPiece { Closed=closed, Points=[new(0,0),new(4,4),new(0,4),new(4,0)] }] };
            Assert.Throws<InvalidDataException>(()=>HpglFormat.Export(doc,path,1));
            Assert.Equal("keep",File.ReadAllText(path));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AdjacentBacktrackingIsRejectedBeforeWrite()
    {
        var path=PathInWork();
        try
        {
            File.WriteAllText(path,"keep");
            var doc=new ProjectDocument { Pieces=[new PatternPiece { Closed=false, Points=[new(0,0),new(10,0),new(5,0),new(5,4)] }] };
            Assert.Throws<InvalidDataException>(()=>HpglFormat.Export(doc,path,1));
            Assert.Equal("keep",File.ReadAllText(path));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ExplicitClosedEndpointIsAccepted()
    {
        var path=PathInWork();
        try
        {
            var doc=new ProjectDocument { Pieces=[new PatternPiece { Closed=true, Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)] }] };
            HpglFormat.Export(doc,path,1);
            Assert.Contains("PU0,0;PD",File.ReadAllText(path));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void LargeIntegerCoordinatesAreCheckedWithoutOverflow()
    {
        var path=PathInWork();
        try
        {
            var doc=new ProjectDocument { Pieces=[new PatternPiece { Closed=true, Points=[new(-100000000,-100000000),new(100000000,-100000000),new(100000000,100000000),new(-100000000,100000000)] }] };
            HpglFormat.Export(doc,path,20);
            Assert.Contains("2000000000",File.ReadAllText(path));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ExcessComparisonWorkIsRejectedWithoutReplacingTarget()
    {
        var path=PathInWork();
        try
        {
            File.WriteAllText(path,"keep");
            var points=Enumerable.Range(0,8000).Select(i=>new Point2(i,i%2)).ToList();
            // Two individually bounded 7,999-edge paths exceed 50M pairs in total.
            var other=points.Select(p=>new Point2(p.X,p.Y+10)).ToList();
            var doc=new ProjectDocument { Pieces=[new PatternPiece { Closed=false, Points=points }, new PatternPiece { Closed=false, Points=other }] };
            var ex=Assert.Throws<InvalidDataException>(()=>HpglFormat.Export(doc,path,1));
            Assert.Contains("sınırı",ex.Message);
            Assert.Equal("keep",File.ReadAllText(path));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }
}
