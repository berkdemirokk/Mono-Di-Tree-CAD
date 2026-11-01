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
        Assert.Contains("28.346457 42.519685 m 170.07874 42.519685 l S",pdf); // 50 mm check, 15 mm from edge
        Assert.Contains("31.181102 782.362205 m",pdf); // 1 mm from top-left frame
        Assert.Contains("28.346457 56.692913 538.582677 728.503937 re W n",pdf); // 190 x 257 mm
        Assert.Contains("28.346457 799.370079 Td (ROW 1/2",pdf); // label 15 mm from top
        var sections=pdf.Split(" re W n 0 G 0.5 w 1 J 1 j\n").Skip(1).ToArray();
        var positions=sections.Select(s=>s[..s.IndexOf('\n')].Split(' '))
            .Select(v=>(X:double.Parse(v[0],CultureInfo.InvariantCulture),Y:double.Parse(v[1],CultureInfo.InvariantCulture))).ToArray();
        Assert.Equal(4,positions.Length);
        Assert.InRange(Math.Abs((positions[0].X-positions[1].X)-190*72.0/25.4),0,0.000002);
        Assert.InRange(Math.Abs((positions[2].Y-positions[0].Y)-257*72.0/25.4),0,0.000002);
        var offsetStart=pdf.LastIndexOf("startxref\n",StringComparison.Ordinal)+"startxref\n".Length;
        var offsetEnd=pdf.IndexOf('\n',offsetStart);
        var offset=int.Parse(pdf[offsetStart..offsetEnd],CultureInfo.InvariantCulture);
        Assert.Equal("xref",pdf.Substring(offset,4));
    }

    [Theory]
    [InlineData(255,1)]
    [InlineData(255.001,2)]
    public void VerticalPageCountCoversBothSidesOfTileBoundary(double height,int expectedPages)
    {
        var path=Path.Combine(Path.GetTempPath(),$"tekstil-tiled-{Guid.NewGuid():N}.pdf");
        var source=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(10,0),new(0,height)] }] };
        PdfTiledFormat.Export(source,path,ProjectStorage.CaptureVersion(path));
        Assert.Contains($"/Count {expectedPages}",File.ReadAllText(path,Encoding.ASCII));
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
