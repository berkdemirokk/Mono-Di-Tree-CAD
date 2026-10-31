using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Strict import must never silently accept unsafe independent model contours.</summary>
public sealed class DxfStrictInsertSafetyTests
{
    private const string Tables = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nLAYER\n2\nCUT\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nENDTAB\n0\nENDSEC\n";

    private const string BlockAndPlacement =
        "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nA\n10\n0\n20\n0\n30\n0\n" +
        "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n" +
        "10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n1\n" +
        "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" +
        "0\nINSERT\n2\nA\n8\n0\n10\n10\n20\n20\n";

    private static string ModelContour(string layer, string extra = "") =>
        $"0\nLWPOLYLINE\n8\n{layer}\n90\n3\n70\n1\n" +
        "10\n100\n20\n100\n" + extra + "10\n101\n20\n100\n10\n100\n20\n101\n";

    private static void AssertRejectedWithoutChangingSource(string directContour, params string[] approvedLayers)
    {
        var bytes = Encoding.UTF8.GetBytes(Tables + BlockAndPlacement + directContour + "0\nENDSEC\n0\nEOF\n");
        using var input = new MemoryStream(bytes, writable: false);
        Assert.Throws<InvalidDataException>(() => DxfFormat.ImportWithBlockPlacements(
            input, "strict-safety", 1, DxfTextEncoding.Utf8, 0, approvedLayers));
        Assert.Equal(bytes, input.ToArray());
    }

    [Fact]
    public void IndependentContourOnUnapprovedLayerCannotBeImportedSilently()
    {
        // The block's layer 0 is approved; a separate layer CUT is not.
        AssertRejectedWithoutChangingSource(ModelContour("CUT"), "0");
    }

    [Fact]
    public void NonzeroBulgeThatRoundsToDoubleZeroCannotBecomeStraightGeometry()
    {
        // The direct contour's layer is approved, isolating the tiny curve value.
        AssertRejectedWithoutChangingSource(ModelContour("CUT", "42\n1e-400\n"), "0", "CUT");
    }
}
