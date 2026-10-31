using System.Collections.Immutable;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Strict LINE expansion checks with synthetic source records.</summary>
public sealed class DxfStrictLineTests
{
    private static DxfIndexGroup G(int code, string value) => new(code, value, value);
    private static (string Type, DxfIndexGroup[] Groups) E(string type, params DxfIndexGroup[] groups) => (type, groups);
    private static (string Type, DxfIndexGroup[] Groups) Line(string x1, string y1, string x2, string y2,
        string layer = "0", params DxfIndexGroup[] extra) =>
        E("LINE", [G(8, layer), G(10, x1), G(20, y1), G(11, x2), G(21, y2), .. extra]);
    private static (string Type, DxfIndexGroup[] Groups) Insert(string name, string x = "0", string y = "0") =>
        E("INSERT", G(2, name), G(8, "0"), G(10, x), G(20, y));
    private static (string Type, DxfIndexGroup[] Groups)[] Block(string name,
        params (string Type, DxfIndexGroup[] Groups)[] body) =>
        [E("BLOCK", G(2, name), G(10, "0"), G(20, "0")), .. body, E("ENDBLK")];

    private static DxfInsertIndex Index((string Type, DxfIndexGroup[] Groups)[] blocks,
        params (string Type, DxfIndexGroup[] Groups)[] entities)
    {
        (string Type, DxfIndexGroup[] Groups)[] all =
            [E("SECTION", G(2, "BLOCKS")), .. blocks, E("ENDSEC"),
             E("SECTION", G(2, "ENTITIES")), .. entities, E("ENDSEC"), E("EOF")];
        return DxfInsertIndex.Build(all.Select((e, n) =>
            new DxfIndexEntity(e.Type, e.Groups.ToImmutableArray(), n)).ToArray(), s => s);
    }
    private static ImmutableArray<DxfExpandedContour> Expand(DxfInsertIndex index, double tolerance = 0,
        CancellationToken token = default)
    {
        var layers = new DxfInsertLayerResolver([
            new DxfInsertLayerState("0", false, true, "CONTINUOUS", true),
            new DxfInsertLayerState("CUT", false, true, "CONTINUOUS", true)], s => s);
        return DxfInsertExpander.Expand(index, 1, s => s, layers, tolerance, token);
    }

    [Fact]
    public void ALineProducesOneOpenContourWithBothEndpoints()
    {
        var result = Assert.Single(Expand(Index(Block("A", Line("1", "2", "3", "4")), Insert("A", "10", "20"))));
        Assert.False(result.Closed);
        Assert.Equal(2, result.Points.Length);
        Assert.Contains(new Point2(11, 22), result.Points);
        Assert.Contains(new Point2(13, 24), result.Points);
    }

    [Fact]
    public void TriangleLinesJoinIntoOneClosedContourRegardlessOfOrderAndDirection()
    {
        var block = Block("A", Line("0", "0", "2", "0"),
            Line("0", "1", "2", "0"), Line("0", "0", "0", "1"));
        var result = Assert.Single(Expand(Index(block, Insert("A", "10", "20"))));
        Assert.True(result.Closed);
        Assert.Equal(3, result.Points.Length);
        Assert.Contains(new Point2(10, 20), result.Points);
        Assert.Contains(new Point2(12, 20), result.Points);
        Assert.Contains(new Point2(10, 21), result.Points);
    }

    [Fact]
    public void StrictImportReadsLineTriangleFromAPlacedBlockWithoutChangingSourceBytes()
    {
        const string drawing =
            "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n" +
            "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n" +
            "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nBLOCKS\n" +
            "0\nBLOCK\n2\nA\n10\n0\n20\n0\n30\n0\n" +
            "0\nLINE\n8\n0\n10\n0\n20\n0\n11\n2\n21\n0\n" +
            "0\nLINE\n8\n0\n10\n2\n20\n0\n11\n0\n21\n1\n" +
            "0\nLINE\n8\n0\n10\n0\n20\n1\n11\n0\n21\n0\n" +
            "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" +
            "0\nINSERT\n2\nA\n8\n0\n10\n10\n20\n20\n" +
            "0\nENDSEC\n0\nEOF\n";
        var bytes = Encoding.UTF8.GetBytes(drawing);
        using var input = new MemoryStream(bytes, writable: false);
        var result = DxfFormat.ImportWithBlockPlacements(input, "sentetik", 1, DxfTextEncoding.Utf8,
            0, ["0"]);
        Assert.Equal(bytes, input.ToArray());
        var piece = Assert.Single(result.Project.Pieces);
        Assert.True(piece.Closed);
        Assert.Equal(3, piece.Points.Count);
        Assert.Contains(new Point2(10, 20), piece.Points);
        Assert.Contains(new Point2(12, 20), piece.Points);
        Assert.Contains(new Point2(10, 21), piece.Points);
    }

