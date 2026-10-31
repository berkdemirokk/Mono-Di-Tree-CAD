using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Independent end-to-end safety cases for opt-in block placements.</summary>
public sealed class DxfStrictInsertAdditionalTests
{
    private const string Polygon = "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n" +
        "10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n1\n";
    private const string Block = "0\nBLOCK\n2\nA\n10\n0\n20\n0\n30\n0\n" + Polygon + "0\nENDBLK\n";
    private const string Insert = "0\nINSERT\n2\nA\n8\n0\n10\n10\n20\n20\n";

    private static string Drawing(string entities, string flags = "70\n0\n", string color = "62\n7\n",
        string plot = "290\n1\n", string extraTables = "") =>
        "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
        "0\nLAYER\n2\n0\n" + flags + color + plot + "6\nCONTINUOUS\n0\nENDTAB\n" + extraTables + "0\nENDSEC\n" +
        "0\nSECTION\n2\nBLOCKS\n" + Block + "0\nENDSEC\n" +
        "0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n";

    private static DxfImportResult Read(string source, CancellationToken token = default)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        using var input = new MemoryStream(bytes, writable: false);
        try
        {
            return DxfFormat.ImportWithBlockPlacements(input, "strict", 1, DxfTextEncoding.Utf8, 0, ["0"], token);
        }
        finally { Assert.Equal(bytes, input.ToArray()); }
    }

    [Fact]
    public void NoModelInsertionNeverFallsBackToUnusedDefinitionCatalog()
    {
        var drawing = Drawing("");
        Assert.Throws<InvalidDataException>(() => Read(drawing));
        using var legacy = new MemoryStream(Encoding.UTF8.GetBytes(drawing), writable: false);
        Assert.Single(DxfFormat.Import(legacy, "legacy").Project.Pieces);
    }

    [Theory]
    [InlineData("70\n1\n", "62\n7\n", "290\n1\n")]
    [InlineData("70\n0\n", "62\n-7\n", "290\n1\n")]
    [InlineData("70\n0\n", "62\n7\n", "290\n0\n")]
    [InlineData("", "62\n7\n", "290\n1\n")]
    [InlineData("70\n0\n", "", "290\n1\n")]
    [InlineData("70\n0\n", "62\n7\n", "")]
    public void HiddenNonPlotOrUnknownLayerIsNotSilentlyAccepted(string flags, string color, string plot)
    {
        Assert.Throws<InvalidDataException>(() => Read(Drawing(Insert, flags, color, plot)));
    }

    [Fact]
    public void UnresolvedViewportVisibilityIsRejected()
    {
        const string viewport = "0\nTABLE\n2\nVPORT\n0\nVPORT\n2\n*ACTIVE\n0\nENDTAB\n";
        Assert.Throws<InvalidDataException>(() => Read(Drawing(Insert, extraTables: viewport)));
    }

    [Fact]
    public void DirectByBlockLineTypeCannotInheritArtificialParentSemantics()
    {
        var direct = Polygon.Replace("8\n0\n", "8\n0\n6\nBYBLOCK\n", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Read(Drawing(Insert + direct)));
    }

    [Fact]
    public void UnsupportedDirectCircleCannotBeDroppedFromSuccessfulImport()
    {
        const string circle = "0\nCIRCLE\n8\n0\n10\n5\n20\n5\n40\n2\n";
        Assert.Throws<InvalidDataException>(() => Read(Drawing(Insert + circle)));
    }

    [Fact]
    public void CancelledReadRetainsSourceBytes()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Read(Drawing(Insert), cancellation.Token));
    }
}
