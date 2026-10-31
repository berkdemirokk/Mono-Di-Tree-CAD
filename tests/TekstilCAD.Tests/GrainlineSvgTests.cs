using System.Globalization;
using System.Xml.Linq;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class GrainlineSvgTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static string Target()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var dir=Path.Combine(root.FullName,"work","grainline-svg-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir,"drawing.svg");
    }
    private static double Number(XAttribute? a) => double.Parse(Assert.IsType<XAttribute>(a).Value, CultureInfo.InvariantCulture);
    private static double[] ViewBox(XElement root) => Assert.IsType<XAttribute>(root.Attribute("viewBox")).Value
        .Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
    private static ProjectDocument Sample()
    {
        return new ProjectDocument { Name="Model", Pieces=[
            new PatternPiece { Name="Ön & <kol> 🧵", Points=[new(-10,-5),new(10,-5),new(10,15),new(-10,15)],
                Grainline=new Grainline { Start=new(-20,-8),End=new(12.5,25),NeedsReview=false } },
            new PatternPiece { Name="Arka & yaka", Points=[new(40,-3),new(55,-3),new(55,10),new(40,10)],
                Grainline=new Grainline { Start=new(42.25,-1.5),End=new(52.75,8.5),NeedsReview=true } }
        ] };
    }

    [Fact]
    public void SvgPreservesBothGrainlinesCoordinatesPieceAssociationAndReviewState()
    {
        var project=Sample();var before=project.Clone();var path=Target();
        SvgFormat.Export(project,path);
        var root=Assert.IsType<XElement>(XDocument.Load(path).Root);
        Assert.Equal(Svg+"svg",root.Name);
        Assert.Equal(2,root.Descendants(Svg+"path").Count());
        var group=Assert.Single(root.Descendants(Svg+"g"),g=>(string?)g.Attribute("id")=="tekstilcad-grainlines");
        Assert.Equal("grainline",(string?)group.Attribute("data-tekstilcad-role"));
        var lines=group.Elements(Svg+"line").ToArray();Assert.Equal(2,lines.Length);
        foreach(var piece in project.Pieces)
        {
            var grain=Assert.IsType<Grainline>(piece.Grainline);
            var line=Assert.Single(lines,l=>(string?)l.Attribute("data-piece-id")==piece.Id.ToString("D"));
            Assert.Equal(grain.NeedsReview ? "true" : "false",(string?)line.Attribute("data-needs-review"));
            Assert.Equal(grain.Start.X,Number(line.Attribute("x1")));Assert.Equal(grain.Start.Y,Number(line.Attribute("y1")));
            Assert.Equal(grain.End.X,Number(line.Attribute("x2")));Assert.Equal(grain.End.Y,Number(line.Attribute("y2")));
        }
        Assert.Equal("Ön & <kol> 🧵",root.Descendants(Svg+"title").First().Value);
        var box=ViewBox(root);Assert.Equal(4,box.Length);
        foreach(var point in project.Pieces.SelectMany(p=>p.Points.Concat([p.Grainline!.Start,p.Grainline.End])))
        {
            Assert.True(point.X>box[0] && point.X<box[0]+box[2]);
            Assert.True(point.Y>box[1] && point.Y<box[1]+box[3]);
        }
        Assert.True(DocumentSession.ContentEquals(before,project));
    }

    [Fact]
    public void ExtremeCoordinatesRemainFiniteAndEndpointsFitViewport()
    {
        var piece=new PatternPiece { Points=[new(-99999990,-99999990),new(-99999980,-99999990),new(-99999980,-99999980)],
            Grainline=new Grainline { Start=new(-100000000,99999999),End=new(99999999,-100000000),NeedsReview=true } };
        var path=Target();SvgFormat.Export(new ProjectDocument { Pieces=[piece] },path);
        var root=Assert.IsType<XElement>(XDocument.Load(path).Root);var box=ViewBox(root);
        Assert.All(box,n=>Assert.True(double.IsFinite(n)));Assert.True(box[2]>0 && box[3]>0);
        var line=Assert.Single(root.Descendants(Svg+"line"));
        Assert.Equal(piece.Grainline.Start.X,Number(line.Attribute("x1")));Assert.Equal(piece.Grainline.End.Y,Number(line.Attribute("y2")));
        foreach(var point in new[]{piece.Grainline.Start,piece.Grainline.End})
        { Assert.InRange(point.X,box[0],box[0]+box[2]);Assert.InRange(point.Y,box[1],box[1]+box[3]); }
    }

    [Fact]
    public void UnmarkedGeometryStillExportsWithoutInventingGrainline()
    {
        var piece=new PatternPiece { Name="Düz", Closed=false, Points=[new(1,2),new(3,4)] };
        var path=Target();SvgFormat.Export(new ProjectDocument { Pieces=[piece] },path);
        var root=Assert.IsType<XElement>(XDocument.Load(path).Root);
        Assert.Empty(root.Descendants(Svg+"line"));
        Assert.Equal("M 1 2 L 3 4",(string?)Assert.Single(root.Descendants(Svg+"path")).Attribute("d"));
    }

    [Fact]
    public void ConditionalConflictPreservesExistingFileAndSuccessfulRetryIncludesGrainline()
    {
        var path=Target();File.WriteAllText(path,"önceki");var version=ProjectStorage.CaptureVersion(path);
        File.WriteAllText(path,"başka çalışma");var bytes=File.ReadAllBytes(path);var project=Sample();var before=project.Clone();
        Assert.Throws<ProjectConflictException>(()=>SvgFormat.Export(project,path,version));
        Assert.Equal(bytes,File.ReadAllBytes(path));Assert.True(DocumentSession.ContentEquals(before,project));
        SvgFormat.Export(project,path,ProjectStorage.CaptureVersion(path));
        Assert.Equal(2,XDocument.Load(path).Descendants(Svg+"line").Count());
    }

    [Fact]
    public void InvalidGrainlineNeverReplacesExistingFile()
    {
        var path=Target();File.WriteAllText(path,"önceki kullanıcı dosyası");var bytes=File.ReadAllBytes(path);
        var project=Sample();project.Pieces[0].Grainline!.End=project.Pieces[0].Grainline!.Start;
        Assert.Throws<InvalidDataException>(()=>SvgFormat.Export(project,path));
        Assert.Equal(bytes,File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));
    }
}
