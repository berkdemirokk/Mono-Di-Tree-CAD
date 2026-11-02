using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class HpglPreparedGeometryPreviewTests
{
    [Fact]
    public void PreviewMatchesEncodedCoordinatesAndIsImmutableAfterSourceChanges()
    {
        var piece=new PatternPiece { Closed=false,Points=[new(.01,.02),new(1.01,.99)] };
        var original=piece.Points.ToArray();var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[piece] },40);
        var preview=prepared.GetPieceGeometry(piece.Id);
        Assert.Equal(piece.Id,preview.PieceId);Assert.False(preview.Closed);Assert.Equal(40,preview.UnitsPerMillimeter);
        Assert.Equal(new HpglPreviewPoint(.01,.02,0,1),preview.Points[0]);
        Assert.Equal(new HpglPreviewPoint(1.01,.99,40,40),preview.Points[1]);
        Assert.Contains("PU0,1;PD40,40;PU;",prepared.Text);
        piece.Points.Clear();
        Assert.Equal(original[0].X,prepared.GetPieceGeometry(piece.Id).Points[0].SourceX);
        var list=Assert.IsAssignableFrom<IList<HpglPreviewPoint>>(preview.Points);
        Assert.Throws<NotSupportedException>(()=>list[0]=new HpglPreviewPoint());
    }

    [Fact]
    public void SimulatorPreviewIncludesCapturedScaleAndClosedDuplicateEndpoint()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root missing.");
        var queue=new SimulationQueue(Path.Combine(root.FullName,"work","hpgl-preview-geometry-tests",Guid.NewGuid().ToString("N")));
        var piece=new PatternPiece { Points=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)] };
        var proposal=queue.PrepareHpglReview(new ProjectDocument { Pieces=[piece] },new MachineProfile("Sim",20,2));
        var preview=proposal.Output.GetPieceGeometry(piece.Id);
        Assert.True(preview.Closed);Assert.Equal(20,preview.UnitsPerMillimeter);Assert.Equal(5,preview.Points.Count);
        Assert.Equal(new HpglPreviewPoint(20,0,400,0),preview.Points[1]);
        Assert.Equal(preview.Points[0],preview.Points[^1]);
    }

    [Fact]
    public void QueueScaleAndTranslatedSourceMatchEncodedPathAndDisplacementReview()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        if(root is null)throw new InvalidOperationException("Repository root missing.");
        var spool=Path.Combine(root.FullName,"work","hpgl-preview-geometry-tests",Guid.NewGuid().ToString("N"));
        var queue=new SimulationQueue(spool);
        var piece=new PatternPiece { Closed=false,Points=[new(.25,-.25),new(10.25,-.25)] };
        var original=piece.Points.ToArray();var proposal=queue.PrepareHpglReview(new ProjectDocument { Pieces=[piece] },new MachineProfile("Sim",2,1.5));
        var captured=proposal.Output.GetPieceGeometry(piece.Id);var review=HpglLengthReview.Analyze(captured);
        Assert.Equal(new HpglPreviewPoint(.375,-.375,1,-1),captured.Points[0]);
        Assert.Equal(new HpglPreviewPoint(15.375,-.375,31,-1),captured.Points[1]);
        Assert.Contains("PU1,-1;PD31,-1;PU;",proposal.Output.Text);
        Assert.Equal(0,review.WorstDisplacementIndex);Assert.Equal(.125,review.WorstDisplacementXmm,12);Assert.Equal(-.125,review.WorstDisplacementYmm,12);
        Assert.Equal(Math.Sqrt(.03125),review.WorstDisplacementMm,12);
        Assert.Equal(15,review.SourceLengthMm,12);Assert.Equal(15,review.EncodedLengthMm,12);
        Assert.Equal(original,piece.Points);Assert.False(Directory.Exists(spool));Assert.Empty(queue.Jobs);
    }

    [Fact]
    public void UnknownPieceAndCancellationAreRejectedWithoutChangingPreparedText()
    {
        var p=new PatternPiece { Closed=false,Points=[new(0,0),new(2,0)] };
        var prepared=HpglFormat.Prepare(new ProjectDocument { Pieces=[p] });var text=prepared.Text;
        Assert.Throws<KeyNotFoundException>(()=>prepared.GetPieceGeometry(Guid.NewGuid()));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>prepared.GetPieceGeometry(p.Id,cancellation.Token));
        Assert.Equal(text,prepared.Text);
    }
}
