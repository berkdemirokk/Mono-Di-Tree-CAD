using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HpglPreparedOutputTests
{
    [Fact]
    public void OutputBoundsCoverEveryEncodedPieceAndRemainACapturedSnapshot()
    {
        var project=new ProjectDocument { Pieces=[
            new PatternPiece { Name="bir",Closed=false,Points=[new(-0.026,2),new(5,3)] },
            new PatternPiece { Name="iki",Closed=false,Points=[new(1,-4),new(8,9)] }
        ] };
        var output=HpglFormat.Prepare(project,40);
        Assert.Equal(new HpglOutputBounds(-1,-160,320,360),output.Bounds);
        Assert.True(output.Bounds.HasNegativeCoordinates);
        Assert.Contains("PU-1,80;PD200,120",output.Text);
        Assert.Contains("PU40,-160;PD320,360",output.Text);
        project.Pieces[0].Points[0]=new(2,2);
        Assert.Equal(-1,output.Bounds.MinX);
    }

    [Fact]
    public void NegativeSourceRoundedToZeroIsNotARecordedNegativeCoordinate()
    {
        var project=new ProjectDocument { Pieces=[new PatternPiece { Name="çizgi",Closed=false,Points=[new(-0.01,0),new(1,1)] }] };
        var output=HpglFormat.Prepare(project,40);
        Assert.Equal(new HpglOutputBounds(0,0,40,40),output.Bounds);
        Assert.False(output.Bounds.HasNegativeCoordinates);
        Assert.Contains("PU0,0;PD40,40",output.Text);
    }
    private static string Target(string file)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root not found.");
        var directory=Path.Combine(root.FullName,"work","hpgl-prepared-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);return Path.Combine(directory,file);
    }
    private static ProjectDocument Source()=>new() { Pieces=[new PatternPiece { Name="Çentik",Points=[new(0,0),new(10,0),new(10,10),new(6,10),new(5,9.99),new(4,10),new(0,10)] }] };

    [Fact]
    public void PreparedBytesAndReviewsReferToCapturedGeometry()
    {
        var project=Source();var original=project.Pieces[0].Points.ToArray();
        var prepared=HpglFormat.Prepare(project,40);
        var report=Assert.Single(prepared.TurnReviews);
        Assert.Equal(project.Pieces[0].Id,report.PieceId);Assert.Equal(40,report.UnitsPerMillimeter);
        Assert.True(report.FlattenedTurns>0);Assert.Equal(original,project.Pieces[0].Points);
        var previousText=prepared.Text;project.Pieces[0].Points[4]=new(5,8);
        Assert.Equal(previousText,prepared.Text);Assert.True(report.FlattenedTurns>0);
        var path=Target("prepared.plt");var expected=ProjectStorage.CaptureVersion(path);
        HpglFormat.WritePrepared(prepared,path,expected);
        Assert.Equal(previousText,File.ReadAllText(path));
    }

    [Fact]
    public void PreparedContentMatchesLegacyExportForSameSnapshot()
    {
        var project=Source();var prepared=HpglFormat.Prepare(project,40);
        var path=Target("legacy.plt");HpglFormat.Export(project,path,40);
        Assert.Equal(prepared.Text,File.ReadAllText(path));
    }

    [Fact]
    public void ChangedDestinationIsNeverOverwrittenByPreparedWrite()
    {
        var prepared=HpglFormat.Prepare(Source(),40);var path=Target("conflict.plt");
        File.WriteAllText(path,"original");var expected=ProjectStorage.CaptureVersion(path);
        File.WriteAllText(path,"externally changed");
        Assert.Throws<ProjectConflictException>(()=>HpglFormat.WritePrepared(prepared,path,expected));
        Assert.Equal("externally changed",File.ReadAllText(path));
    }

    [Fact]
    public void PreCancellationLeavesSourceAndDestinationUnchanged()
    {
        var source=Source();var points=source.Pieces[0].Points.ToArray();var prepared=HpglFormat.Prepare(source,40);
        var path=Target("cancel.plt");File.WriteAllText(path,"existing");var version=ProjectStorage.CaptureVersion(path);
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>HpglFormat.Prepare(source,40,cancellation.Token));
        Assert.Throws<OperationCanceledException>(()=>HpglFormat.WritePrepared(prepared,path,version,cancellation.Token));
        Assert.Equal("existing",File.ReadAllText(path));Assert.Equal(points,source.Pieces[0].Points);
    }
}
