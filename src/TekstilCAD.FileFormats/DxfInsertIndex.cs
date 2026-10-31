using System.Collections.Immutable;
using System.Globalization;

namespace TekstilCAD.FileFormats;

// This is an import-internal structural index, not another DXF reader. Its
// input must come from the already decoded, bounded and sanitized snapshot.
internal readonly record struct DxfIndexGroup(int Code, string Value, string RawValue);
internal sealed record DxfIndexEntity(string Type, ImmutableArray<DxfIndexGroup> Groups, int SourceOrdinal);
internal sealed record DxfIndexedEntity(DxfIndexEntity Entity, ImmutableArray<DxfIndexEntity> Children, DxfIndexEntity? SequenceEnd);
internal sealed record DxfIndexedBlock(string Name, DxfIndexEntity Header, ImmutableArray<DxfIndexedEntity> Entities, DxfIndexEntity End);

/// <summary>
/// Separates block definitions from ENTITIES and preserves sequence ownership.
/// It does not resolve INSERT references, expand geometry, or validate DXF
/// coordinate systems, visibility, block flags or layer inheritance.
/// </summary>
internal sealed record DxfInsertIndex(ImmutableArray<DxfIndexedBlock> Blocks, ImmutableArray<DxfIndexedEntity> ModelEntities)
{
    public static DxfInsertIndex Build(IReadOnlyList<DxfIndexEntity> entities, Func<string, string> decodeBlockName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(decodeBlockName);
        var blocks = ImmutableArray.CreateBuilder<DxfIndexedBlock>();
        var modelEntities = ImmutableArray.CreateBuilder<DxfIndexedEntity>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previousOrdinal = -1;
        string? section = null;
        DxfIndexEntity? blockHeader = null;
        string? blockName = null;
        ImmutableArray<DxfIndexedEntity>.Builder? blockEntities = null;

        for (var i = 0; i < entities.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = entities[i] ?? throw new InvalidDataException("DXF varlığı boş olamaz.");
            ValidateEntity(current, ref previousOrdinal);
            if (current.Type == "SECTION")
            {
                if (section is not null || blockHeader is not null) throw new InvalidDataException("DXF SECTION sınırları iç içe geçemez.");
                section = SingleGroup(current, 2, true)!.Value.Value;
                continue;
            }
            if (current.Type == "ENDSEC")
            {
                if (section is null || blockHeader is not null) throw new InvalidDataException("DXF ENDSEC öncesinde SECTION veya ENDBLK eksik.");
                section = null;
                continue;
            }
            if (section is not ("BLOCKS" or "ENTITIES")) continue;
            if (current.Type == "BLOCK")
            {
                if (section != "BLOCKS" || blockHeader is not null) throw new InvalidDataException("DXF BLOCK sınırı geçersiz.");
                var nameGroup = SingleGroup(current, 2, true)!.Value;
                var decoded = decodeBlockName(nameGroup.RawValue);
                if (string.IsNullOrWhiteSpace(decoded)) throw new InvalidDataException("DXF blok adı boş.");
                if (!names.Add(decoded)) throw new InvalidDataException("DXF blok adı yinelenmiş: " + decoded);
                blockHeader = current;
                blockName = decoded;
                blockEntities = ImmutableArray.CreateBuilder<DxfIndexedEntity>();
                continue;
            }
            if (current.Type == "ENDBLK")
            {
                if (section != "BLOCKS" || blockHeader is null || blockEntities is null || blockName is null)
                    throw new InvalidDataException("DXF ENDBLK için BLOCK bulunamadı.");
                blocks.Add(new(blockName, blockHeader, blockEntities.ToImmutable(), current));
                blockHeader = null; blockName = null; blockEntities = null;
                continue;
            }
            if (section == "BLOCKS" && blockHeader is null)
                throw new InvalidDataException("DXF BLOCKS içinde BLOCK dışı varlık var.");
            if (current.Type is "VERTEX" or "ATTRIB" or "SEQEND")
                throw new InvalidDataException("DXF dizi alt varlığının sahibi bulunamadı: " + current.Type);

            var children = ImmutableArray.CreateBuilder<DxfIndexEntity>();
            DxfIndexEntity? end = null;
            var childType = current.Type == "POLYLINE" ? "VERTEX" : current.Type == "INSERT" && HasAttributes(current) ? "ATTRIB" : null;
            if (childType is not null)
            {
                while (i + 1 < entities.Count && entities[i + 1].Type == childType)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var child = entities[++i];
                    ValidateEntity(child, ref previousOrdinal);
                    children.Add(child);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (i + 1 >= entities.Count || entities[i + 1].Type != "SEQEND")
                    throw new InvalidDataException($"DXF {current.Type} SEQEND kaydı eksik.");
                end = entities[++i];
                ValidateEntity(end, ref previousOrdinal);
            }
            var indexed = new DxfIndexedEntity(current, children.ToImmutable(), end);
            if (section == "BLOCKS") blockEntities!.Add(indexed);
            else modelEntities.Add(indexed);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (blockHeader is not null) throw new InvalidDataException("DXF ENDBLK kaydı eksik.");
        if (section is not null) throw new InvalidDataException("DXF ENDSEC kaydı eksik.");
        return new(blocks.ToImmutable(), modelEntities.ToImmutable());
    }

    private static bool HasAttributes(DxfIndexEntity entity)
    {
        var flag = SingleGroup(entity, 66, false);
        if (flag is null) return false;
        if (!int.TryParse(flag.Value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value is not (0 or 1))
            throw new InvalidDataException("DXF INSERT attribute bayrağı geçersiz.");
        return value == 1;
    }

    private static DxfIndexGroup? SingleGroup(DxfIndexEntity entity, int code, bool required)
    {
        DxfIndexGroup? result = null;
        foreach (var group in entity.Groups)
        {
            if (group.Code != code) continue;
            if (result is not null) throw new InvalidDataException($"DXF {entity.Type} için {code} alanı yinelenmiş.");
            result = group;
        }
        if (required && (result is null || string.IsNullOrWhiteSpace(result.Value.Value)))
            throw new InvalidDataException($"DXF {entity.Type} için {code} alanı eksik.");
        return result;
    }

    private static void ValidateEntity(DxfIndexEntity entity, ref int previousOrdinal)
    {
        if (string.IsNullOrEmpty(entity.Type) || entity.Groups.IsDefault || entity.SourceOrdinal <= previousOrdinal)
            throw new InvalidDataException("DXF varlık türü, grupları veya kaynak sırası geçersiz.");
        previousOrdinal = entity.SourceOrdinal;
    }
}
