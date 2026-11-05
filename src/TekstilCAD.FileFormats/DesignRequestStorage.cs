using System.Text;
using System.Text.Json;

namespace TekstilCAD.FileFormats;

/// <summary>Portable offline design request; never a pattern or production approval.</summary>
public sealed record DesignReference(string Name,string MediaType,byte[] Data);
public sealed record DesignRequest(string Prompt,string Measurements,DesignReference? Reference, string? ModelId = null);

public static class DesignRequestStorage
{
    public const int MaximumTextLength=16_384;
    public const int MaximumReferenceBytes=8*1024*1024;
    public const int MaximumFileBytes=12*1024*1024;
    public const string Format="tekstilcad.design-request";
    private static readonly HashSet<string> ModelIds = new(StringComparer.Ordinal) { "wrap", "aline", "corset", "sheath", "straight", "shirt" };

    private static bool WellFormed(string value)
    {
        for(var i=0;i<value.Length;i++)
        {
            if(char.IsHighSurrogate(value[i])) { if(++i>=value.Length || !char.IsLowSurrogate(value[i]))return false; }
            else if(char.IsLowSurrogate(value[i]))return false;
        }
        return true;
    }
    public static void Validate(DesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if(request.ModelId is not null && !ModelIds.Contains(request.ModelId))
            throw new InvalidDataException("Model kimliği desteklenmiyor.");
        if(request.Prompt is null || request.Measurements is null || request.Prompt.Length>MaximumTextLength || request.Measurements.Length>MaximumTextLength || !WellFormed(request.Prompt) || !WellFormed(request.Measurements))
            throw new InvalidDataException("İstek metni veya ölçüler sınırı aşıyor.");
        if(string.IsNullOrWhiteSpace(request.Prompt) && request.Reference is null)
            throw new InvalidDataException("Açıklama veya referans görseli gereklidir.");
        if(request.Reference is { } r)
        {
            if(string.IsNullOrWhiteSpace(r.Name) || r.Name.Length>255 || !WellFormed(r.Name) || r.Name.IndexOfAny(['/', '\\', ':'])>=0 || r.Name is "." or "..")
                throw new InvalidDataException("Görsel adı geçersiz.");
            if(r.Data is null || r.Data.Length==0 || r.Data.Length>MaximumReferenceBytes)
                throw new InvalidDataException("Görsel boyutu geçersiz.");
            bool png=r.Data.Length>=8 && r.Data.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10});
            bool jpeg=r.Data.Length>=3 && r.Data[0]==0xff && r.Data[1]==0xd8 && r.Data[2]==0xff;
            if(!(r.MediaType=="image/png" && png || r.MediaType=="image/jpeg" && jpeg))
                throw new InvalidDataException("Yalnız PNG veya JPEG referans baytları desteklenir.");
        }
    }
    public static string Serialize(DesignRequest request)
    {
        Validate(request);
        var reference=request.Reference is null ? null : new { name=request.Reference.Name,mediaType=request.Reference.MediaType,dataBase64=Convert.ToBase64String(request.Reference.Data) };
        return JsonSerializer.Serialize(new { format=Format,version=1,prompt=request.Prompt,measurements=request.Measurements,reference });
    }
    private static void Fields(JsonElement obj,params string[] names)
    {
        if(obj.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("İstek nesnesi geçersiz.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in obj.EnumerateObject())if(!names.Contains(p.Name,StringComparer.Ordinal) || !seen.Add(p.Name))throw new InvalidDataException("Bilinmeyen veya tekrarlı alan.");
        if(seen.Count!=names.Length)throw new InvalidDataException("Zorunlu alan eksik.");
    }
    private static string Text(JsonElement obj,string key)
    {
        var field=obj.GetProperty(key);
        return field.ValueKind==JsonValueKind.String ? field.GetString()! : throw new InvalidDataException("Metin alanı geçersiz.");
    }
    public static DesignRequest Deserialize(string json)
    {
        if(json is null || Encoding.UTF8.GetByteCount(json)>MaximumFileBytes)throw new InvalidDataException("İstek dosyası çok büyük.");
        try
        {
            using var parsed=JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=8 });var root=parsed.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("version",out var versionField) || versionField.ValueKind!=JsonValueKind.Number || !versionField.TryGetInt32(out var version) || version is not (1 or 2))
                throw new InvalidDataException("İstek biçimi veya sürümü desteklenmiyor.");
            if(version==1)Fields(root,"format","version","prompt","measurements","reference");
            else Fields(root,"format","version","prompt","measurements","reference","model");
            if(Text(root,"format")!=Format)throw new InvalidDataException("İstek biçimi veya sürümü desteklenmiyor.");
            string? modelId=null;
            if(version==2)
            {
                var model=root.GetProperty("model");
                if(model.ValueKind!=JsonValueKind.Null)
                {
                    Fields(model,"id");modelId=Text(model,"id");
                    if(!ModelIds.Contains(modelId))throw new InvalidDataException("Model kimliği desteklenmiyor.");
                }
            }
            var reference=root.GetProperty("reference");DesignReference? image=null;
            if(reference.ValueKind!=JsonValueKind.Null)
            {
                Fields(reference,"name","mediaType","dataBase64");var encoded=Text(reference,"dataBase64");
                if(encoded.Length>((MaximumReferenceBytes+2)/3)*4)throw new InvalidDataException("Görsel çok büyük.");
                image=new(Text(reference,"name"),Text(reference,"mediaType"),Convert.FromBase64String(encoded));
            }
            // This platform-independent library has no bounded image decoder.
            // A signature alone cannot validate a V2 still image.
            if(version==2 && image is not null)throw new InvalidDataException("V2 referans görseli bu sürümde doğrulanamıyor.");
            var result=new DesignRequest(Text(root,"prompt"),Text(root,"measurements"),image,modelId);Validate(result);return result;
        }
        catch(Exception e) when(e is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        { throw new InvalidDataException("İstek dosyası okunamadı.",e); }
    }
    public static DesignRequest Load(string path)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>MaximumFileBytes)throw new InvalidDataException("İstek dosyası çok büyük.");
        using var memory=new MemoryStream();var buffer=new byte[81920];int n;
        while((n=file.Read(buffer))>0) { if(memory.Length+n>MaximumFileBytes)throw new InvalidDataException("İstek dosyası çok büyük.");memory.Write(buffer,0,n); }
        return Deserialize(new UTF8Encoding(false,true).GetString(memory.ToArray()));
    }
    public static void SaveNew(string path,DesignRequest request)
    {
        if(!path.EndsWith(".tcad-request.json",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("İstek dosyası .tcad-request.json ile bitmelidir.");
        var target=Path.GetFullPath(path);ProjectStorage.EnsureWritableDestination(target);
        ProjectStorage.AtomicWriteIfUnchanged(target,Serialize(request),new ProjectFileVersion(target,null));
    }
}
