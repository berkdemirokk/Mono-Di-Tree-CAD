using System.Globalization;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class PdfTiledFormatTests
{
    [Fact]
    public void FourSheetsHaveA4MediaBoxOrderedLabelsClipAndPhysicalScale()
    {
        var path=Path.Combine(Path.GetTempPath(),$"tekstil-tiled-{Guid.NewGuid():N}.pdf");
        var project=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(300,0),new(300,300),new(0,300)] }] };
        PdfTiledFormat.Export(project,path,ProjectStorage.CaptureVersion(path));
        var pdf=File.ReadAllText(path,Encoding.ASCII);
        Assert.Contains("/Count 4",pdf);
        Assert.Equal(4,pdf.Split("/MediaBox [0 0 595.275591 841.889764]").Length-1);
        Assert.Contains("ROW 1/2  COL 1/2  PAGE 1/4",pdf);
        Assert.Contains("ROW 1/2  COL 2/2  PAGE 2/4",pdf);
        Assert.Contains("ROW 2/2  COL 1/2  PAGE 3/4",pdf);
        Assert.Contains("ROW 2/2  COL 2/2  PAGE 4/4",pdf);
        Assert.Equal(4,pdf.Split(" re W n ").Length-1);
        Assert.Contains("28.346457 14.173228 m 170.07874 14.173228 l S",pdf); // 50 mm check
        Assert.Contains("31.181102 810.708661 m",pdf); // 1 mm from top-left frame
        var offsetStart=pdf.LastIndexOf("startxref\n",StringComparison.Ordinal)+"startxref\n".Length;
        var offsetEnd=pdf.IndexOf('\n',offsetStart);
        var offset=int.Parse(pdf[offsetStart..offsetEnd],CultureInfo.InvariantCulture);
        Assert.Equal("xref",pdf.Substring(offset,4));
    }

    [Fact]
    public void ExcessivePagesAndCollapsedEdgesPreserveExistingDestination()
    {
        var path=Path.Combine(Path.GetTempPath(),$"tekstil-tiled-{Guid.NewGuid():N}.pdf");File.WriteAllText(path,"original");
        var source=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(10000,0),new(0,10000)] }] };
        Assert.Throws<InvalidDataException>(()=>PdfTiledFormat.Export(source,path,ProjectStorage.CaptureVersion(path)));
        source.Pieces[0].Points=[new(0,0),new(10,0),new(0,10),new(0.00000001,0)];
        Assert.Throws<InvalidDataException>(()=>PdfTiledFormat.Export(source,path,ProjectStorage.CaptureVersion(path)));
        Assert.Equal("original",File.ReadAllText(path));
    }

    [Fact]
    public void TargetConflictAndUnsupportedGrainlineDoNotOverwrite()
    {
        var path=Path.Combine(Path.GetTempPath(),$"tekstil-tiled-{Guid.NewGuid():N}.pdf");
        var expected=ProjectStorage.CaptureVersion(path);File.WriteAllText(path,"external");
        var source=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] }] };
        Assert.Throws<ProjectConflictException>(()=>PdfTiledFormat.Export(source,path,expected));
        source.Pieces[0].Grainline=new Grainline { Start=new(1,1),End=new(1,5) };
        Assert.Throws<InvalidDataException>(()=>PdfTiledFormat.Export(source,path,ProjectStorage.CaptureVersion(path)));
        Assert.Equal("external",File.ReadAllText(path));
    }
}