    [Fact]
    public void TouchingOccurrencesRemainIndependentEvenOnSameEffectiveLayer()
    {
        var block = Block("A", Line("0", "0", "1", "0"));
        var result = Expand(Index(block, Insert("A"), Insert("A", "1", "0")));
        Assert.Equal(2, result.Length);
        Assert.All(result, p => Assert.False(p.Closed));
        Assert.All(result, p => Assert.Equal(2, p.Points.Length));
        Assert.NotEqual(result[0].InsertPath[0].Occurrence, result[1].InsertPath[0].Occurrence);
        Assert.Single(result, p => p.Points.Contains(new Point2(0, 0)) && p.Points.Contains(new Point2(1, 0)));
        Assert.Single(result, p => p.Points.Contains(new Point2(1, 0)) && p.Points.Contains(new Point2(2, 0)));
    }

    [Fact]
    public void TouchingSegmentsOnDistinctEffectiveLayersAreNotJoined()
    {
        var block = Block("A", Line("0", "0", "1", "0"), Line("1", "0", "2", "0", "CUT"));
        var result = Expand(Index(block, Insert("A")));
        Assert.Equal(2, result.Length);
        Assert.Single(result, p => p.EffectiveLayer == "0");
        Assert.Single(result, p => p.EffectiveLayer == "CUT");
        Assert.All(result, p => Assert.Equal(2, p.Points.Length));
    }

    [Fact]
    public void BranchingConnectionsAreRejectedInsteadOfInventingAContour()
    {
        var block = Block("A", Line("0", "0", "1", "0"), Line("1", "0", "2", "0"),
            Line("1", "0", "1", "1"));
        Assert.Throws<InvalidDataException>(() => Expand(Index(block, Insert("A"))));
    }

    [Fact]
    public void ClosedBowTieWithAnInteriorCrossingIsRejected()
    {
        var block = Block("A", Line("0", "0", "2", "2"), Line("2", "2", "0", "2"),
            Line("0", "2", "2", "0"), Line("2", "0", "0", "0"));
        Assert.Throws<InvalidDataException>(() => Expand(Index(block, Insert("A"))));
    }

    [Fact]
    public void EndpointTouchingAnotherLinesInteriorIsRejectedAsAmbiguous()
    {
        var block = Block("A", Line("0", "0", "2", "0"), Line("1", "0", "1", "1"));
        Assert.Throws<InvalidDataException>(() => Expand(Index(block, Insert("A"))));
    }

    [Fact]
    public void PartiallyOverlappingCollinearSegmentsAreRejected()
    {
        var block = Block("A", Line("0", "0", "3", "0"), Line("1", "0", "4", "0"));
        Assert.Throws<InvalidDataException>(() => Expand(Index(block, Insert("A"))));
    }

    [Fact]
    public void DuplicateSegmentsAndCoordinatesCollapsedByBinary64AreRejected()
    {
        var duplicate = Block("A", Line("0", "0", "1", "0"), Line("1", "0", "0", "0"));
        Assert.Throws<InvalidDataException>(() => Expand(Index(duplicate, Insert("A"))));
        var precision = Block("A", Line("10000000000000000", "0", "10000000000000001", "0"));
        Assert.Throws<InvalidDataException>(() => Expand(Index(precision, Insert("A"))));
    }

    [Theory]
    [InlineData(39, "1")]
    [InlineData(210, "1")]
    [InlineData(999, "unsupported")]
    public void UnsupportedLinePropertiesFailClosed(int code, string value)
    {
        var block = Block("A", Line("0", "0", "1", "0", "0", G(code, value)));
        Assert.Throws<InvalidDataException>(() => Expand(Index(block, Insert("A"))));
    }

    [Fact]
    public void CancellationDoesNotMutateSourceRecords()
    {
        var index = Index(Block("A", Line("0", "0", "1", "0")), Insert("A"));
        var before = index.Blocks[0].Entities[0].Entity.Groups.ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Expand(index, token: cancellation.Token));
        Assert.Equal(before, index.Blocks[0].Entities[0].Entity.Groups.ToArray());
    }
}
