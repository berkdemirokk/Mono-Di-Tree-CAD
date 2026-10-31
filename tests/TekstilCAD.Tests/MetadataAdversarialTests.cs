using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class MetadataAdversarialTests
{
    private static ProjectDocument Sample(PieceMetadata? metadata) => new()
    {
        Pieces = [new PatternPiece { Points = [new(0, 0), new(10, 0), new(0, 10)], Metadata = metadata }]
    };

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    public void QuantityOutsideAllowedRangeIsRejected(int quantity) =>
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(Sample(new() { Quantity = quantity })));

    [Theory]
    [InlineData(1)]
    [InlineData(100000)]
    public void QuantityBoundsAreAccepted(int quantity) => ProjectStorage.Validate(Sample(new() { Quantity = quantity }));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("M ")]
    [InlineData("M\nL")]
    public void BlankUntrimmedAndControlTextIsRejected(string value) =>
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(Sample(new() { Size = value })));

    [Fact]
    public void MissingMetadataIsValidButEmptyMetadataObjectIsRejected()
    {
        ProjectStorage.Validate(Sample(null));
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(Sample(new())));
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Validate(Sample(new() { Material = new string('x', 129) })));
    }

    [Fact]
    public void MetadataInOldEnvelopeAndUnknownVersionAreRejectedWithoutChangingFile()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "metadata-adversarial-tests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tcad");
        try
        {
            ProjectStorage.Save(Sample(new() { Size = "M" }), path);
            var valid = File.ReadAllText(path);
            using var json = System.Text.Json.JsonDocument.Parse(valid);
            var project = json.RootElement[1].GetRawText();
            foreach (var invalid in new[] { "[1," + project + "]", "[3," + project + "]", project })
            {
                File.WriteAllText(path, invalid);
                Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(path));
                Assert.Equal(invalid, File.ReadAllText(path));
            }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
