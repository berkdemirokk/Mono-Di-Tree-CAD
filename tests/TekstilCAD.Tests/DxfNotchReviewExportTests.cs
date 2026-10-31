using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfNotchReviewExportTests
{
    private static ProjectDocument Source()
    {
        var first=new PatternPiece { Name="front", Points=[new(0,0),new(10,0),new(0,10)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }] };
        var second=new PatternPiece { Name="TCD_REVIEW_NOTCH",Points=[new(20,0),new(30,0),new(20,10)],
            Notches=[new NotchMark { VertexIndex=2,Position=new(20,10) }] };
        return new ProjectDocument { Pieces=[first,second] };
    }

    [Fact]
    public void ReviewHasSeparatePointLayerAndExactCoordinatesAndStandardFormatsStillReject()
    {
        var source=Source();var before=source.Clone();
        var plan=DxfNotchReviewExport.Prepare(source);
        Assert.True(DocumentSession.ContentEquals(before,source));
        Assert.True(plan.Matches(source));
        Assert.Equal(2,plan.Items.Length);
        Assert.All(plan.Items,mark=>Assert.NotEqual(mark.ContourLayer,plan.MarkerLayer));
        Assert.Contains("mm",plan.Warning);
        Assert.Contains("makineye göndermeyin",plan.Warning);
        Assert.Throws<InvalidDataException>(()=>HpglFormat.Prepare(source));
        var directory=Path.Combine(Path.GetTempPath(),"notch-review-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"review.dxf");
            Assert.Throws<InvalidDataException>(()=>DxfFormat.Export(source,path));
            DxfNotchReviewExport.Write(plan,path,ProjectStorage.CaptureVersion(path));
            var bytes=File.ReadAllBytes(path);var text=Encoding.UTF8.GetString(bytes);
            Assert.Equal(plan.ByteLength,bytes.Length);
            Assert.Equal(plan.Sha256,Convert.ToHexString(SHA256.HashData(bytes)));
            Assert.Contains("$INSUNITS\n70\n4\n",text);
            Assert.Contains("REVIEW ONLY",text);
            Assert.Contains("8\n"+plan.MarkerLayer+"\n100\nAcDbPoint\n10\n10\n20\n0\n30\n0\n",text);
            Assert.Contains("8\n"+plan.MarkerLayer+"\n100\nAcDbPoint\n10\n20\n20\n10\n30\n0\n",text);
            Assert.Equal(2,text.Split("0\nPOINT\n",StringSplitOptions.None).Length-1);
            source.Pieces[0].Notches![0].VertexIndex=0;source.Pieces[0].Notches[0].Position=source.Pieces[0].Points[0];
            Assert.False(plan.Matches(source));
        }
        finally { Directory.Delete(directory,true); }
    }

    [Fact]
    public void OtherUnsupportedSemanticsAndUnsafeWritesAreRejected()
    {
        var source=Source();source.Pieces[0].Grainline=new Grainline { Start=new(1,1),End=new(1,2) };
        Assert.Throws<InvalidDataException>(()=>DxfNotchReviewExport.Prepare(source));
        source=Source();var plan=DxfNotchReviewExport.Prepare(source);
        var directory=Path.Combine(Path.GetTempPath(),"notch-review-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var path=Path.Combine(directory,"review.dxf");File.WriteAllText(path,"original");
            var version=ProjectStorage.CaptureVersion(path);File.WriteAllText(path,"changed");
            Assert.Throws<ProjectConflictException>(()=>DxfNotchReviewExport.Write(plan,path,version));
            Assert.Equal("changed",File.ReadAllText(path));
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            Assert.Throws<OperationCanceledException>(()=>DxfNotchReviewExport.Write(plan,path,ProjectStorage.CaptureVersion(path),cancel.Token));
            Assert.Equal("changed",File.ReadAllText(path));
        }
        finally { Directory.Delete(directory,true); }
    }
}
