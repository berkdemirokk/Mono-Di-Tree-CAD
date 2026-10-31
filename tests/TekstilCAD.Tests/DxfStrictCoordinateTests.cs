using System.Collections.Immutable;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfStrictCoordinateTests
{
    private static DxfIndexGroup G(int code, string value) => new(code, value, value);
    private static (string Type, DxfIndexGroup[] Groups) E(string type, params DxfIndexGroup[] groups) => (type, groups);
    private static (string Type, DxfIndexGroup[] Groups) Polygon() => E("LWPOLYLINE",
        G(90, "3"), G(70, "1"), G(10, "0"), G(20, "0"),
        G(10, "1"), G(20, "0"), G(10, "0"), G(20, "1"));
    private static DxfIndexGroup[] PointGroups(int? omittedCode) =>
        new[] { G(10, "0"), G(20, "0") }.Where(group => group.Code != omittedCode).ToArray();
    private static DxfIndexEntity[] Entities(params (string Type, DxfIndexGroup[] Groups)[] source) =>
        source.Select((item, ordinal) => new DxfIndexEntity(item.Type, item.Groups.ToImmutableArray(), ordinal)).ToArray();
    private static DxfInsertIndex Index(bool nested, string missingEntity, int missingCode)
    {
        var rootHeader = E("BLOCK", [G(2, "A"), .. PointGroups(missingEntity == "root-block" ? missingCode : null)]);
        var rootInsert = E("INSERT", [G(2, "A"), .. PointGroups(missingEntity == "root-insert" ? missingCode : null)]);
        var childHeader = E("BLOCK", [G(2, "B"), .. PointGroups(missingEntity == "child-block" ? missingCode : null)]);
        var childInsert = E("INSERT", [G(2, "B"), .. PointGroups(missingEntity == "child-insert" ? missingCode : null)]);
        var records = new List<(string Type, DxfIndexGroup[] Groups)>
        {
            E("SECTION", G(2, "BLOCKS")), rootHeader,
            nested ? childInsert : Polygon(), E("ENDBLK")
        };
        if (nested) records.AddRange([childHeader, Polygon(), E("ENDBLK")]);
        records.AddRange([E("ENDSEC"), E("SECTION", G(2, "ENTITIES")), rootInsert, E("ENDSEC"), E("EOF")]);
        return DxfInsertIndex.Build(Entities(records.ToArray()), value => value);
    }
    private static void ExpandStrict(DxfInsertIndex index)
    {
        var resolver = new DxfInsertLayerResolver([new DxfInsertLayerState("0", false, true, "CONTINUOUS", true)], value => value);
        _ = DxfInsertExpander.Expand(index, 1, value => value, resolver);
    }

    [Theory]
    [InlineData("root-block", 10)] [InlineData("root-block", 20)]
    [InlineData("root-insert", 10)] [InlineData("root-insert", 20)]
    public void StrictModeRejectsMissingRootPointComponents(string entity, int code)
    {
        var error = Assert.Throws<InvalidDataException>(() => ExpandStrict(Index(false, entity, code)));
        Assert.Contains(code.ToString(), error.Message);
    }

    [Theory]
    [InlineData("child-block", 10)] [InlineData("child-block", 20)]
    [InlineData("child-insert", 10)] [InlineData("child-insert", 20)]
    public void StrictModeRejectsMissingNestedPointComponents(string entity, int code)
    {
        var error = Assert.Throws<InvalidDataException>(() => ExpandStrict(Index(true, entity, code)));
        Assert.Contains(code.ToString(), error.Message);
    }

    [Fact]
    public void CallbackOnlyLegacyPathRetainsItsExistingMissingCoordinateBehavior()
    {
        var index = Index(false, "root-insert", 10);
        Assert.Single(DxfInsertExpander.Expand(index, 1, value => value, _ => true));
    }
}
