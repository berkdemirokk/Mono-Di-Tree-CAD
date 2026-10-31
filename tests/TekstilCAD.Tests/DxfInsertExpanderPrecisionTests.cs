using System.Collections.Immutable;
using System.Globalization;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Exercises the strict internal expansion stage; it is not a public importer test.</summary>
public sealed class DxfInsertExpanderPrecisionTests
{
    private static DxfIndexGroup G(int code, string value) => new(code, value, value);
    private static (string Type, DxfIndexGroup[] Groups) E(string type, params DxfIndexGroup[] groups) => (type, groups);
    private static (string Type, DxfIndexGroup[] Groups) Poly(string x1 = "0", string x2 = "2", string x3 = "0") =>
        E("LWPOLYLINE", G(8, "0"), G(90, "3"), G(70, "1"),
            G(10, x1), G(20, "0"), G(10, x2), G(20, "0"), G(10, x3), G(20, "1"));
    private static (string Type, DxfIndexGroup[] Groups)[] Block(string name, string bx, string by,
        params (string Type, DxfIndexGroup[] Groups)[] body) =>
        [E("BLOCK", G(2, name), G(10, bx), G(20, by)), .. body, E("ENDBLK")];
    private static (string Type, DxfIndexGroup[] Groups) Insert(string name, string x = "0", string y = "0", params DxfIndexGroup[] extra) =>
        E("INSERT", [G(2, name), G(8, "0"), G(10, x), G(20, y), .. extra]);
    private static DxfInsertIndex Index((string Type, DxfIndexGroup[] Groups)[] blocks,
        params (string Type, DxfIndexGroup[] Groups)[] model)
    {
        (string Type, DxfIndexGroup[] Groups)[] records =
            [E("SECTION", G(2, "BLOCKS")), .. blocks, E("ENDSEC"),
             E("SECTION", G(2, "ENTITIES")), .. model, E("ENDSEC"), E("EOF")];
        return DxfInsertIndex.Build(records.Select((r, i) =>
            new DxfIndexEntity(r.Type, r.Groups.ToImmutableArray(), i)).ToArray(), raw => raw);
    }
    private static DxfInsertLayerResolver Resolver() =>
        new([new DxfInsertLayerState("0", false, true, "CONTINUOUS", true)], raw => raw);
    private static ImmutableArray<DxfExpandedContour> Expand(DxfInsertIndex index, double units = 1,
        double tolerance = 0, CancellationToken token = default) =>
        DxfInsertExpander.Expand(index, units, raw => raw, Resolver(), tolerance, token);
    private static void AssertPoint(Point2 expected, Point2 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
    }

    [Theory]
    [InlineData("90", 10, 20, 10, 16, 7, 20)]
    [InlineData("-270.000", 10, 20, 10, 16, 7, 20)]
    public void ExactCardinalRotationAndNegativeScalePreserveTheExpectedShape(
        string angle, double x0, double y0, double x1, double y1, double x2, double y2)
    {
        var index = Index(Block("A", "0", "0", Poly()), Insert("A", "10", "20",
            G(41, "-2"), G(42, "3"), G(50, angle)));
        var result = Assert.Single(Expand(index));
        AssertPoint(new(x0, y0), result.Points[0]);
        AssertPoint(new(x1, y1), result.Points[1]);
        AssertPoint(new(x2, y2), result.Points[2]);
    }

    [Theory]
    [InlineData("89.999999999999999")]
    [InlineData("90.000000000000001")]
    [InlineData("45")]
    public void NonCardinalRawAngleIsRejectedEvenWhenBinary64RoundsItToNinety(string angle)
    {
        var index = Index(Block("A", "0", "0", Poly()), Insert("A", "0", "0", G(50, angle)));
        Assert.Throws<InvalidDataException>(() => Expand(index));
    }

    [Fact]
    public void NestedRotationsAndReflectionsUseInnerBeforeOuter()
    {
        var blocks = Block("A", "1", "1", Insert("B", "3", "4", G(50, "90")))
            .Concat(Block("B", "0", "0", Poly())).ToArray();
        var index = Index(blocks, Insert("A", "100", "200", G(41, "-2"), G(42, "3")));
        var result = Assert.Single(Expand(index));
        AssertPoint(new(96, 209), result.Points[0]);
        AssertPoint(new(96, 215), result.Points[1]);
        AssertPoint(new(98, 209), result.Points[2]);
        Assert.Equal(2, result.InsertPath.Length);
    }

    [Fact]
    public void DistinctLargeDecimalsAreRejectedWhenSubtractionChangesLength()
    {
        var index = Index(Block("A", "10000000000000002", "0",
            Poly("10000000000000001", "10000000000000004", "10000000000000002")), Insert("A"));
        Assert.Throws<InvalidDataException>(() => Expand(index));
    }

    [Fact]
    public void LargeInsertionCannotSilentlyLoseAUnitEvenWhenOutputFitsTheDomain()
    {
        var index = Index(Block("A", "0", "0", Poly()), Insert("A", "10000000000000001", "0"));
        Assert.Throws<InvalidDataException>(() => Expand(index, 0.000000001));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(25.4)]
    public void UnitFactorUsesExplicitToleranceAgainstRoundTripDecimal(double factor)
    {
        var index = Index(Block("A", "0", "0", Poly()), Insert("A"));
        var result = Assert.Single(Expand(index, factor, 1e-12));
        AssertPoint(new(2 * factor, 0), result.Points[1]);
        AssertPoint(new(0, factor), result.Points[2]);
    }

    [Fact]
    public void CancellationAndInvalidToleranceDoNotModifyTheSourceIndex()
    {
        var index = Index(Block("A", "0", "0", Poly()), Insert("A"));
        var before = index.Blocks[0].Entities[0].Entity.Groups.ToArray();
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Expand(index, token: source.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => Expand(index, tolerance: -1));
        Assert.Equal(before, index.Blocks[0].Entities[0].Entity.Groups.ToArray());
    }

    [Fact]
    public void ExpandedPointsTimesDepthCannotExceedThePrecisionWorkBudget()
    {
        const int points = 10_000;
        const int depth = 11;
        var groups = new List<DxfIndexGroup> { G(8, "0"), G(90, points.ToString(CultureInfo.InvariantCulture)), G(70, "0") };
        for (int i = 0; i < points; i++)
        {
            groups.Add(G(10, i.ToString(CultureInfo.InvariantCulture)));
            groups.Add(G(20, (i % 2).ToString(CultureInfo.InvariantCulture)));
        }
        var blocks = new List<(string Type, DxfIndexGroup[] Groups)>();
        for (int i = 0; i < depth; i++)
        {
            var name = "B" + i.ToString(CultureInfo.InvariantCulture);
            var body = i + 1 == depth ? E("LWPOLYLINE", groups.ToArray()) : Insert("B" + (i + 1).ToString(CultureInfo.InvariantCulture));
            blocks.AddRange(Block(name, "0", "0", body));
        }
        var index = Index(blocks.ToArray(), Insert("B0"));
        Assert.Throws<InvalidDataException>(() => Expand(index));
    }
}
