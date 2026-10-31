using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Strict block POLYLINE import with synthetic DXF; no vendor files are used.</summary>
public sealed class DxfStrictLegacyPolylineTests
{
    private const string Tables = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nLAYER\n2\nCUT\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nLAYER\n2\nOTHER\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nENDTAB\n0\nENDSEC\n";

    private static string Vertex(string x, string y, string extra = "", string layer = "0") =>
        $"0\nVERTEX\n8\n{layer}\n10\n{x}\n20\n{y}\n{extra}";
    private static string Polyline(string extra = "", string vertexExtra = "", string vertexLayer = "0", bool end = true) =>
        "0\nPOLYLINE\n8\n0\n66\n1\n70\n1\n" + extra + Vertex("2", "3", vertexExtra, vertexLayer) +
        Vertex("4", "3") + Vertex("2", "4") + (end ? "0\nSEQEND\n" : "");
    private static string Block(string name, string body, string bx = "2", string by = "3") =>
        $"0\nBLOCK\n2\n{name}\n10\n{bx}\n20\n{by}\n30\n0\n{body}0\nENDBLK\n";
    private static string Insert(string name, string x, string y, string extra = "", string layer = "0") =>
        $"0\nINSERT\n2\n{name}\n8\n{layer}\n10\n{x}\n20\n{y}\n{extra}";
    private static string Drawing(string blocks, string model) => Tables +
        "0\nSECTION\n2\nBLOCKS\n" + blocks + "0\nENDSEC\n" +
        "0\nSECTION\n2\nENTITIES\n" + model + "0\nENDSEC\n0\nEOF\n";

    private static DxfImportResult Import(string text, double factor = 1, double tolerance = 0,
        string[]? approved = null, CancellationToken token = default)
    {
        var original = Encoding.UTF8.GetBytes(text);
        using var stream = new MemoryStream(original, writable: false);
        try
        {
            return DxfFormat.ImportWithBlockPlacements(stream, "POLYLINE denemesi", factor,
                DxfTextEncoding.Utf8, tolerance, approved ?? ["0", "CUT", "OTHER"], token);
        }
        finally { Assert.Equal(original, stream.ToArray()); }
    }

    [Fact]
    public void NestedRotatedBlockUsesBaseAndFileUnits()
    {
        var outer = Block("OUTER", Insert("INNER", "3", "4", "50\n90\n"), "1", "1");
        var inner = Block("INNER", Polyline());
        var result = Import(Drawing(outer + inner, Insert("OUTER", "100", "200", "41\n2\n42\n3\n", "CUT")), factor: 10);
        var piece = Assert.Single(result.Project.Pieces);
        Assert.True(piece.Closed);
        Assert.Equal("CUT", piece.Name);
        Assert.Equal(new[] { new Point2(1040, 2090), new Point2(1040, 2150), new Point2(1020, 2090) }, piece.Points);
    }

    [Fact]
    public void SeparateInstancesAndInheritedLayerRemainDistinct()
    {
        var source = Drawing(Block("A", Polyline()), Insert("A", "10", "20", layer: "CUT") + Insert("A", "-10", "5", layer: "OTHER"));
        var result = Import(source);
        Assert.Equal(2, result.Project.Pieces.Count);
        var first = Assert.Single(result.Project.Pieces, p => p.Name == "CUT");
        var second = Assert.Single(result.Project.Pieces, p => p.Name == "OTHER");
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(new[] { new Point2(10, 20), new Point2(12, 20), new Point2(10, 21) }, first.Points);
        Assert.Equal(new[] { new Point2(-10, 5), new Point2(-8, 5), new Point2(-10, 6) }, second.Points);
    }

