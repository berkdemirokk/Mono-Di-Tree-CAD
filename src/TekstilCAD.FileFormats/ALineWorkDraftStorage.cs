using System.Text;
using System.Text.Json;

namespace TekstilCAD.FileFormats;

/// <summary>Unconfirmed, possibly incomplete form text; never geometry or measurement approval.</summary>
public sealed record ALineBalanceSnapshot(string Method,IReadOnlyList<double> Original,IReadOnlyList<double> Candidate);
public sealed record ALineWorkDraft(string? Unit, IReadOnlyList<string> Fields, string? SourceHash=null,ALineBalanceSnapshot? Balance=null);

public static class ALineWorkDraftStorage
{
    public const string Extension=".tcad-aline-work.json";
    public const string ModelVersion="A-04.lower-section.experimental.v1";
    public const int MaximumFileBytes=16*1024;
    public const string BalanceMethod="aline-side-balance-equal-width-v1";
    public const string SourceAlgorithm="design-request-json-sha256-v1";
    public static string SourceIdentity(DesignRequest request)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    private const string Format="tekstilcad-aline-work";

    public static string Serialize(ALineWorkDraft draft)
    {
        Validate(draft);
        if(draft.Balance is { } balance)return JsonSerializer.Serialize(new { format=Format,version=3,modelVersion=ModelVersion,sourceAlgorithm=SourceAlgorithm,sourceHash=draft.SourceHash,unit=draft.Unit,fields=draft.Fields,balance=new { method=balance.Method,original=balance.Original,candidate=balance.Candidate } });
        return draft.SourceHash is null ? JsonSerializer.Serialize(new { format=Format,version=1,modelVersion=ModelVersion,unit=draft.Unit,fields=draft.Fields }) :
            JsonSerializer.Serialize(new { format=Format,version=2,modelVersion=ModelVersion,sourceAlgorithm=SourceAlgorithm,sourceHash=draft.SourceHash,unit=draft.Unit,fields=draft.Fields });
    }
    private static void Validate(ALineWorkDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if(draft.Unit is not (null or "cm" or "mm") || draft.Fields is null || draft.Fields.Count!=11 ||
            draft.Fields.Any(f=>f is null || f.Length>24 || f.Any(char.IsControl)))
            throw new InvalidDataException("A kesim çalışma taslağının birimi veya 11 alanı geçersiz. Alanlar en fazla 24 karakter olabilir.");
        if(draft.SourceHash is not null && (draft.SourceHash.Length!=64 || draft.SourceHash.Any(c=>!(c is >= '0' and <= '9' or >= 'A' and <= 'F'))))
            throw new InvalidDataException("İstek özeti geçersiz.");
        if(draft.Balance is { } balance && (draft.SourceHash is null || draft.Unit is null || balance.Method!=BalanceMethod ||
            balance.Original is null || balance.Candidate is null || balance.Original.Count!=4 || balance.Candidate.Count!=4 ||
            balance.Original.Concat(balance.Candidate).Any(v=>!double.IsFinite(v) || v<0 || v>10000)))
            throw new InvalidDataException("Aday kaydı, kaynak kimliği veya bolluk değerleri geçersiz.");
        // Preserve unfinished input exactly, but reject malformed Unicode rather
        // than silently replacing it during JSON serialization.
        try { foreach(var field in draft.Fields)new UTF8Encoding(false,true).GetByteCount(field); }
        catch(EncoderFallbackException ex) { throw new InvalidDataException("Çalışma alanında geçersiz metin var.",ex); }
    }
    public static ALineWorkDraft Deserialize(string json)
    {
        try
        {
            if(Encoding.UTF8.GetByteCount(json)>MaximumFileBytes)throw new InvalidDataException("Çalışma taslağı 16 KiB sınırını aşıyor.");
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=4 });var root=doc.RootElement;
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in root.EnumerateObject())
                if(!names.Add(property.Name) || property.Name is not ("format" or "version" or "modelVersion" or "unit" or "fields" or "sourceAlgorithm" or "sourceHash" or "balance"))
                    throw new InvalidDataException("Çalışma taslağında bilinmeyen veya yinelenen alan var.");
            var version=root.GetProperty("version").GetInt32();
            if((version==1 ? names.Count!=5 : version==2 ? names.Count!=7 || root.GetProperty("sourceAlgorithm").GetString()!=SourceAlgorithm : version==3 ? names.Count!=8 || root.GetProperty("sourceAlgorithm").GetString()!=SourceAlgorithm : true) || root.GetProperty("format").GetString()!=Format ||
                root.GetProperty("modelVersion").GetString()!=ModelVersion)
                throw new InvalidDataException("Çalışma taslağı biçimi veya model sürümü desteklenmiyor.");
            var values=root.GetProperty("fields");
            if(values.GetArrayLength()!=11)throw new InvalidDataException("Çalışma taslağında tam 11 alan bulunmalı.");
            var fields=values.EnumerateArray().Select(e=>e.GetString()!).ToArray();
            ALineBalanceSnapshot? balance=null;
            if(version==3)
            {
                var node=root.GetProperty("balance");var keys=new HashSet<string>(StringComparer.Ordinal);
                foreach(var p in node.EnumerateObject())if(!keys.Add(p.Name) || p.Name is not ("method" or "original" or "candidate"))throw new InvalidDataException("Aday alanları geçersiz veya yinelenmiş.");
                if(keys.Count!=3)throw new InvalidDataException("Aday alanı eksik.");
                static double[] ReadValues(JsonElement value) { if(value.GetArrayLength()!=4)throw new InvalidDataException("Adayda dört bolluk değeri gerekli.");return value.EnumerateArray().Select(x=>x.GetDouble()).ToArray(); }
                balance=new(node.GetProperty("method").GetString() ?? "",Array.AsReadOnly(ReadValues(node.GetProperty("original"))),Array.AsReadOnly(ReadValues(node.GetProperty("candidate"))));
            }
            var draft=new ALineWorkDraft(root.GetProperty("unit").GetString(),Array.AsReadOnly(fields),version>=2 ? root.GetProperty("sourceHash").GetString() ?? throw new InvalidDataException("İstek özeti eksik.") : null,balance);Validate(draft);return draft;
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new InvalidDataException("A kesim çalışma taslağı okunamadı.",ex); }
    }
    public static ALineWorkDraft Load(string path)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>MaximumFileBytes)throw new InvalidDataException("Çalışma taslağı 16 KiB sınırını aşıyor.");
        using var memory=new MemoryStream();var buffer=new byte[4096];int count;
        while((count=file.Read(buffer))>0)
        { if(memory.Length+count>MaximumFileBytes)throw new InvalidDataException("Çalışma taslağı 16 KiB sınırını aşıyor.");memory.Write(buffer,0,count); }
        try { return Deserialize(new UTF8Encoding(false,true).GetString(memory.ToArray())); }
        catch(DecoderFallbackException ex) { throw new InvalidDataException("Çalışma taslağı UTF-8 metni geçersiz.",ex); }
    }
    public static void SaveNew(string path,ALineWorkDraft draft)
    {
        if(!path.EndsWith(Extension,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Dosya uzantısı "+Extension+" olmalı.");
        var target=Path.GetFullPath(path);ProjectStorage.EnsureWritableDestination(target);
        var text=Serialize(draft);
        ProjectStorage.AtomicWriteIfUnchanged(target,text,new ProjectFileVersion(target,null));
    }
}
