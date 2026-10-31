using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>End-to-end placement checks against synthetic, independently calculated DXF data.</summary>
public sealed class DxfStrictInsertImportTests
{
    private const string LayerTable = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nLAYER\n2\nCUT\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nENDTAB\n0\nENDSEC\n";

    private const string Triangle = "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n" +
        "10\n2\n20\n3\n10\n4\n20\n3\n10\n2\n20\n4\n";

    private static string Block(string name, string body, string bx = "2", string by = "3") =>
        $"0\nBLOCK\n2\n{name}\n10\n{bx}\n20\n{by}\n30\n0\n{body}0\nENDBLK\n";
    private static string Insert(string name, string x, string y, string extra = "", string layer = "0") =>
        $"0\nINSERT\n2\n{name}\n8\n{layer}\n10\n{x}\n20\n{y}\n{extra}";
    private static string Drawing(string blocks, string entities, string tables = LayerTable) =>
        tables + "0\nSECTION\n2\nBLOCKS\n" + blocks + "0\nENDSEC\n" +
        "0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n";

    private static DxfImportResult Import(string text, double factor = 1, double tolerance = 0,
        string[]? approved = null, CancellationToken token = default)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var input = new MemoryStream(bytes, writable: false);
        try
        {
            return DxfFormat.ImportWithBlockPlacements(input, "sentetik", factor, DxfTextEncoding.Utf8,
                tolerance, approved ?? ["0", "CUT"], token);
        }
        finally { Assert.Equal(bytes, input.ToArray()); }
    }

    private static void Points(IReadOnlyList<Point2> actual, params Point2[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X, 9);
            Assert.Equal(expected[i].Y, actual[i].Y, 9);
        }
    }

    [Fact]
    public void TwoInstancesUseTheirOwnPlacementAndUnusedDefinitionsAreExcluded()
    {
        var blocks = Block("A", Triangle) + Block("UNUSED", Triangle);
        var entities = Insert("A", "10", "20", "41\n2\n42\n3\n50\n90\n", "CUT") +
            Insert("A", "-10", "5");
        var result = Import(Drawing(blocks, entities));

        Assert.Equal(2, result.Project.Pieces.Count);
        var first = Assert.Single(result.Project.Pieces, p => p.Points.Any(q => q == new Point2(10, 24)));
        var second = Assert.Single(result.Project.Pieces, p => p.Points.Any(q => q == new Point2(-8, 5)));
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(first.Closed);
        Assert.True(second.Closed);
        Points(first.Points, new(10, 20), new(10, 24), new(7, 20));
        Points(second.Points, new(-10, 5), new(-8, 5), new(-10, 6));
    }

    [Fact]
    public void DirectModelGeometryAndPlacedBlockAreBothIncludedExactlyOnce()
    {
        var direct = "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n" +
            "10\n100\n20\n100\n10\n101\n20\n100\n10\n100\n20\n101\n";
        var result = Import(Drawing(Block("A", Triangle), direct + Insert("A", "10", "20")));
        Assert.Equal(2, result.Project.Pieces.Count);
        Assert.Single(result.Project.Pieces, p => p.Points.Contains(new Point2(100, 100)));
        Assert.Single(result.Project.Pieces, p => p.Points.Contains(new Point2(10, 20)));
    }

    [Fact]
    public void NestedBlocksComposeChildThenParentBeforeUnitConversion()
    {
        var outer = Block("OUTER", Insert("INNER", "3", "4", "50\n90\n"), "1", "1");
        var inner = Block("INNER", Triangle, "2", "3");
        var result = Import(Drawing(outer + inner, Insert("OUTER", "100", "200", "41\n2\n42\n3\n")), factor: 10);
        var piece = Assert.Single(result.Project.Pieces);
        Points(piece.Points, new(1040, 2090), new(1040, 2150), new(1020, 2090));
    }

    [Fact]
    public void UnknownOrUnapprovedLayerCannotBeAcceptedSilently()
    {
        var drawing = Drawing(Block("A", Triangle), Insert("A", "0", "0", layer: "CUT"));
        Assert.Throws<InvalidDataException>(() => Import(drawing, approved: ["0"]));
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Block("A", Triangle), Insert("A", "0", "0"), tables: "")));
    }

    [Theory]
    [InlineData("50\n89.999999999999999\n")]
    [InlineData("70\n2\n")]
    [InlineData("210\n1\n")]
    public void UnsupportedOrAmbiguousInsertIsRejected(string unsupported)
    {
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Block("A", Triangle), Insert("A", "0", "0", unsupported))));
    }

    [Fact]
    public void UnknownAndCyclicBlockReferencesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Block("A", Triangle), Insert("MISSING", "0", "0"))));
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Block("A", Insert("A", "0", "0")), Insert("A", "0", "0"))));
    }

    [Fact]
    public void InvalidToleranceAndCancelledImportAreRejected()
    {
        var drawing = Drawing(Block("A", Triangle), Insert("A", "0", "0"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Import(drawing, tolerance: -1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Import(drawing, token: cancellation.Token));
    }
}