    [Theory]
    [InlineData("70\n8\n", "")]
    [InlineData("40\n0.1\n", "")]
    [InlineData("", "70\n32\n")]
    [InlineData("", "42\n1e-400\n")]
    [InlineData("", "40\n1e-400\n")]
    [InlineData("", "30\n1\n")]
    [InlineData("", "60\n1\n")]
    [InlineData("", "999\nunexpected\n")]
    public void UnsafeVertexOrPolylineMetadataRejectsWholeImport(string parentExtra, string vertexExtra)
    {
        var source = Drawing(Block("A", Polyline(parentExtra, vertexExtra)), Insert("A", "0", "0"));
        Assert.Throws<InvalidDataException>(() => Import(source));
    }

    [Fact]
    public void MissingSequenceEndOrDifferentVertexLayerIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Block("A", Polyline(end: false)), Insert("A", "0", "0"))));
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Block("A", Polyline(vertexLayer: "OTHER")), Insert("A", "0", "0", layer: "CUT"))));
    }

    [Fact]
    public void LargeOriginRoundingAndUnapprovedLayerCannotSilentlyChangeGeometry()
    {
        var body = "0\nPOLYLINE\n8\n0\n66\n1\n70\n1\n" +
            Vertex("10000000000000001", "0") + Vertex("10000000000000004", "0") +
            Vertex("10000000000000002", "2") + "0\nSEQEND\n";
        var source = Drawing(Block("A", body, "10000000000000002", "0"), Insert("A", "0", "0"));
        Assert.Throws<InvalidDataException>(() => Import(source));
        var unapproved = Drawing(Block("A", Polyline()), Insert("A", "0", "0", layer: "CUT"));
        Assert.Throws<InvalidDataException>(() => Import(unapproved, approved: ["0"]));
    }

    [Fact]
    public void CancelledImportPreservesInputBytes()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Import(Drawing(Block("A", Polyline()), Insert("A", "0", "0")), token: cancellation.Token));
    }

    [Fact]
    public void DirectModelPolylineAndBlockInstanceRemainSeparate()
    {
        var source = Drawing(Block("A", Polyline()), Polyline() + Insert("A", "10", "20", layer: "CUT"));
        var result = Import(source);
        Assert.Equal(2, result.Project.Pieces.Count);
        var direct = Assert.Single(result.Project.Pieces, p => p.Points.Contains(new Point2(2, 3)));
        var placed = Assert.Single(result.Project.Pieces, p => p.Points.Contains(new Point2(10, 20)));
        Assert.True(direct.Closed);
        Assert.True(placed.Closed);
        Assert.NotEqual(direct.Id, placed.Id);
        Assert.Equal("0", direct.Name);
        Assert.Equal("CUT", placed.Name);
        Assert.Equal(new[] { new Point2(2, 3), new Point2(4, 3), new Point2(2, 4) }, direct.Points);
        Assert.Equal(new[] { new Point2(10, 20), new Point2(12, 20), new Point2(10, 21) }, placed.Points);
    }

    [Theory]
    [InlineData("", "60\n1\n", "0")]
    [InlineData("", "42\n0.25\n", "0")]
    [InlineData("", "42\n1e-400\n", "0")]
    [InlineData("", "", "OTHER")]
    [InlineData("70\n8\n", "", "0")]
    [InlineData("", "50\n45\n", "0")]
    [InlineData("", "71\n3\n", "0")]
    public void DirectModelUnsupportedVerticesRejectTheWholeImport(string parentExtra, string vertexExtra, string vertexLayer)
    {
        var model = Polyline(parentExtra, vertexExtra, vertexLayer) + Insert("A", "10", "20");
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Block("A", Polyline()), model)));
    }

    [Theory]
    [InlineData("100\nAcDb3dPolyline\n", "")]
    [InlineData("", "100\nAcDb3dPolylineVertex\n")]
    public void WrongThreeDimensionalSubclassIsRejectedEvenWhenFlagsClaim2D(string parentExtra, string vertexExtra)
    {
        var block = Block("A", Polyline(parentExtra, vertexExtra));
        Assert.Throws<InvalidDataException>(() => Import(Drawing(block, Insert("A", "10", "20"))));
        var model = Polyline(parentExtra, vertexExtra) + Insert("A", "10", "20");
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Block("A", Polyline()), model)));
    }
}
