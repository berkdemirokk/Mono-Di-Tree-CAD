using System.Text.Json;
using System.Text.Json.Serialization;
using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Standalone trace bytes for research and validation. Not a project file format or save authorization.</summary>
public static class RotatedNestingTraceCodec
{
    public const int MaximumBytes=64*1024*1024;
    private static readonly JsonSerializerOptions Options=new() { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,IgnoreReadOnlyProperties=true };

    public static byte[] SerializeVerified(RotatedNestingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var bytes=JsonSerializer.SerializeToUtf8Bytes(record.ToTrace(),Options);
        if(bytes.Length>MaximumBytes)throw new InvalidDataException("Döndürme izi 64 MB sınırını aşıyor.");
        return bytes;
    }

    /// <summary>Checks raw JSON and revalidates the evidence against a detached caller-supplied layout.
    /// The caller must prevent layout mutation during this operation.</summary>
    public static RotatedNestingRecord DeserializeAndVerify(byte[] bytes,ProjectDocument layout,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();ArgumentNullException.ThrowIfNull(bytes);ArgumentNullException.ThrowIfNull(layout);
        if(bytes.Length>MaximumBytes)throw new InvalidDataException("Döndürme izi 64 MB sınırını aşıyor.");
        try
        {
            using var json=JsonDocument.Parse(bytes);
            ValidateRaw(json.RootElement,layout,token);
            var trace=json.RootElement.Deserialize<RotatedNestingTrace>(Options) ?? throw new InvalidDataException("Döndürme izi boş.");
            token.ThrowIfCancellationRequested();return RotatedNestingRecord.FromTrace(trace,layout,token);
        }
        catch(JsonException ex) { throw new InvalidDataException("Döndürme izi JSON biçimi geçersiz.",ex); }
    }

    private static void ValidateRaw(JsonElement root,ProjectDocument layout,CancellationToken token)
    {
        Fields(root,["Schema","Algorithm","TransformConvention","SourceFingerprint","LayoutFingerprint","SourcePieces","LayoutPieces","Selections","Copies","FabricWidth","Gap","EdgeMargin","UsedLength","MaxLength","GrainDirection","ToleranceDegrees"]);
        foreach(var field in new[]{"Schema","Algorithm","TransformConvention","SourceFingerprint","LayoutFingerprint","GrainDirection"})String(root.GetProperty(field),128);
        foreach(var field in new[]{"FabricWidth","Gap","EdgeMargin","UsedLength","ToleranceDegrees"})Number(root.GetProperty(field));
        var max=root.GetProperty("MaxLength");if(max.ValueKind!=JsonValueKind.Null)Number(max);
        var sources=root.GetProperty("SourcePieces");var historical=root.GetProperty("LayoutPieces");var selected=root.GetProperty("Selections");var copies=root.GetProperty("Copies");
        Array(sources,1,NestingEngine.MaximumPieces);Array(historical,1,NestingEngine.MaximumPieces);Array(selected,1,NestingEngine.MaximumPieces);Array(copies,1,NestingEngine.MaximumPieces);
        if(layout.Pieces is null || layout.Pieces.Count>NestingEngine.MaximumPieces)throw new InvalidDataException("Yerleşim sınırı geçersiz.");
        long total=0;
        foreach(var piece in layout.Pieces)
        {
            token.ThrowIfCancellationRequested();
            if(piece?.Points is null || piece.Points.Count>NestingEngine.MaximumVerticesPerPiece || (total+=piece.Points.Count)>NestingEngine.MaximumTotalVertices)
                throw new InvalidDataException("Yerleşim nokta sınırı geçersiz.");
        }
        foreach(var id in selected.EnumerateArray()) { token.ThrowIfCancellationRequested();GuidValue(id); }
        foreach(var copy in copies.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();Fields(copy,["SourcePieceId","CopyNumber","PlacedPieceId","Degrees"]);
            GuidValue(copy.GetProperty("SourcePieceId"));GuidValue(copy.GetProperty("PlacedPieceId"));
            Integer(copy.GetProperty("CopyNumber"));Integer(copy.GetProperty("Degrees"));
        }
        foreach(var piece in sources.EnumerateArray().Concat(historical.EnumerateArray()))
        {
            token.ThrowIfCancellationRequested();Fields(piece,["Id","Name","Points","Closed","Metadata","Grainline"],["Notches"]);
            GuidValue(piece.GetProperty("Id"));String(piece.GetProperty("Name"),1024);Boolean(piece.GetProperty("Closed"));
            var points=piece.GetProperty("Points");Array(points,2,NestingEngine.MaximumVerticesPerPiece);
            if((total+=points.GetArrayLength())>NestingEngine.MaximumTotalVertices)throw new InvalidDataException("Kaynak ve yerleşim toplam nokta sınırını aşıyor.");
            foreach(var p in points.EnumerateArray()) { token.ThrowIfCancellationRequested();Point(p); }
            var metadata=piece.GetProperty("Metadata");Fields(metadata,["Role","Size","Material","Quantity"]);
            NullableString(metadata.GetProperty("Role"),128);NullableString(metadata.GetProperty("Size"),128);NullableString(metadata.GetProperty("Material"),128);
            var quantity=metadata.GetProperty("Quantity");if(quantity.ValueKind!=JsonValueKind.Null)Integer(quantity);
            var grain=piece.GetProperty("Grainline");Fields(grain,["Start","End","NeedsReview"]);
            Point(grain.GetProperty("Start"));Point(grain.GetProperty("End"));Boolean(grain.GetProperty("NeedsReview"));
            if(piece.TryGetProperty("Notches",out var marks))
            {
                Array(marks,1,points.GetArrayLength());
                foreach(var mark in marks.EnumerateArray())
                { token.ThrowIfCancellationRequested();Fields(mark,["VertexIndex","Position"]);Integer(mark.GetProperty("VertexIndex"));Point(mark.GetProperty("Position")); }
            }
        }
    }

