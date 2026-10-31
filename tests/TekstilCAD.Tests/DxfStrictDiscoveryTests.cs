using System.Security.Cryptography;
using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Snapshot inspection and verified import use the same bytes, not just a path or length.</summary>
public sealed class DxfStrictDiscoveryTests
{
    private static string Drawing(string layer = "KESIM", string x = "10", bool unknownVisibility = false) =>
        "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
        $"0\nLAYER\n2\n{layer}\n" + (unknownVisibility ? "" : "70\n0\n62\n7\n") + "290\n1\n6\nCONTINUOUS\n" +
        "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nBLOCKS\n" +
        "0\nBLOCK\n2\nA\n10\n0\n20\n0\n30\n0\n" +
        "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n1\n" +
        "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" +
        $"0\nINSERT\n2\nA\n8\n{layer}\n10\n{x}\n20\n20\n" +
        "0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void InspectionReportsExactSnapshotHashLayersAndRestoresStreamPosition()
    {
        byte[] data = Encoding.UTF8.GetBytes(Drawing());
        byte[] prefix = [9, 8, 7];
        using var input = new MemoryStream(prefix.Concat(data).ToArray(), writable: false);
        input.Position = prefix.Length;
        var result = DxfFormat.InspectBlockPlacements(input, DxfTextEncoding.Utf8);
        Assert.Equal(prefix.Length, input.Position);
        Assert.True(result.HasModelInsert);
        Assert.Equal(data.Length, result.SourceByteLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), result.SourceSha256);
        var layer = Assert.Single(result.Layers);
        Assert.Equal("KESIM", layer.Name);
        Assert.False(layer.Hidden);
        Assert.True(layer.PlotEnabled);
        Assert.Equal("CONTINUOUS", layer.LineType);
        Assert.NotEmpty(result.Limitations);
        Assert.Equal(prefix.Concat(data), input.ToArray());
    }

    [Fact]
    public void MissingLayerVisibilityIsReportedAsUnknownRatherThanVisible()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Drawing(unknownVisibility: true)), writable: false);
        var result = DxfFormat.InspectBlockPlacements(input, DxfTextEncoding.Utf8);
        Assert.Null(Assert.Single(result.Layers).Hidden);
        Assert.Equal(0, input.Position);
    }

    [Fact]
    public void VerifiedImportAcceptsInspectedBytesAndRetainsSourceIdentity()
    {
        var data = Encoding.UTF8.GetBytes(Drawing());
        using var input = new MemoryStream(data, writable: false);
        var inspection = DxfFormat.InspectBlockPlacements(input, DxfTextEncoding.Utf8);
        var result = DxfFormat.ImportWithBlockPlacementsVerified(input, "fixture", 1, DxfTextEncoding.Utf8,
            0, ["KESIM"], inspection.SourceSha256, inspection.SourceByteLength);
        Assert.Equal(inspection.SourceSha256, result.SourceSha256);
        Assert.Equal(inspection.SourceByteLength, result.SourceByteLength);
        Assert.Single(result.Project.Pieces);
        Assert.Equal(data, input.ToArray());
    }

    [Fact]
    public void InspectionBasedImportUsesTheInspectionEncodingAndSnapshot()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] data = Encoding.GetEncoding(1254).GetBytes(Drawing(layer: "ÇİZGİ"));
        using var input = new MemoryStream(data, writable: false);
        var inspection = DxfFormat.InspectBlockPlacements(input, DxfTextEncoding.Windows1254);
        var result = DxfFormat.ImportWithInspectedBlockPlacements(input, "fixture", 1, 0, ["ÇİZGİ"], inspection);
        Assert.Single(result.Project.Pieces);
        Assert.Equal(inspection.SourceSha256, result.SourceSha256);
        Assert.Equal(data, input.ToArray());
    }

    [Fact]
    public void SameLengthSourceMutationInvalidatesEarlierInspection()
    {
        byte[] original = Encoding.UTF8.GetBytes(Drawing(x: "10"));
        byte[] changed = Encoding.UTF8.GetBytes(Drawing(x: "11"));
        Assert.Equal(original.Length, changed.Length);
        using var before = new MemoryStream(original, writable: false);
        var inspection = DxfFormat.InspectBlockPlacements(before, DxfTextEncoding.Utf8);
        using var after = new MemoryStream(changed, writable: false);
        Assert.Throws<InvalidDataException>(() => DxfFormat.ImportWithBlockPlacementsVerified(after, "fixture", 1,
            DxfTextEncoding.Utf8, 0, ["KESIM"], inspection.SourceSha256, inspection.SourceByteLength));
        Assert.Equal(changed, after.ToArray());
    }

    [Fact]
    public void TruncatedSourceOrWrongExpectedHashIsRejected()
    {
        byte[] original = Encoding.UTF8.GetBytes(Drawing());
        using var input = new MemoryStream(original, writable: false);
        var inspection = DxfFormat.InspectBlockPlacements(input, DxfTextEncoding.Utf8);
        using var truncated = new MemoryStream(original[..^1], writable: false);
        Assert.Throws<InvalidDataException>(() => DxfFormat.ImportWithBlockPlacementsVerified(truncated, "fixture", 1,
            DxfTextEncoding.Utf8, 0, ["KESIM"], inspection.SourceSha256, inspection.SourceByteLength));
        using var same = new MemoryStream(original, writable: false);
        string wrongHash = new('0', 64);
        Assert.Throws<InvalidDataException>(() => DxfFormat.ImportWithBlockPlacementsVerified(same, "fixture", 1,
            DxfTextEncoding.Utf8, 0, ["KESIM"], wrongHash, inspection.SourceByteLength));
    }

    [Fact]
    public void ExplicitEncodingControlsLayerDecodingAndInvalidUtf8IsRejected()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] data = Encoding.GetEncoding(1254).GetBytes(Drawing(layer: "ÇİZGİ"));
        using var utf8 = new MemoryStream(data, writable: false);
        Assert.Throws<InvalidDataException>(() => DxfFormat.InspectBlockPlacements(utf8, DxfTextEncoding.Utf8));
        Assert.Equal(0, utf8.Position);
        using var ansi = new MemoryStream(data, writable: false);
        var result = DxfFormat.InspectBlockPlacements(ansi, DxfTextEncoding.Windows1254);
        Assert.Equal("ÇİZGİ", Assert.Single(result.Layers).Name);
        Assert.Equal(0, ansi.Position);
        Assert.Equal(data, ansi.ToArray());
    }

    [Fact]
    public void InspectionWarnsBeforeSelectionWhenWindows1254BytesAreAlsoValidUtf8()
    {
        // ASCII bytes are valid in both encodings. The inspection must report
        // the ambiguity rather than claim that the selected decoder is proven.
        byte[] data = Encoding.UTF8.GetBytes(Drawing(layer: "KESIM"));
        using var input = new MemoryStream(data, writable: false);
        var result = DxfFormat.InspectBlockPlacements(input, DxfTextEncoding.Windows1254);
        Assert.Equal(DxfTextEncoding.Windows1254, result.Encoding);
        Assert.Equal("KESIM", Assert.Single(result.Layers).Name);
        Assert.Contains(result.Warnings, warning => warning.Contains("UTF-8", StringComparison.OrdinalIgnoreCase)
            && warning.Contains("geçerli", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, input.Position);
        Assert.Equal(data, input.ToArray());
    }

    [Fact]
    public void CancelledInspectionRestoresPositionAndDoesNotMutateBytes()
    {
        byte[] data = Encoding.UTF8.GetBytes(Drawing());
        using var input = new MemoryStream(data, writable: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DxfFormat.InspectBlockPlacements(input, DxfTextEncoding.Utf8, cancellation.Token));
        Assert.Equal(0, input.Position);
        Assert.Equal(data, input.ToArray());
    }
}
