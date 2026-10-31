using System.Globalization;
using System.Xml.Linq;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class FormatTests : IDisposable
{
    private readonly string directory;
    public FormatTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        directory = Path.Combine(root.FullName, "work", "format-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }
    private string FilePath(string name) => Path.Combine(directory, name);
    private static ProjectDocument Sample() => new()
    {
        Name = "Kalıp ölçüsü",
        Pieces = [new PatternPiece { Name = "Ön beden", Closed = true,
            Points = [new(-10.5, 2.25), new(89.5, 2.25), new(89.5, 102.25), new(-10.5, 102.25)] },
            new PatternPiece { Name = "İplik", Closed = false, Points = [new(20, 20), new(20, 80)] }]
    };
    [Fact]
    public void NativeRoundTripPreservesIdentityGeometryAndClosure()
    {
        var source = Sample(); var path = FilePath("project.tcad");
        ProjectStorage.Save(source, path);
        var loaded = ProjectStorage.Load(path);
        Assert.Equal(source.Name, loaded.Name);
        Assert.Equal(source.Pieces.Count, loaded.Pieces.Count);
        for (var i = 0; i < source.Pieces.Count; i++)
        {
            Assert.Equal(source.Pieces[i].Id, loaded.Pieces[i].Id);
            Assert.Equal(source.Pieces[i].Name, loaded.Pieces[i].Name);
            Assert.Equal(source.Pieces[i].Closed, loaded.Pieces[i].Closed);
            Assert.Equal(source.Pieces[i].Points, loaded.Pieces[i].Points);
        }
        Assert.Single(Directory.GetFiles(directory));
    }
    [Fact]
    public void InvalidSaveDoesNotDestroyExistingProject()
    {
        var path = FilePath("project.tcad"); var source = Sample();
        ProjectStorage.Save(source, path); var before = File.ReadAllBytes(path);
        source.Pieces[0].Points[0] = new(double.NaN, 0);
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Save(source, path));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
    [Fact]
    public void NativeLoadRejectsNullContours()
    {
        var path = FilePath("bad.tcad");
        File.WriteAllText(path, "{\"Pieces\":[{\"Points\":null}]}");
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
    }
    [Fact]
    public void DxfRoundTripPreservesMillimetersUnderTurkishCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var source = Sample(); var path = FilePath("roundtrip.dxf");
            DxfFormat.Export(source, path);
            var result = DxfFormat.Import(path);
            Assert.Equal(2, result.Project.Pieces.Count);
            for (var i = 0; i < 2; i++)
            {
                Assert.Equal(source.Pieces[i].Name, result.Project.Pieces[i].Name);
                Assert.Equal(source.Pieces[i].Points, result.Project.Pieces[i].Points);
                Assert.Equal(source.Pieces[i].Closed, result.Project.Pieces[i].Closed);
            }
            Assert.Equal(10_000, Math.Abs(Geometry.SignedArea(result.Project.Pieces[0].Points)), 8);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void UnorderedLinesStitchIntoClosedHundredMillimeterSquare()
    {
        var path = FilePath("square.dxf");
        File.WriteAllText(path, "0\nSECTION\n2\nENTITIES\n" +
            Line(100, 100, 0, 100) + Line(0, 0, 100, 0) + Line(0, 0, 0, 100) + Line(100, 100, 100, 0) + "0\nENDSEC\n0\nEOF\n");
        var result = DxfFormat.Import(path);
        var piece = Assert.Single(result.Project.Pieces);
        Assert.True(piece.Closed); Assert.Equal(4, piece.Points.Count);
        Assert.Equal(100, Geometry.Bounds(piece.Points).Width);
        Assert.Equal(100, Geometry.Bounds(piece.Points).Height);
        Assert.Equal(10_000, Math.Abs(Geometry.SignedArea(piece.Points)), 8);
        Assert.False(result.Units.IsReliable);
        Assert.Null(result.Units.MillimetersPerUnit);
        Assert.Contains(result.Warnings, w => w.Contains("birimi güvenilir biçimde belirlenemedi"));
    }
    private static string Line(int x1, int y1, int x2, int y2) =>
        $"0\nLINE\n8\nKALIP\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}\n";
    [Fact]
    public void WearCadReferenceSquarePreservesSize()
    {
        // Optional read-only local integration fixture; portable fallback has the
        // same four LINE coordinates observed in the WearCAD 100 mm reference.
        var reference = Environment.GetEnvironmentVariable("TEKSTILCAD_REFERENCE_DXF");
        var path = FilePath("reference-square.dxf");
        if (!string.IsNullOrEmpty(reference)) File.Copy(reference, path);
        else File.WriteAllText(path, "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1009\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" +
            Line(0, 0, 100, 0) + Line(100, 0, 100, 100) + Line(100, 100, 0, 100) + Line(0, 100, 0, 0) + "0\nENDSEC\n0\nEOF\n");
        var piece = Assert.Single(DxfFormat.Import(path).Project.Pieces);
        Assert.True(piece.Closed);
        Assert.Equal(100, Geometry.Bounds(piece.Points).Width);
        Assert.Equal(100, Geometry.Bounds(piece.Points).Height);
        Assert.Equal(10_000, Math.Abs(Geometry.SignedArea(piece.Points)), 8);
        var native = FilePath("reference-square.tcad");
        var doc = new ProjectDocument { Pieces = [piece] };
        ProjectStorage.Save(doc, native);
        Assert.Equal(piece.Points, Assert.Single(ProjectStorage.Load(native).Pieces).Points);
    }
    [Fact]
    public void ExplicitDxfScaleConvertsCoordinates()
    {
        var path = FilePath("units.dxf");
        File.WriteAllText(path, "0\nSECTION\n2\nENTITIES\n" + Line(0, 0, 10, 0) + "0\nENDSEC\n0\nEOF\n");
        var piece = Assert.Single(DxfFormat.Import(path, 25.4).Project.Pieces);
        Assert.Equal(254, Geometry.Distance(piece.Points[0], piece.Points[1]), 8);
    }
    [Fact]
    public void DxfRejectsTruncation()
    {
        var path = FilePath("truncated.dxf");
        File.WriteAllText(path, "0\nSECTION\n2\nENTITIES\n" + Line(0, 0, 10, 0));
        Assert.Throws<InvalidDataException>(() => DxfFormat.Import(path));
    }
    [Fact]
    public void DxfReportsUnsupportedEntities()
    {
        var path = FilePath("arc.dxf");
        File.WriteAllText(path, "0\nSECTION\n2\nENTITIES\n" + Line(0, 0, 10, 0) + "0\nARC\n10\n0\n20\n0\n40\n5\n0\nENDSEC\n0\nEOF\n");
        Assert.Contains(DxfFormat.Import(path).Warnings, w => w.Contains("ARC"));
    }
    [Fact]
    public void SvgUsesPhysicalDimensionsAndEscapesNames()
    {
        var doc = Sample(); doc.Pieces[0].Name = "Ön <A> & \"B\"";
        var path = FilePath("pattern.svg"); SvgFormat.Export(doc, path);
        var svg = XDocument.Load(path); XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal("100.25mm", svg.Root!.Attribute("width")!.Value);
        Assert.Equal("100.25mm", svg.Root.Attribute("height")!.Value);
        var paths = svg.Root.Elements(ns + "path").ToList();
        Assert.Equal(2, paths.Count);
        Assert.Equal(doc.Pieces[0].Name, paths[0].Element(ns + "title")!.Value);
        Assert.EndsWith(" Z", paths[0].Attribute("d")!.Value);
        Assert.DoesNotContain("Z", paths[1].Attribute("d")!.Value);
    }
    [Fact]
    public void HpglScalesClosesAndLiftsPenBetweenPieces()
    {
        var doc = new ProjectDocument { Pieces = [new() { Closed = true, Points = [new(0, 0), new(100, 0), new(100, 100)] },
            new() { Closed = false, Points = [new(10, 10), new(20, 20)] }] };
        var path = FilePath("plot.plt"); HpglFormat.Export(doc, path);
        Assert.Equal("IN;SP1;PA;\nPU0,0;PD4000,0,4000,4000,0,0;PU;\nPU400,400;PD800,800;PU;\nSP0;\n", File.ReadAllText(path));
    }
    [Fact]
    public void HpglOverflowDoesNotWriteOutput()
    {
        var doc = Sample(); doc.Pieces[0].Points[0] = new(double.MaxValue, 0);
        var path = FilePath("overflow.plt");
        Assert.Throws<InvalidDataException>(() => HpglFormat.Export(doc, path));
        Assert.False(File.Exists(path));
    }
    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }
}
