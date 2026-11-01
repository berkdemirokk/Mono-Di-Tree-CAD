using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class ExportDestinationTests
{
    [Theory]
    [InlineData("model.dxf", 1)]
    [InlineData("MODEL.DXF", 1)]
    [InlineData("model.svg", 2)]
    [InlineData("model.SVG", 2)]
    [InlineData("model.plt", 3)]
    [InlineData("model.HPGL", 3)]
    [InlineData("model.pdf", 4)]
    public void MatchingExtensionsAreAccepted(string path, int format)
        => ExportDestination.ValidateExtension(path, format);

    [Theory]
    [InlineData("model.svg", 1)]
    [InlineData("model.dxf", 2)]
    [InlineData("model.svg", 3)]
    [InlineData("model", 1)]
    [InlineData("model.", 2)]
    public void MismatchOrMissingExtensionIsRejected(string path, int format)
        => Assert.Throws<InvalidDataException>(() => ExportDestination.ValidateExtension(path, format));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void UnknownFormatIsRejected(int format)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ExportDestination.ValidateExtension("model.dxf", format));
}
