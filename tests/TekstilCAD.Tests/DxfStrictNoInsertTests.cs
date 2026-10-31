using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Strict model-space import without INSERT, using synthetic, read-only DXF streams.</summary>
public sealed class DxfStrictNoInsertTests
{
    private const string Tables = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nLAYER\n2\nCUT\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
        "0\nENDTAB\n0\nENDSEC\n";
    private const string UnusedBlock = "0\nBLOCK\n2\nUNUSED\n10\n0\n20\n0\n30\n0\n" +
        "0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n10\n1000\n20\n1000\n10\n1001\n20\n1000\n10\n1000\n20\n1001\n0\nENDBLK\n";
    private const string Lightweight = "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n" +
        "10\n10\n20\n10\n10\n12\n20\n10\n10\n10\n20\n11\n";
    private const string Lines = "0\nLINE\n8\n0\n10\n20\n20\n20\n11\n22\n21\n20\n" +
        "0\nLINE\n8\n0\n10\n22\n20\n20\n11\n20\n21\n21\n" +
        "0\nLINE\n8\n0\n10\n20\n20\n21\n11\n20\n21\n20\n";
    private const string Classic = "0\nPOLYLINE\n8\nCUT\n66\n1\n70\n1\n" +
        "0\nVERTEX\n8\nCUT\n10\n30\n20\n30\n" +
        "0\nVERTEX\n8\nCUT\n10\n32\n20\n30\n" +
        "0\nVERTEX\n8\nCUT\n10\n30\n20\n31\n0\nSEQEND\n8\nCUT\n";

    private static string Drawing(string model, string block = UnusedBlock, string tables = Tables) => tables +
        "0\nSECTION\n2\nBLOCKS\n" + block + "0\nENDSEC\n" +
        "0\nSECTION\n2\nENTITIES\n" + model + "0\nENDSEC\n0\nEOF\n";

    private static DxfImportResult Import(string drawing, string[]? approved = null, CancellationToken token = default)
    {
        var bytes = Encoding.UTF8.GetBytes(drawing);
        using var input = new MemoryStream(bytes, writable: false);
        try
        {
            return DxfFormat.ImportWithBlockPlacements(input, "model", 1, DxfTextEncoding.Utf8, 0,
                approved ?? ["0", "CUT"], token);
        }
        finally { Assert.Equal(bytes, input.ToArray()); }
    }

    [Fact]
    public void ModelGeometryImportsOnceAndUnusedDefinitionsAreExcluded()
    {
        var result = Import(Drawing(Lightweight + Lines + Classic));
        Assert.Equal(3, result.Project.Pieces.Count);
        Assert.Single(result.Project.Pieces, p => p.Closed && p.Points.Contains(new Point2(10, 10)));
        Assert.Single(result.Project.Pieces, p => p.Closed && p.Points.Contains(new Point2(20, 20)));
        Assert.Single(result.Project.Pieces, p => p.Closed && p.Points.Contains(new Point2(30, 30)));
        Assert.DoesNotContain(result.Project.Pieces, p => p.Points.Any(q => q.X >= 1000 || q.Y >= 1000));
    }

    [Fact]
    public void UnusedBlockAloneDoesNotBecomeModelGeometry()
        => Assert.Throws<InvalidDataException>(() => Import(Drawing("")));

    [Theory]
    [InlineData("67\n1\n")]
    [InlineData("410\nLayout1\n")]
    [InlineData("60\n1\n")]
    public void NonModelOrInvisibleEntityIsNeverSilentlyAccepted(string fields)
        => Assert.Throws<InvalidDataException>(() => Import(Drawing(Lightweight.Replace("8\nCUT\n", "8\nCUT\n" + fields))));

    [Fact]
    public void MissingOrUnapprovedLayerIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Lightweight), approved: ["0"]));
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Lightweight, tables: "")));
        Assert.Throws<InvalidDataException>(() => Import(Drawing(Lightweight.Replace("8\nCUT\n", "8\nUNKNOWN\n"))));
    }

    [Theory]
    [InlineData("1001\nAPPLICATION\n1000\nunknown\n")]
    [InlineData("102\n{PRIVATE\n1\nopaque\n102\n}\n")]
    public void ApplicationMetadataIsNotSilentlyDiscarded(string metadata)
        => Assert.Throws<InvalidDataException>(() => Import(Drawing(Lightweight + metadata)));

    [Fact]
    public void DistinctCoordinatesThatRoundTogetherAreRejected()
    {
        var collision = "0\nLWPOLYLINE\n8\nCUT\n90\n3\n70\n1\n" +
            "10\n10000000000000000\n20\n0\n10\n10000000000000001\n20\n0\n" +
            "10\n10000000000000000\n20\n1\n";
        Assert.Throws<InvalidDataException>(() => Import(Drawing(collision)));
    }

    [Fact]
    public void CancellationLeavesOriginalBytesUntouched()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Import(Drawing(Lightweight), token: source.Token));
    }
}
