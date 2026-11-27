using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DartExportTests
{
    private static ProjectDocument WithDart()
    {
        var piece = new PatternPiece { Name = "Ön", Points = [new(0,0), new(100,0), new(100,100), new(0,100)] };
        piece = DartTools.AddDart(piece, 0, .4, .6, 20);
        return new ProjectDocument { Pieces = [piece] };
    }

    [Fact]
    public void TiledPdfIncludesSewingLegsAsInternalDashedMarks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tekstil-dart-{Guid.NewGuid():N}.pdf");
        try
        {
            PdfTiledFormat.Export(WithDart(), path, ProjectStorage.CaptureVersion(path));
            var pdf = File.ReadAllText(path);
            Assert.Contains("[3 2] 0 d 0.15 0.35 0.75 RG", pdf);
            Assert.Contains("m ", pdf);
            Assert.Contains("[] 0 d", pdf);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void HpglRefusesDartProjectInsteadOfSilentlyDroppingSewingLegs()
    {
        var error = Assert.Throws<InvalidDataException>(() => HpglFormat.Prepare(WithDart()));
        Assert.Contains("pens dikiş kollarını", error.Message);
    }
}
