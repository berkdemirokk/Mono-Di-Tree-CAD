using System.Collections.Immutable;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

/// <summary>Tests for the source index only; this does not test DXF import or geometry expansion.</summary>
public sealed class DxfInsertIndexTests
{
    private static DxfIndexGroup G(int code, string value) => new(code, value, value);
    private static (string Type, DxfIndexGroup[] Groups) E(string type, params DxfIndexGroup[] groups) => (type, groups);
    private static DxfIndexEntity[] Input(params (string Type, DxfIndexGroup[] Groups)[] entries) =>
        entries.Select((entry, ordinal) => new DxfIndexEntity(entry.Type, entry.Groups.ToImmutableArray(), ordinal)).ToArray();
    private static DxfInsertIndex Build(params (string Type, DxfIndexGroup[] Groups)[] entries) =>
        DxfInsertIndex.Build(Input(entries), raw => raw);

    [Fact]
    public void PreservesUnicodeBlockNamesAndSeparateInsertOccurrences()
    {
        var result = Build(
            E("SECTION", G(2, "BLOCKS")), E("BLOCK", G(2, "Gömlek_Ç"), G(10, "2"), G(20, "3")),
            E("LINE", G(10, "2"), G(20, "3"), G(11, "4"), G(21, "3")), E("ENDBLK"), E("ENDSEC"),
            E("SECTION", G(2, "ENTITIES")), E("INSERT", G(2, "Gömlek_Ç"), G(10, "1")),
            E("INSERT", G(2, "Gömlek_Ç"), G(10, "9")), E("ENDSEC"), E("EOF"));

        var block = Assert.Single(result.Blocks);
        Assert.Equal("Gömlek_Ç", block.Name);
        Assert.Equal("BLOCK", block.Header.Type);
        Assert.Equal("LINE", Assert.Single(block.Entities).Entity.Type);
        Assert.Equal("ENDBLK", block.End.Type);
        Assert.Equal(2, result.ModelEntities.Length);
        Assert.All(result.ModelEntities, entry => Assert.Equal("INSERT", entry.Entity.Type));
        Assert.NotEqual(result.ModelEntities[0].Entity.SourceOrdinal, result.ModelEntities[1].Entity.SourceOrdinal);
        Assert.Equal("1", result.ModelEntities[0].Entity.Groups.Single(g => g.Code == 10).Value);
        Assert.Equal("9", result.ModelEntities[1].Entity.Groups.Single(g => g.Code == 10).Value);
    }

    [Fact]
    public void PolylineAndInsertKeepTheirOwnChildrenAndSequenceEnd()
    {
        var index = Build(
            E("SECTION", G(2, "ENTITIES")),
            E("POLYLINE"), E("VERTEX", G(10, "1")), E("VERTEX", G(10, "2")), E("SEQEND"),
            E("INSERT", G(2, "A"), G(66, "1")), E("ATTRIB", G(1, "one")), E("SEQEND"),
            E("LINE"), E("ENDSEC"), E("EOF"));

        Assert.Equal(3, index.ModelEntities.Length);
        var polyline = index.ModelEntities[0];
        Assert.Equal("POLYLINE", polyline.Entity.Type);
        Assert.Equal(new[] { "VERTEX", "VERTEX" }, polyline.Children.Select(x => x.Type));
        Assert.Equal("SEQEND", Assert.IsType<DxfIndexEntity>(polyline.SequenceEnd).Type);
        var insert = index.ModelEntities[1];
        Assert.Equal("INSERT", insert.Entity.Type);
        Assert.Equal("ATTRIB", Assert.Single(insert.Children).Type);
        Assert.Equal("SEQEND", Assert.IsType<DxfIndexEntity>(insert.SequenceEnd).Type);
        Assert.Equal("LINE", index.ModelEntities[2].Entity.Type);
        Assert.Empty(index.ModelEntities[2].Children);
        Assert.Null(index.ModelEntities[2].SequenceEnd);
    }

    [Fact]
    public void DuplicateBlockNamesAreRejectedEvenWhenOnlyCaseDiffers()
    {
        Assert.Throws<InvalidDataException>(() => Build(
            E("SECTION", G(2, "BLOCKS")), E("BLOCK", G(2, "KALIP")), E("ENDBLK"),
            E("BLOCK", G(2, "kalip")), E("ENDBLK"), E("ENDSEC"), E("EOF")));
    }

    [Fact]
    public void MissingEndBlockIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Build(
            E("SECTION", G(2, "BLOCKS")), E("BLOCK", G(2, "A")), E("LINE"), E("ENDSEC"), E("EOF")));
    }

    [Fact]
    public void MissingSectionEndIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Build(
            E("SECTION", G(2, "ENTITIES")), E("LINE"), E("EOF")));
    }

    [Theory]
    [InlineData("POLYLINE", "VERTEX")]
    [InlineData("INSERT", "ATTRIB")]
    public void MissingSequenceEndIsRejected(string owner, string child)
    {
        var ownerEntry = owner == "INSERT" ? E(owner, G(2, "A"), G(66, "1")) : E(owner);
        Assert.Throws<InvalidDataException>(() => Build(
            E("SECTION", G(2, "ENTITIES")), ownerEntry, E(child), E("LINE"), E("ENDSEC"), E("EOF")));
    }

    [Theory]
    [InlineData("VERTEX")]
    [InlineData("ATTRIB")]
    [InlineData("SEQEND")]
    public void UnownedSequenceRecordsAreRejected(string type)
    {
        Assert.Throws<InvalidDataException>(() => Build(
            E("SECTION", G(2, "ENTITIES")), E(type), E("ENDSEC"), E("EOF")));
    }

    [Fact]
    public void AlreadyCancelledTokenAbortsIndexing()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var entities = Input(E("SECTION", G(2, "ENTITIES")), E("LINE"), E("ENDSEC"), E("EOF"));
        Assert.ThrowsAny<OperationCanceledException>(() => DxfInsertIndex.Build(entities, raw => raw, source.Token));
    }
}
