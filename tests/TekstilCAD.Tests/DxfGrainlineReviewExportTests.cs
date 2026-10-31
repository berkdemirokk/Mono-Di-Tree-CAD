using System.Text;
using System.Security.Cryptography;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfGrainlineReviewExportTests
{
    private static ProjectDocument Source()
    {
        var id=Guid.Parse("00000000-0000-0000-0000-000000000001");
        return new ProjectDocument { Name="test", Pieces = [
            new PatternPiece { Id=id, Name="front", Points=[new(0,0),new(10,0),new(0,10)], Grainline=new Grainline { Start=new(2,1),End=new(2,7),NeedsReview=true } },
            new PatternPiece { Name="TCD_GRAIN_00000000000000000000000000000001",Points=[new(20,0),new(30,0),new(20,10)] }
        ] };
    }
    [Fact]
    public void PlanSeparatesMarkerLayersAndDoesNotChangeSource()
    {
        var source=Source();var before=source.Clone();
        var plan=DxfGrainlineReviewExport.Prepare(source);
        Assert.True(DocumentSession.ContentEquals(before,source));
        Assert.True(plan.Matches(source));
        Assert.Equal(2,plan.Items.Length);
        var marker=plan.Items[0];
        Assert.NotNull(marker.MarkerLayer);
        Assert.DoesNotContain(plan.Items, item=>string.Equals(item.ContourLayer,marker.MarkerLayer,StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new Point2(2,1),marker.Start);
        Assert.True(marker.NeedsReview);
        Assert.Null(plan.Items[1].MarkerLayer);
        Assert.Contains(plan.Omissions, x=>x.Kind==DxfExportOmissionKind.GrainlineReviewState);
        Assert.Contains(plan.Omissions, x=>x.Kind==DxfExportOmissionKind.PieceIdentity);
        Assert.DoesNotContain(plan.Omissions,x=>x.Kind==DxfExportOmissionKind.GrainlineUnsupported);
        source.Pieces[0].Grainline!.NeedsReview=false;
        Assert.False(plan.Matches(source));
    }
    [Fact]
    public void ReviewWriteUsesPreparedBytesAndStandardExportStillRejectsGrainline()
    {
        var directory=Path.Combine(Path.GetTempPath(),"dxf-grain-review-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"review.dxf");var standard=Path.Combine(directory,"standard.dxf");
            var source=Source();var plan=DxfGrainlineReviewExport.Prepare(source);
            Assert.ThrowsAny<Exception>(()=>DxfFormat.Export(source,standard));
            DxfGrainlineReviewExport.Write(plan,path,ProjectStorage.CaptureVersion(path));
            var bytes=File.ReadAllBytes(path);
            Assert.Equal(plan.ByteLength,bytes.Length);
            Assert.Equal(plan.Sha256,Convert.ToHexString(SHA256.HashData(bytes)));
            var text=Encoding.UTF8.GetString(bytes);
            Assert.Contains("0\nLINE\n",text);
            Assert.Contains("8\n"+plan.Items[0].MarkerLayer+"\n",text);
            Assert.Contains("10\n2\n20\n1\n30\n0\n11\n2\n21\n7\n31\n0\n",text);
        }
        finally { Directory.Delete(directory,true); }
    }
    [Fact]
    public void ChangedDestinationAndCancelledWritePreserveExistingBytes()
    {
        var directory=Path.Combine(Path.GetTempPath(),"dxf-grain-review-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"review.dxf");File.WriteAllText(path,"old");
            var expected=ProjectStorage.CaptureVersion(path);var plan=DxfGrainlineReviewExport.Prepare(Source());
            File.WriteAllText(path,"other");
            Assert.Throws<ProjectConflictException>(()=>DxfGrainlineReviewExport.Write(plan,path,expected));
            Assert.Equal("other",File.ReadAllText(path));
            using var cts=new CancellationTokenSource();cts.Cancel();
            Assert.Throws<OperationCanceledException>(()=>DxfGrainlineReviewExport.Write(plan,path,ProjectStorage.CaptureVersion(path),cts.Token));
            Assert.Equal("other",File.ReadAllText(path));
        }
        finally { Directory.Delete(directory,true); }
    }
}
