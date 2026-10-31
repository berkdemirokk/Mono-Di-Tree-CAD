using System.Collections.Immutable;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Tests the internal expansion stage only, not the public DXF importer.</summary>
public sealed class DxfInsertExpanderTests
{
    private static DxfIndexGroup G(int code, string value) => new(code, value, value);
    private static (string Type, DxfIndexGroup[] Groups) E(string type, params DxfIndexGroup[] groups) => (type, groups);
    private static (string Type, DxfIndexGroup[] Groups)[] Block(string name, params (string Type, DxfIndexGroup[] Groups)[] body) =>
        [E("BLOCK", G(2, name), G(10, "0"), G(20, "0"), G(30, "0")), .. body, E("ENDBLK")];
    private static (string Type, DxfIndexGroup[] Groups) Triangle(params DxfIndexGroup[] extra) =>
        E("LWPOLYLINE", [G(8, "0"), G(90, "3"), G(70, "1"),
            G(10, "0"), G(20, "0"), G(10, "2"), G(20, "0"), G(10, "0"), G(20, "1"), .. extra]);
    private static (string Type, DxfIndexGroup[] Groups) Insert(string name, params DxfIndexGroup[] extra) =>
        E("INSERT", [G(2, name), G(10, "0"), G(20, "0"), .. extra]);
    private static DxfInsertIndex Index((string Type, DxfIndexGroup[] Groups)[] blocks,
        params (string Type, DxfIndexGroup[] Groups)[] model)
    {
        (string Type, DxfIndexGroup[] Groups)[] records =
            [E("SECTION", G(2, "BLOCKS")), .. blocks, E("ENDSEC"),
             E("SECTION", G(2, "ENTITIES")), .. model, E("ENDSEC"), E("EOF")];
        var entities = records.Select((record, ordinal) =>
            new DxfIndexEntity(record.Type, record.Groups.ToImmutableArray(), ordinal)).ToArray();
        return DxfInsertIndex.Build(entities, raw => raw);
    }
    private static ImmutableArray<DxfExpandedContour> Expand(DxfInsertIndex index, double mm = 1,
        Func<DxfIndexEntity, bool>? approve = null, CancellationToken token = default) =>
        DxfInsertExpander.Expand(index, mm, raw => raw, approve ?? (_ => true), token);

