using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class HpglSourceTopologyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsSourceCrossingEvenWhenRoundingRemovesIt(bool reverse)
    {
        var points=new List<Point2> { new(0,1),new(10,1),new(9.988,0.2),new(10.014,2) };
        if(reverse)points.Reverse();
        Assert.True(Geometry.HasSelfIntersections(points,false));
        var project=new ProjectDocument { Pieces=[new PatternPiece { Name="kesişim",Closed=false,Points=points }] };
        var error=Assert.Throws<InvalidDataException>(()=>HpglFormat.Prepare(project,40));
        Assert.Contains("kaynak",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowsNonCrossingNeighborDespiteCloseSourceGeometry()
    {
        var project=new ProjectDocument { Pieces=[new PatternPiece { Name="yakın",Closed=false,Points=[new(0,1),new(10,1),new(9.988,0.2),new(10.016,2)] }] };
        Assert.Contains("PU0,40;PD400,40,400,8,401,80",HpglFormat.Prepare(project,40).Text);
    }

    [Fact]
    public void RejectedSourceCrossingDoesNotReplaceExistingOutput()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);var directory=Path.Combine(root!.FullName,"work","hpgl-source-topology-tests");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".plt");
        try
        {
            File.WriteAllText(path,"previous output");
            var project=new ProjectDocument { Pieces=[new PatternPiece { Name="kesişim",Closed=false,Points=[new(0,1),new(10,1),new(9.988,0.2),new(10.014,2)] }] };
            Assert.Throws<InvalidDataException>(()=>HpglFormat.Export(project,path,40));
            Assert.Equal("previous output",File.ReadAllText(path));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }

    [Fact]
    public void ExistingIdenticalConsecutivePointsAndExplicitClosureRemainSupported()
    {
        var project=new ProjectDocument { Pieces=[
            new PatternPiece { Name="açık",Closed=false,Points=[new(0,0),new(5,0),new(5,0),new(5,5)] },
            new PatternPiece { Name="kapalı",Closed=true,Points=[new(20,0),new(30,0),new(30,10),new(20,10),new(20,0)] }
        ] };
        var text=HpglFormat.Prepare(project,40).Text;
        Assert.Contains("PU0,0;PD200,0,200,0,200,200",text);
        Assert.Contains("PU800,0;PD1200,0,1200,400,800,400,800,0",text);
    }

    [Fact]
    public void PreCancellationStopsSourceTopologyWork()
    {
        using var token=new CancellationTokenSource();token.Cancel();
        var project=new ProjectDocument { Pieces=[new PatternPiece { Name="çizgi",Closed=false,Points=[new(0,0),new(1,1)] }] };
        Assert.Throws<OperationCanceledException>(()=>HpglFormat.Prepare(project,40,token.Token));
    }

    [Fact]
    public void ExactSourceTopologyMatchesIntegerDiagnosticForSeededPaths()
    {
        var random=new Random(20261101);
        for(int run=0;run<500;run++)
        {
            int count=random.Next(2,18);
            var integers=Enumerable.Range(0,count).Select(_=>new HpglIntersectionDiagnostic.Point(random.Next(-20,21),random.Next(-20,21))).ToArray();
            var points=integers.Select(p=>new Point2(p.X,p.Y)).ToArray();var before=points.ToArray();
            foreach(var closed in new[] { false,true })
            {
                var integerError=Record.Exception(()=>HpglIntersectionDiagnostic.Validate(integers,closed));
                var sourceError=Record.Exception(()=>HpglSourceTopology.Validate(points,closed));
                Assert.Equal(integerError is null,sourceError is null);
                if(sourceError is not null)Assert.IsType<InvalidDataException>(sourceError);
            }
            Assert.Equal(before,points);
        }
    }
}
