using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public class NotchExportGuardTests
{
    private static ProjectDocument Marked() => new()
    {
        Pieces = [new PatternPiece
        {
            Points = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            Notches = [new NotchMark { VertexIndex = 1, Position = new(10, 0) }]
        }]
    };

    [Fact]
    public void AllFormatWritersRejectMarkersBeforeReplacingExistingFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tekstilcad-notch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "existing");
            File.WriteAllText(path, "existing");
            var project = Marked();
            Assert.Contains("Çentik", Assert.Throws<InvalidDataException>(() => DxfFormat.Export(project, path)).Message);
            Assert.Contains("Çentik", Assert.Throws<InvalidDataException>(() => SvgFormat.Export(project, path)).Message);
            Assert.Contains("Çentik", Assert.Throws<InvalidDataException>(() => HpglFormat.Export(project, path)).Message);
            Assert.Equal("existing", File.ReadAllText(path));
            Assert.Contains("Çentik", Assert.Throws<InvalidDataException>(() => HpglFormat.Prepare(project)).Message);
            Assert.Contains("Çentik", Assert.Throws<InvalidDataException>(() => DxfGrainlineReviewExport.Prepare(project)).Message);
            Assert.Contains("Çentik", Assert.Throws<InvalidDataException>(() => DxfFormat.AnalyzeExport(project)).Message);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PdfExportRejectsMarksWithoutChangingSourceOrDestination(bool tiled, bool destinationExists)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tekstilcad-notch-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "output.pdf");
            if (destinationExists) File.WriteAllText(path, "original content");
            var project = Marked();
            var before = project.Clone();
            var version = ProjectStorage.CaptureVersion(path);
            var error = Assert.Throws<InvalidDataException>(() =>
            {
                if (tiled) PdfTiledFormat.Export(project, path, version);
                else PdfFormat.Export(project, path, version);
            });
            Assert.Contains("Çentik", error.Message);
            Assert.True(DocumentSession.ContentEquals(before, project));
            if (destinationExists) Assert.Equal("original content", File.ReadAllText(path));
            else Assert.False(File.Exists(path));
            Assert.Equal(destinationExists ? 1 : 0, Directory.EnumerateFileSystemEntries(directory).Count());
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData(JobFormat.Dxf)]
    [InlineData(JobFormat.Hpgl)]
    public void SimulationPreparationRejectsMarkersBeforeCreatingAJob(JobFormat format)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tekstilcad-notch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var queue = new SimulationQueue(directory);
            var project = Marked();
            var profile = new MachineProfile("test");
            Assert.Contains("Çentik", Assert.Throws<InvalidDataException>(() => queue.Prepare(project, profile, format)).Message);
            Assert.Contains("Çentik", Assert.Throws<InvalidDataException>(() => queue.PrepareHpglReview(project, profile)).Message);
            Assert.Empty(queue.Jobs);
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
