using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class PlotQuantizationTests : IDisposable
{
    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void QuantizationCannotMergeDistinctNonAdjacentVertices(double units)
    {
        var points=new Point2[] { new(0,0),new(4,0),new(4,4),new(2.4,4),new(2.4,1),new(1.6,2),new(1.6,4),new(0,4) };
        var doc=Contour(true,points.Select(p=>new Point2(p.X/units,p.Y/units)).ToArray());
        var path=Path.Combine(directory,"notch.plt");
        File.WriteAllText(path,"previous");
        Assert.Throws<InvalidDataException>(()=>HpglFormat.Export(doc,path,units));
        Assert.Equal("previous",File.ReadAllText(path));
    }
    private readonly string directory;

    public PlotQuantizationTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        directory = Path.Combine(root.FullName, "work", "plot-quantization-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    private static ProjectDocument Contour(bool closed, params Point2[] points) => new()
    {
        Pieces = [new PatternPiece { Name = "Ön beden", Closed = closed, Points = [.. points] }]
    };

    [Fact]
    public void TinyClosedContourDoesNotCreateFile()
    {
        var path = Path.Combine(directory, "tiny.plt");
        var document = Contour(true, new(0, 0), new(.01, 0), new(0, .01));
        var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(document, path));
        Assert.Contains("HPGL çözünürlüğünde", error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CollapsedDetailPreservesExistingFileEvenWhenContourStillHasArea()
    {
        var path = Path.Combine(directory, "existing.plt");
        File.WriteAllText(path, "previous approved output");
        var document = Contour(true, new(0, 0), new(.01, 0), new(1, 0), new(1, 1), new(0, 1));
        Assert.Throws<InvalidDataException>(() => HpglFormat.Export(document, path));
        Assert.Equal("previous approved output", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void CollapsedOpenEdgeIsRejected()
    {
        var path = Path.Combine(directory, "open.plt");
        Assert.Throws<InvalidDataException>(() => HpglFormat.Export(Contour(false, new(0, 0), new(.01, .01)), path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ClosingEdgeIsAlsoChecked()
    {
        var path = Path.Combine(directory, "closure.plt");
        var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(
            Contour(true, new(0, 0), new(1, 0), new(1, 1), new(.01, .01)), path));
        Assert.Contains("4. kenarı", error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AreaCollapseWithDistinctOutputVerticesIsRejected()
    {
        var path = Path.Combine(directory, "flat.plt");
        var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(
            Contour(true, new(0, 0), new(1, .001), new(2, 0)), path));
        Assert.Contains("alan oluşturmuyor", error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ExplicitClosureVertexRemainsSupported()
    {
        var path = Path.Combine(directory, "closed.plt");
        HpglFormat.Export(Contour(true, new(0, 0), new(1, 0), new(1, 1), new(0, 0)), path);
        Assert.Equal("IN;SP1;PA;\nPU0,0;PD40,0,40,40,0,0,0,0;PU;\nSP0;\n", File.ReadAllText(path));
    }

    [Fact]
    public void NegativeCoordinatesAndHalfUnitRoundingKeepExistingConvention()
    {
        var path = Path.Combine(directory, "rounding.plt");
        HpglFormat.Export(Contour(false, new(-.75, -1.25), new(.75, 1.25)), path, 2);
        Assert.Equal("IN;SP1;PA;\nPU-2,-2;PD2,2;PU;\nSP0;\n", File.ReadAllText(path));
    }

    [Fact]
    public void ScaledGeometryWithCustomUnitsExportsExpectedCoordinates()
    {
        var path = Path.Combine(directory, "scaled.plt");
        var source = new[] { new Point2(-1, -2), new Point2(3, -2), new Point2(3, 4) };
        HpglFormat.Export(Contour(true, source.Select(point => point * 2.5).ToArray()), path, 20);
        Assert.Equal("IN;SP1;PA;\nPU-50,-100;PD150,-100,150,200,-50,-100;PU;\nSP0;\n", File.ReadAllText(path));
    }

    [Fact]
    public void SymmetricIntegerBoundaryAndSmallAreaAtLargeOffsetRemainValid()
    {
        const double units = 32;
        var high = int.MaxValue / units;
        var path = Path.Combine(directory, "boundary.plt");
        var document = Contour(true, new(high, high), new(high - 1 / units, high), new(high, high - 1 / units));
        document.Pieces.Add(new PatternPiece { Closed = false, Points = [new(-high, 0), new(-high + 1 / units, 0)] });
        HpglFormat.Export(document, path, units);
        var output = File.ReadAllText(path);
        Assert.Contains("PU2147483647,2147483647;PD2147483646,2147483647,2147483647,2147483646", output);
        Assert.Contains("PU-2147483647,0;PD-2147483646,0", output);
    }

    [Theory]
    [InlineData(60_000_000)]
    [InlineData(-60_000_000)]
    public void CadValidButHpglOutOfRangePreservesExistingFile(double coordinate)
    {
        var path = Path.Combine(directory, "overflow.plt");
        File.WriteAllText(path, "approved");
        var document = Contour(false, new(coordinate, 0), new(coordinate, 10));
        ProjectStorage.Validate(document);
        var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Export(document, path));
        Assert.Contains("koordinat sınırı", error.Message);
        Assert.Equal("approved", File.ReadAllText(path));
    }

    [Fact]
    public void MultiplicationOverflowIsRejectedBeforeWriting()
    {
        var path = Path.Combine(directory, "product-overflow.plt");
        Assert.Throws<InvalidDataException>(() => HpglFormat.Export(
            Contour(false, new(2, 0), new(3, 1)), path, double.MaxValue));
        Assert.False(File.Exists(path));
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
        Directory.Delete(directory);
    }
}
