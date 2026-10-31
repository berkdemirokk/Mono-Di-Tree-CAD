using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class FormatSafetyTests
{
    private static ProjectDocument Sample() => new() { Pieces = [new() { Points = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)] }] };

    [Theory]
    [InlineData(@"C:\WearCadfe\must-not-be-created.tcad")]
    [InlineData(@"C:\WearCadfe \must-not-be-created.tcad")]
    [InlineData(@"C:\WearCadfe.\must-not-be-created.tcad")]
    [InlineData(@"C:\Hpgl\must-not-be-created.tcad")]
    [InlineData(@"C:\Nester\must-not-be-created.tcad")]
    [InlineData(@"C:\Nester1\must-not-be-created.tcad")]
    [InlineData(@"C:\NST\must-not-be-created.tcad")]
    [InlineData(@"C:\Projects\..\WearCadfe\must-not-be-created.tcad")]
    public void ProtectedDestinationRejectedBeforeAnyWrite(string path)
    {
        Assert.Throws<InvalidOperationException>(() => ProjectStorage.EnsureWritableDestination(path));
    }

    [Fact]
    public void RejectsOutOfDomainFiniteCoordinates()
    {
        var doc = Sample(); doc.Pieces[0].Points[0] = new(1e9, 0);
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(doc));
    }
    [Fact]
    public void RejectsDuplicateIdentity()
    {
        var doc = Sample(); doc.Pieces.Add(doc.Pieces[0].Clone());
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(doc));
    }
    [Fact]
    public void RejectsNullNamesAndIncompleteClosedContours()
    {
        var doc = Sample(); doc.Name = null!;
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(doc));
        doc = Sample(); doc.Pieces[0].Name = null!;
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(doc));
        doc = Sample(); doc.Pieces[0].Points.RemoveRange(0, 2);
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(doc));
    }
    [Theory]
    [InlineData("30\n20\n")]
    [InlineData("210\n1\n")]
    [InlineData("230\n-1\n")]
    public void DxfDoesNotSilentlyFlattenSpatialLine(string plane)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root.FullName, "work", "format-safety"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".dxf");
        try
        {
            const string line = "0\nLINE\n10\n0\n20\n0\n11\n100\n21\n0\n";
            File.WriteAllText(path, "0\nSECTION\n2\nENTITIES\n" + line + line + plane + "0\nENDSEC\n0\nEOF\n");
            var result = DxfFormat.Import(path);
            Assert.Single(result.Project.Pieces);
            Assert.Contains(result.Warnings, w => w.Contains("3D/elevation/normal"));
        }
        finally { File.Delete(path); }
    }
}
