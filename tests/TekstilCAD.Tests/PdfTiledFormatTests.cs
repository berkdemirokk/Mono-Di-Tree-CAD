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
        Assert.Contains("SATIR 1/2  SUTUN 1/2  SAYFA 1/4",pdf);
        Assert.Contains("SATIR 1/2  SUTUN 2/2  SAYFA 2/4",pdf);
        Assert.Contains("SATIR 2/2  SUTUN 1/2  SAYFA 3/4",pdf);
        Assert.Contains("SATIR 2/2  SUTUN 2/2  SAYFA 4/4",pdf);
        Assert.Equal(4,pdf.Split(" re W n ").Length-1);
        Assert.Contains("28.346457 42.519685 m 170.07874 42.519685 l S",pdf); // 50 mm check, 15 mm from edge
        Assert.Contains("31.181102 782.362205 m",pdf); // 1 mm from top-left frame
        Assert.Contains("28.346457 56.692913 538.582677 728.503937 re W n",pdf); // 190 x 257 mm
        Assert.Contains("28.346457 799.370079 Td (SATIR 1/2",pdf); // label 15 mm from top
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
    public void TargetConflictAndCollapsedGrainlineDoNotOverwrite()
    {
        var path=Path.Combine(Path.GetTempPath(),$"tekstil-tiled-{Guid.NewGuid():N}.pdf");
        var expected=ProjectStorage.CaptureVersion(path);File.WriteAllText(path,"external");
        var source=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] }] };
        Assert.Throws<ProjectConflictException>(()=>PdfTiledFormat.Export(source,path,expected));
        source.Pieces[0].Grainline=new Grainline { Start=new(1,1),End=new(1+1e-9,1) };
        Assert.Throws<InvalidDataException>(()=>PdfTiledFormat.Export(source,path,ProjectStorage.CaptureVersion(path)));
        Assert.Equal("external",File.ReadAllText(path));
    }
    [Fact]
    public void GrainlineUsesSameScaleAndClipAcrossAllSheets()
    {
        var path=Path.Combine(Path.GetTempPath(),$"tekstil-tiled-grain-{Guid.NewGuid():N}.pdf");
        try
        {
            var piece=new PatternPiece { Points=[new(0,0),new(300,0),new(300,300),new(0,300)],Grainline=new Grainline { Start=new(100,100),End=new(250,200),NeedsReview=true } };
            var source=new ProjectDocument { Pieces=[piece] };var before=source.Clone();
            PdfTiledFormat.Export(source,path,ProjectStorage.CaptureVersion(path));
            var pdf=File.ReadAllText(path,Encoding.ASCII);
            var sections=pdf.Split("[4 2] 0 d 0 G 0.5 w\n").Skip(1).ToArray();
            Assert.Equal(4,sections.Length);Assert.Equal(4,pdf.Split("KESIK: IPLIK YONU").Length-1);
            var lines=sections.Select(section=>section[..section.IndexOf('\n')].Split(' '))
                .Select(v=>(X1:double.Parse(v[0],CultureInfo.InvariantCulture),Y1:double.Parse(v[1],CultureInfo.InvariantCulture),X2:double.Parse(v[3],CultureInfo.InvariantCulture),Y2:double.Parse(v[4],CultureInfo.InvariantCulture))).ToArray();
            const double scale=72.0/25.4;
            Assert.InRange(Math.Abs((lines[0].X2-lines[0].X1)-150*scale),0,0.000002);
            Assert.InRange(Math.Abs((lines[0].Y1-lines[0].Y2)-100*scale),0,0.000002);
            Assert.InRange(Math.Abs((lines[0].X1-lines[1].X1)-190*scale),0,0.000002);
            Assert.InRange(Math.Abs((lines[2].Y1-lines[0].Y1)-257*scale),0,0.000002);
            Assert.True(DocumentSession.ContentEquals(before,source));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }

    [Fact]
    public void GrainlineOutsideContourExpandsSheetCoverage()
    {
        var path=Path.Combine(Path.GetTempPath(),$"tekstil-tiled-grain-bounds-{Guid.NewGuid():N}.pdf");
        try
        {
            var source=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)],Grainline=new Grainline { Start=new(400,0),End=new(400,5) } }] };
            PdfTiledFormat.Export(source,path,ProjectStorage.CaptureVersion(path));
            Assert.Contains("/Count 3",File.ReadAllText(path,Encoding.ASCII));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }

}
