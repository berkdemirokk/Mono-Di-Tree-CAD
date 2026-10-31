using System.Collections.Immutable;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfInsertLayerResolverTests
{
    private static DxfIndexGroup G(int code, string value) => new(code, value, value);
    private static DxfIndexEntity Entity(string type, int ordinal, params DxfIndexGroup[] groups) => new(type, groups.ToImmutableArray(), ordinal);
    private static DxfInsertLayerState Layer(string name, bool hidden = false, bool plot = true, string type = "CONTINUOUS", bool approved = true)
        => new(name, hidden, plot, type, approved);
    private static DxfInsertLayerResolver Resolver(params DxfInsertLayerState[] layers) => new(layers, raw => raw);

    [Fact]
    public void LayerZeroAndByBlockUseEachInsertOccurrenceContext()
    {
        var resolver = Resolver(Layer("Kesim"), Layer("Dikiş"));
        var first = resolver.Resolve(Entity("INSERT", 1, G(8, "Kesim")), null, false);
        var second = resolver.Resolve(Entity("INSERT", 2, G(8, "Dikiş")), null, false);
        var nested = resolver.Resolve(Entity("INSERT", 3, G(8, "0"), G(6, "BYBLOCK")), first, true);
        var line = Entity("LWPOLYLINE", 4, G(8, "0"), G(6, "BYLAYER"));
        var firstResult = resolver.Resolve(line, nested, true);
        var secondResult = resolver.Resolve(line, second, true);
        Assert.Equal("Kesim", firstResult.EffectiveLayer);
        Assert.Equal("Dikiş", secondResult.EffectiveLayer);
        Assert.Equal(new[] { 1, 3 }, firstResult.InsertPath.ToArray());
        Assert.Equal(new[] { 2 }, secondResult.InsertPath.ToArray());
        Assert.True(firstResult.Visible && firstResult.PlotEligible);
    }

    [Fact]
    public void NonzeroChildLayerOverridesParentAndUsesItsOwnState()
    {
        var resolver = Resolver(Layer("A"), Layer("B", plot: false));
        var parent = resolver.Resolve(Entity("INSERT", 1, G(8, "A")), null, false);
        var child = resolver.Resolve(Entity("LWPOLYLINE", 2, G(8, "B")), parent, true);
        Assert.Equal("B", child.EffectiveLayer);
        Assert.True(child.Visible);
        Assert.False(child.PlotEligible);
    }

    [Fact]
    public void HiddenAncestorAndInvisibleEntitySuppressDescendants()
    {
        var resolver = Resolver(Layer("Hidden", hidden: true), Layer("Shown"));
        var parent = resolver.Resolve(Entity("INSERT", 1, G(8, "Hidden")), null, false);
        var child = resolver.Resolve(Entity("LWPOLYLINE", 2, G(8, "Shown")), parent, true);
        Assert.False(child.Visible);
        Assert.False(child.PlotEligible);
        var invisible = resolver.Resolve(Entity("INSERT", 3, G(8, "Shown"), G(60, "1")), null, false);
        Assert.False(resolver.Resolve(Entity("LWPOLYLINE", 4, G(8, "0")), invisible, true).Visible);
    }

    [Fact]
    public void MissingStateUnknownVisibilityOrUnapprovedMeaningIsRejected()
    {
        var entity = Entity("INSERT", 1, G(8, "A"));
        Assert.Throws<InvalidDataException>(() => Resolver().Resolve(entity, null, false));
        Assert.Throws<InvalidDataException>(() => Resolver(new DxfInsertLayerState("A", null, true, "CONTINUOUS", true)).Resolve(entity, null, false));
        Assert.Throws<InvalidDataException>(() => Resolver(Layer("A", approved: false)).Resolve(entity, null, false));
        Assert.Throws<InvalidDataException>(() => Resolver(Layer("A"), Layer("a")));
    }

    [Theory]
    [InlineData(67, "1")]
    [InlineData(410, "Layout1")]
    [InlineData(60, "2")]
    [InlineData(62, "1")]
    [InlineData(62, "0")]
    [InlineData(420, "16777215")]
    [InlineData(430, "book")]
    public void UnverifiedSpaceVisibilityOrColorIsRejected(int code, string value)
    {
        var resolver = Resolver(Layer("A"));
        Assert.Throws<InvalidDataException>(() => resolver.Resolve(Entity("INSERT", 1, G(8, "A"), G(code, value)), null, false));
    }

    [Fact]
    public void ByBlockAtRootUnknownLinetypeAndMalformedFieldsAreRejected()
    {
        var resolver = Resolver(Layer("A"), Layer("B", type: "DASHED"));
        Assert.Throws<InvalidDataException>(() => resolver.Resolve(Entity("INSERT", 1, G(8, "A"), G(6, "BYBLOCK")), null, false));
        Assert.Throws<InvalidDataException>(() => resolver.Resolve(Entity("INSERT", 1, G(8, "B")), null, false));
        Assert.Throws<InvalidDataException>(() => resolver.Resolve(Entity("INSERT", 1, G(8, "A"), G(8, "A")), null, false));
        Assert.Throws<InvalidDataException>(() => resolver.Resolve(Entity("LWPOLYLINE", 1, G(8, "A")), null, true));
    }

    [Fact]
    public void CancellationIsObserved()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Resolver(Layer("A")).Resolve(Entity("INSERT", 1, G(8, "A")), null, false, cts.Token));
    }
}
