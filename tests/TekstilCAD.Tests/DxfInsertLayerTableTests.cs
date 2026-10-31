using System.Collections.Immutable;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfInsertLayerTableTests
{
    private static DxfIndexGroup G(int code, string value) => new(code, value, value);
    private static DxfIndexEntity E(string type, int ordinal, params DxfIndexGroup[] groups) => new(type, groups.ToImmutableArray(), ordinal);
    private static DxfIndexEntity[] Drawing(params DxfIndexEntity[] records) =>
        [E("SECTION", 0, G(2, "TABLES")), E("TABLE", 1, G(2, "LAYER")), .. records, E("ENDTAB", 99), E("ENDSEC", 100), E("EOF", 101)];
    private static DxfIndexEntity Layer(string name, string flags = "0", string color = "7", string plot = "1") =>
        E("LAYER", 2, G(2, name), G(70, flags), G(62, color), G(290, plot), G(6, "CONTINUOUS"));
    private static ImmutableArray<DxfInsertLayerState> Read(params DxfIndexEntity[] records) => DxfInsertLayerTable.Read(records, raw => raw);

    [Fact]
    public void CompleteTableNeverApprovesCuttingMeaning()
    {
        var layer = Assert.Single(Read(Drawing(Layer("Kalıp"))));
        Assert.Equal("Kalıp", layer.Name);
        Assert.False(layer.Hidden);
        Assert.True(layer.PlotEnabled);
        Assert.Equal("CONTINUOUS", layer.LineType);
        Assert.False(layer.CuttingSemanticsApproved);
    }

    [Theory]
    [InlineData("1", "7", true)]
    [InlineData("0", "-7", true)]
    [InlineData("4", "7", false)]
    [InlineData("64", "7", false)]
    public void FrozenOffAndLockedHaveDifferentVisibility(string flags, string color, bool hidden)
        => Assert.Equal(hidden, Assert.Single(Read(Drawing(Layer("A", flags, color)))).Hidden);

    [Theory]
    [InlineData("2")]
    [InlineData("16")]
    [InlineData("32")]
    public void ViewportAndExternalReferenceFlagsAreUnknown(string flags)
        => Assert.Null(Assert.Single(Read(Drawing(Layer("A", flags)))).Hidden);

    [Fact]
    public void MissingFieldsRemainUnknownAndByLayerIsNotAResolvedLineType()
    {
        var item = E("LAYER", 2, G(2, "A"), G(6, "BYLAYER"));
        var layer = Assert.Single(Read(Drawing(item)));
        Assert.Null(layer.Hidden);
        Assert.Null(layer.PlotEnabled);
        Assert.Null(layer.LineType);
        Assert.False(layer.CuttingSemanticsApproved);
    }

    [Fact]
    public void ViewportDataMakesVisibilityUnknownEvenIfItAppearsAfterTable()
    {
        var records = Drawing(Layer("A")).Append(E("VIEWPORT", 102)).ToArray();
        Assert.Null(Assert.Single(Read(records)).Hidden);
    }

    [Fact]
    public void DuplicateNamesAndPropertiesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => Read(Drawing(Layer("A"), Layer("a"))));
        var duplicate = E("LAYER", 2, G(2, "A"), G(70, "0"), G(70, "1"));
        Assert.Throws<InvalidDataException>(() => Read(Drawing(duplicate)));
    }

    [Theory]
    [InlineData("-1", "7", "1")]
    [InlineData("8", "7", "1")]
    [InlineData("0", "0", "1")]
    [InlineData("0", "256", "1")]
    [InlineData("0", "7", "2")]
    public void InvalidFlagsColorAndPlotAreRejected(string flags, string color, string plot)
        => Assert.Throws<InvalidDataException>(() => Read(Drawing(Layer("A", flags, color, plot))));

    [Fact]
    public void MissingTableEndAndCancellationFailWithoutPartialResult()
    {
        Assert.Throws<InvalidDataException>(() => Read(E("SECTION", 0, G(2, "TABLES")), E("TABLE", 1, G(2, "LAYER")), Layer("A"), E("ENDSEC", 2)));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DxfInsertLayerTable.Read(Drawing(Layer("A")), raw => raw, cts.Token));
    }
}
