using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class GrainlineDisplayTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(0.000001)]
    [InlineData(100000000)]
    public void ArrowPointsToEndAndReversesWithoutChangingSource(double length)
    {
        var start=new Point2(0,0);var end=new Point2(0,length);
        var forward=GrainlineDisplay.CreateArrow(start,end,2);
        var reverse=GrainlineDisplay.CreateArrow(end,start,2);
        Assert.Equal(end,forward.Tip);Assert.Equal(start,reverse.Tip);
        Assert.True(forward.Left.Y<forward.Tip.Y && forward.Right.Y<forward.Tip.Y);
        Assert.True(reverse.Left.Y>reverse.Tip.Y && reverse.Right.Y>reverse.Tip.Y);
        Assert.InRange(Math.Abs(forward.Left.X),0,0.7);
    }

    [Fact]
    public void DegenerateOrUnrepresentableArrowsAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>GrainlineDisplay.CreateArrow(new(0,0),new(0,0),2));
        Assert.Throws<ArgumentException>(()=>GrainlineDisplay.CreateArrow(new(0,0),new(double.NaN,0),2));
        Assert.Throws<ArgumentException>(()=>GrainlineDisplay.CreateArrow(new(0,0),new(double.Epsilon,0),2));
        Assert.Throws<ArgumentException>(()=>GrainlineDisplay.CreateArrow(new(1e16,0),new(1e16,100),2));
    }

    [Fact]
    public void PdfPreservesArrowReviewLegendClipAndSourceWithoutOverwritingOnTinyArrow()
    {
        var folder=Path.Combine(Path.GetTempPath(),"grain-display-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var source=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(300,0),new(300,300)],
                Grainline=new Grainline { Start=new(50,20),End=new(50,120),NeedsReview=true } }] };
            var before=source.Clone();var path=Path.Combine(folder,"forward.pdf");
            PdfTiledFormat.Export(source,path,ProjectStorage.CaptureVersion(path));
            var text=File.ReadAllText(path);
            Assert.Equal(4,text.Split("KONTROL BEKLEYEN: 1").Length-1);
            Assert.Equal(4,text.Split("(KONTROL BEKLIYOR)").Length-1);
            Assert.Equal(4,text.Split("re W n").Length-1);
            Assert.Contains("OK: BASLANGICTAN BITISE",text);
            string Map(Point2 p) => ((10+p.X+1)*72/25.4).ToString("0.######",System.Globalization.CultureInfo.InvariantCulture)+" "+
                ((20+257-(p.Y+1))*72/25.4).ToString("0.######",System.Globalization.CultureInfo.InvariantCulture);
            var arrow=GrainlineDisplay.CreateArrow(new(50,20),new(50,120),2);
            Assert.Contains($"[] 0 d {Map(arrow.Left)} m {Map(arrow.Tip)} l {Map(arrow.Right)} l S",text);
            Assert.True(DocumentSession.ContentEquals(before,source));
            var line=source.Pieces[0].Grainline!;(line.Start,line.End)=(line.End,line.Start);line.NeedsReview=false;
            var other=Path.Combine(folder,"reverse.pdf");PdfTiledFormat.Export(source,other,ProjectStorage.CaptureVersion(other));
            var reversed=File.ReadAllText(other);Assert.NotEqual(text,reversed);
            var reversedArrow=GrainlineDisplay.CreateArrow(line.Start,line.End,2);
            Assert.Contains($"[] 0 d {Map(reversedArrow.Left)} m {Map(reversedArrow.Tip)} l {Map(reversedArrow.Right)} l S",reversed);
            Assert.Contains("KONTROL BEKLEYEN: 0",reversed);Assert.DoesNotContain("(KONTROL BEKLIYOR)",reversed);
            line.Start=new(1,1);line.End=new(1.0000001,1);
            Assert.Throws<InvalidDataException>(()=>PdfTiledFormat.Export(source,path,ProjectStorage.CaptureVersion(path)));
            Assert.Equal(text,File.ReadAllText(path));
        }
        finally { Directory.Delete(folder,true); }
    }
}