    private static void Fields(JsonElement value,string[] required,string[]? optional=null)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Döndürme izi nesnesi eksik.");
        var allowed=new HashSet<string>(required,StringComparer.Ordinal);if(optional is not null)allowed.UnionWith(optional);
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in value.EnumerateObject())
            if(!allowed.Contains(property.Name) || !seen.Add(property.Name))throw new InvalidDataException("Döndürme izinde bilinmeyen veya yinelenen alan var.");
        if(required.Any(name=>!seen.Contains(name)))throw new InvalidDataException("Döndürme izinde zorunlu alan eksik.");
    }
    private static void Point(JsonElement value)
    {
        // ProjectStorage historically includes the computed read-only field;
        // accept it only when it agrees with the required finite coordinates.
        Fields(value,["X","Y"],["IsFinite"]);Number(value.GetProperty("X"));Number(value.GetProperty("Y"));
        if(value.TryGetProperty("IsFinite",out var finite) && finite.ValueKind!=JsonValueKind.True)
            throw new InvalidDataException("Döndürme izi noktasının sonluluk alanı geçersiz.");
    }
    private static void Array(JsonElement value,int min,int max) { if(value.ValueKind!=JsonValueKind.Array || value.GetArrayLength()<min || value.GetArrayLength()>max)throw new InvalidDataException("Döndürme izi liste sınırı geçersiz."); }
    private static void String(JsonElement value,int max) { if(value.ValueKind!=JsonValueKind.String || value.GetString() is not { } s || s.Length>max)throw new InvalidDataException("Döndürme izi metni geçersiz."); }
    private static void NullableString(JsonElement value,int max) { if(value.ValueKind!=JsonValueKind.Null)String(value,max); }
    private static void Number(JsonElement value) { if(value.ValueKind!=JsonValueKind.Number || !value.TryGetDouble(out var d) || !double.IsFinite(d))throw new InvalidDataException("Döndürme izi sayısı geçersiz."); }
    private static void Integer(JsonElement value) { if(value.ValueKind!=JsonValueKind.Number || !value.TryGetInt32(out _))throw new InvalidDataException("Döndürme izi tamsayısı geçersiz."); }
    private static void GuidValue(JsonElement value) { if(value.ValueKind!=JsonValueKind.String || !value.TryGetGuid(out var id) || id==Guid.Empty)throw new InvalidDataException("Döndürme izi kimliği geçersiz."); }
    private static void Boolean(JsonElement value) { if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidDataException("Döndürme izi bayrağı geçersiz."); }
}