    private static void Points(IReadOnlyList<Point2> actual, params Point2[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < actual.Count; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X, 8);
            Assert.Equal(expected[i].Y, actual[i].Y, 8);
        }
    }

    [Fact]
    public void SeparateInstancesRetainSeparatePathsAndOnlyReferencedBlocksAppear()
    {
        var blocks = Block("A", Triangle()).Concat(Block("UNUSED", Triangle())).ToArray();
        var index = Index(blocks,
            E("INSERT", G(2, "A"), G(10, "10"), G(20, "20"), G(41, "2"), G(42, "3"), G(50, "90")),
            E("INSERT", G(2, "A"), G(10, "-10"), G(20, "5")));
        var result = Expand(index);
        Assert.Equal(2, result.Length);
        Points(result[0].Points, new(10, 20), new(10, 24), new(7, 20));
        Points(result[1].Points, new(-10, 5), new(-8, 5), new(-10, 6));
        Assert.All(result, item => Assert.True(item.Closed));
        Assert.NotEqual(result[0].InsertPath[0].Occurrence, result[1].InsertPath[0].Occurrence);
        Assert.Equal(result[0].EntityOccurrence, result[1].EntityOccurrence);
    }

    [Fact]
    public void NestedInstancesApplyChildThenParentAndFinallyUnits()
    {
        var blocks = new[] {
            E("BLOCK", G(2, "A"), G(10, "1"), G(20, "1"), G(30, "0")),
            E("INSERT", G(2, "B"), G(10, "3"), G(20, "4"), G(50, "90")), E("ENDBLK")
        }.Concat(Block("B", Triangle())).ToArray();
        var index = Index(blocks,
            E("INSERT", G(2, "A"), G(10, "100"), G(20, "200"), G(41, "2"), G(42, "3")));
        var result = Assert.Single(Expand(index, 10));
        Points(result.Points, new(1040, 2090), new(1040, 2150), new(1020, 2090));
        Assert.Equal(2, result.InsertPath.Length);
        Assert.Equal("A", result.InsertPath[0].BlockName);
        Assert.Equal("B", result.InsertPath[1].BlockName);
    }

    [Fact]
    public void MissingBlockAndRecursiveReferenceAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Expand(Index([], Insert("MISSING"))));
        Assert.Throws<InvalidDataException>(() => Expand(Index(Block("A", Insert("A")), Insert("A"))));
        var indirect = Block("A", Insert("B")).Concat(Block("B", Insert("A"))).ToArray();
        Assert.Throws<InvalidDataException>(() => Expand(Index(indirect, Insert("A"))));
    }

    [Theory]
    [InlineData(70, "2")]
    [InlineData(71, "2")]
    [InlineData(30, "1")]
    [InlineData(210, "1")]
    [InlineData(41, "0")]
    public void UnsupportedOrInvalidInsertTransformIsRejected(int code, string value)
    {
        var index = Index(Block("A", Triangle()), Insert("A", G(code, value)));
        Assert.Throws<InvalidDataException>(() => Expand(index));
    }

    [Fact]
    public void BulgeAndUnsupportedBlockGeometryFailTheEntireExpansion()
    {
        var bulge = Index(Block("A", Triangle(G(42, "0.25"))), Insert("A"));
        Assert.Throws<InvalidDataException>(() => Expand(bulge));
        var mixed = Index(Block("A", Triangle(), E("CIRCLE", G(10, "0"), G(20, "0"), G(40, "1"))), Insert("A"));
        Assert.Throws<InvalidDataException>(() => Expand(mixed));
    }

    [Fact]
    public void AttributeSequenceAndExternalBlocksAreRejected()
    {
        var attributes = Index(Block("A", Triangle()),
            Insert("A", G(66, "1")), E("ATTRIB", G(1, "value")), E("SEQEND"));
        Assert.Throws<InvalidDataException>(() => Expand(attributes));
        var external = new[] { E("BLOCK", G(2, "A"), G(10, "0"), G(20, "0"), G(70, "4")), Triangle(), E("ENDBLK") };
        Assert.Throws<InvalidDataException>(() => Expand(Index(external, Insert("A"))));
    }

    [Fact]
    public void DistinctSourceDecimalsThatRoundToSameDoubleAreRejected()
    {
        var points = E("LWPOLYLINE", G(90, "3"), G(70, "1"),
            G(10, "10000000000000000"), G(20, "0"), G(10, "10000000000000001"), G(20, "0"),
            G(10, "10000000000000000"), G(20, "1"));
        Assert.Throws<InvalidDataException>(() => Expand(Index(Block("A", points), Insert("A")), 0.000001));
    }

    [Fact]
    public void NestedInsertionCannotRoundOntoParentBase()
    {
        var blocks = new[] {
            E("BLOCK", G(2, "A"), G(10, "10000000000000000"), G(20, "0")),
            E("INSERT", G(2, "B"), G(10, "10000000000000001"), G(20, "0")), E("ENDBLK")
        }.Concat(Block("B", Triangle())).ToArray();
        Assert.Throws<InvalidDataException>(() => Expand(Index(blocks, Insert("A"))));
    }

    [Fact]
    public void SubnormalBulgeAndRoundedExtrusionCannotBeTreatedAsDefaults()
    {
        Assert.Throws<InvalidDataException>(() => Expand(Index(Block("A", Triangle(G(42, "1e-400"))), Insert("A"))));
        Assert.Throws<InvalidDataException>(() => Expand(Index(Block("A", Triangle()), Insert("A", G(230, "1.00000000000000001")))));
    }

    [Fact]
    public void CallerDenialAndCancellationNeverReturnPartialResults()
    {
        var index = Index(Block("A", Triangle()), Insert("A"));
        Assert.Throws<InvalidDataException>(() => Expand(index, approve: entity => entity.Type != "LWPOLYLINE"));
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Expand(index, token: source.Token));
    }

    [Fact]
    public void StrictResolutionCarriesEffectiveLayerForEachNestedOccurrence()
    {
        var blocks = Block("OUTER", Insert("INNER", G(8, "0"), G(6, "BYBLOCK")))
            .Concat(Block("INNER", Triangle())).ToArray();
        var index = Index(blocks, Insert("OUTER", G(8, "KUMAŞ")), Insert("OUTER", G(8, "ASTAR")));
        var resolver = new DxfInsertLayerResolver([
            new("KUMAŞ", false, true, "CONTINUOUS", true),
            new("ASTAR", false, true, "CONTINUOUS", true)
        ], raw => raw);
        var contours = DxfInsertExpander.Expand(index, 1, raw => raw, resolver);
        Assert.Equal(2, contours.Length);
        Assert.Equal("KUMAŞ", contours[0].EffectiveLayer);
        Assert.Equal("ASTAR", contours[1].EffectiveLayer);
        Assert.Equal(2, contours[0].InsertPath.Length);
        Assert.NotEqual(contours[0].InsertPath[0].Occurrence, contours[1].InsertPath[0].Occurrence);
    }

    [Fact]
    public void StrictResolutionHonorsExplicitChildLayer()
    {
        var child = E("LWPOLYLINE", G(8, "INNER"), G(90, "3"), G(70, "1"),
            G(10, "0"), G(20, "0"), G(10, "2"), G(20, "0"), G(10, "0"), G(20, "1"));
        var index = Index(Block("A", child), Insert("A", G(8, "OUTER")));
        var resolver = new DxfInsertLayerResolver([
            new("OUTER", false, true, "CONTINUOUS", true),
            new("INNER", false, true, "CONTINUOUS", true)
        ], raw => raw);
        Assert.Equal("INNER", Assert.Single(DxfInsertExpander.Expand(index, 1, raw => raw, resolver)).EffectiveLayer);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void StrictResolutionRejectsHiddenOrNonPlotParentWithoutPartialResults(bool hidden, bool plot)
    {
        var index = Index(Block("A", Triangle()), Insert("A", G(8, "SAFE")), Insert("A", G(8, "UNSAFE")));
        var resolver = new DxfInsertLayerResolver([
            new("SAFE", false, true, "CONTINUOUS", true),
            new("UNSAFE", hidden, plot, "CONTINUOUS", true)
        ], raw => raw);
        Assert.Throws<InvalidDataException>(() => DxfInsertExpander.Expand(index, 1, raw => raw, resolver));
    }

    [Fact]
    public void StrictResolutionRejectsHiddenChildAndUnknownLayer()
    {
        var index = Index(Block("A", Triangle(G(60, "1"))), Insert("A", G(8, "OUTER")));
        var resolver = new DxfInsertLayerResolver([new("OUTER", false, true, "CONTINUOUS", true)], raw => raw);
        Assert.Throws<InvalidDataException>(() => DxfInsertExpander.Expand(index, 1, raw => raw, resolver));
        var unknown = Index(Block("A", Triangle()), Insert("A", G(8, "UNKNOWN")));
        Assert.Throws<InvalidDataException>(() => DxfInsertExpander.Expand(unknown, 1, raw => raw, resolver));
    }
}
