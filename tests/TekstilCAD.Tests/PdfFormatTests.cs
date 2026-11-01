using System.Globalization;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class PdfFormatTests
{
    [Fact]
    public void VectorPdfUsesPhysicalPageAndMatchesCanvasOrientation()
    {
        var path=Path.Combine(Path.GetTempPath(),"tekstilcad-pdf-"+Guid.NewGuid().ToString("N")+".pdf");
        var source=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(100,0),new(20,50)] }] };
        PdfFormat.Export(source,path,ProjectStorage.CaptureVersion(path));
        var text=File.ReadAllText(path,Encoding.ASCII);
        Assert.Contains("/MediaBox [0 0 311.811024 170.07874]",text);
        Assert.Contains("14.173228 155.905512 m",text);
        Assert.Contains("70.866142 14.173228 l",text);
        var start=text.LastIndexOf("startxref\n",StringComparison.Ordinal)+"startxref\n".Length;
        var end=text.IndexOf('\n',start);
        var offset=int.Parse(text[start..end],CultureInfo.InvariantCulture);
        Assert.Equal("xref",text.Substring(offset,4));
    }

    [Fact]
    public void ExistingVersionConflictAndOversizeLeaveDestinationAlone()
    {
        var path=Path.Combine(Path.GetTempPath(),"tekstilcad-pdf-"+Guid.NewGuid().ToString("N")+".pdf");
        var version=ProjectStorage.CaptureVersion(path);
        File.WriteAllText(path,"existing");
        var source=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] }] };
        Assert.Throws<ProjectConflictException>(()=>PdfFormat.Export(source,path,version));
        source.Pieces[0].Points=[new(0,0),new(10000,0),new(0,10)];
        Assert.Throws<InvalidDataException>(()=>PdfFormat.Export(source,path,ProjectStorage.CaptureVersion(path)));
        Assert.Equal("existing",File.ReadAllText(path));
    }

    [Fact]
    public void RoundedClosingEdgeIsRejectedWithoutOverwritingTarget()
    {
        var path=Path.Combine(Path.GetTempPath(),"tekstilcad-pdf-"+Guid.NewGuid().ToString("N")+".pdf");
        File.WriteAllText(path,"keep");
        var source=new ProjectDocument { Pieces=[new PatternPiece { Points=[new(0,0),new(10,0),new(0,10),new(0.00000001,0)] }] };
        Assert.Throws<InvalidDataException>(()=>PdfFormat.Export(source,path,ProjectStorage.CaptureVersion(path)));
        Assert.Equal("keep",File.ReadAllText(path));
        source.Pieces[0].Points[^1]=source.Pieces[0].Points[0];
        PdfFormat.Export(source,path,ProjectStorage.CaptureVersion(path));
        Assert.StartsWith("%PDF-1.4",File.ReadAllText(path));
    }
}
