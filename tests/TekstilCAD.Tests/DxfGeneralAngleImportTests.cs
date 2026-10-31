using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>End-to-end angle tests with synthetic DXF and independently calculated coordinates.</summary>
public sealed class DxfGeneralAngleImportTests
{
    private const string Tables = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nLAYER\n2\nCUT\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nLAYER\n2\nOTHER\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nENDTAB\n0\nENDSEC\n";

    private static string Block(string name, string body) =>
        $"0\nBLOCK\n2\n{name}\n10\n0\n20\n0\n30\n0\n{body}0\nENDBLK\n";
    private static string Insert(string name, string angle, string x = "0", string y = "0", string extra = "", string layer = "0") =>
        $"0\nINSERT\n2\n{name}\n8\n{layer}\n10\n{x}\n20\n{y}\n50\n{angle}\n{extra}";
    private static string Drawing(string blocks, string model) => Tables +
        "0\nSECTION\n2\nBLOCKS\n" + blocks + "0\nENDSEC\n" +
        "0\nSECTION\n2\nENTITIES\n" + model + "0\nENDSEC\n0\nEOF\n";

    private static string Triangle(string type) => type switch
    {
        "LWPOLYLINE" => "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n0\n20\n0\n10\n2\n20\n0\n10\n0\n20\n1\n",
        "POLYLINE" => "0\nPOLYLINE\n8\n0\n66\n1\n70\n1\n" +
                      "0\nVERTEX\n8\n0\n10\n0\n20\n0\n" +
                      "0\nVERTEX\n8\n0\n10\n2\n20\n0\n" +
                      "0\nVERTEX\n8\n0\n10\n0\n20\n1\n0\nSEQEND\n",
        "LINE" => "0\nLINE\n8\n0\n10\n0\n20\n0\n11\n2\n21\n0\n" +
                  "0\nLINE\n8\n0\n10\n2\n20\n0\n11\n0\n21\n1\n" +
                  "0\nLINE\n8\n0\n10\n0\n20\n1\n11\n0\n21\n0\n",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static DxfImportResult Import(string source, double tolerance = 1e-9,
        string[]? layers = null, double unit = 1, CancellationToken token = default)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        using var stream = new MemoryStream(bytes, writable: false);
        try
        {
            return DxfFormat.ImportWithBlockPlacements(stream, "Genel açı testi", unit,
                DxfTextEncoding.Utf8, tolerance, layers ?? ["0", "CUT", "OTHER"], token);
        }
        finally { Assert.Equal(bytes, stream.ToArray()); }
    }

    private static void ContainsPoint(IReadOnlyList<Point2> points, double x, double y, double epsilon = 1e-9) =>
        Assert.Contains(points, p => Math.Abs(p.X - x) <= epsilon && Math.Abs(p.Y - y) <= epsilon);

    [Theory]
    [InlineData("LINE")]
    [InlineData("LWPOLYLINE")]
    [InlineData("POLYLINE")]
    public void ThirtyDegreePlacementPreservesEachSupportedGeometry(string type)
    {
        var result = Import(Drawing(Block("A", Triangle(type)), Insert("A", "30", "10", "20", layer: "CUT")));
        var piece = Assert.Single(result.Project.Pieces);
        Assert.True(piece.Closed);
        Assert.Equal("CUT", piece.Name);
        Assert.Equal(3, piece.Points.Count);
        ContainsPoint(piece.Points, 10, 20);
        ContainsPoint(piece.Points, 11.7320508075688772935, 21);
        ContainsPoint(piece.Points, 9.5, 20.8660254037844386468);
    }

    [Fact]
    public void NestedNonuniformReflectionAndSeparateLayersPreservePlacements()
    {
        var inner = Block("INNER", Triangle("LWPOLYLINE"));
        var outer = Block("OUTER", Insert("INNER", "30", extra: "41\n-2\n42\n3\n"));
        var model = Insert("OUTER", "45", "100", "200", layer: "CUT") +
                    Insert("OUTER", "45", "-100", "-200", layer: "OTHER");
        var result = Import(Drawing(inner + outer, model), unit: 10);
        Assert.Equal(2, result.Project.Pieces.Count);
        var first = Assert.Single(result.Project.Pieces, p => p.Name == "CUT");
        var second = Assert.Single(result.Project.Pieces, p => p.Name == "OTHER");
        Assert.NotEqual(first.Id, second.Id);
        ContainsPoint(first.Points, 989.64723819589917, 1961.3629669484373, 1e-8);
        ContainsPoint(second.Points, -1010.3527618041008, -2038.6370330515627, 1e-8);
    }

    [Fact]
    public void AngleThatRoundsToNinetyStillUsesOriginalDecimalForTolerance()
    {
        var source = Drawing(Block("A", Triangle("LWPOLYLINE")), Insert("A", "89.999999999999999"));
        Assert.Throws<InvalidDataException>(() => Import(source, tolerance: 1e-18));
        var result = Import(source, tolerance: 1e-12);
        var piece = Assert.Single(result.Project.Pieces);
        ContainsPoint(piece.Points, 0, 2);
    }

    [Fact]
    public void ZeroToleranceRejectsIrrationalPlacementAndCancellationPreservesSource()
    {
        var source = Drawing(Block("A", Triangle("LWPOLYLINE")), Insert("A", "30"));
        Assert.Throws<InvalidDataException>(() => Import(source, tolerance: 0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Import(source, token: cancellation.Token));
    }

    [Fact]
    public void GeneralAngleDoesNotBypassLayerApproval()
    {
        var source = Drawing(Block("A", Triangle("LWPOLYLINE")), Insert("A", "45", layer: "CUT"));
        Assert.Throws<InvalidDataException>(() => Import(source, layers: ["0"]));
    }
}
