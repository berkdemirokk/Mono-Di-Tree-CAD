using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Detached, unapproved evidence. SHA-256 detects changes; it is not authentication or fit approval.</summary>
public sealed record ALineCandidateEvidence(Guid FrontPieceId,Guid BackPieceId,string SourceSha256,
    string Algorithm,string Method,string Assumptions,double[] InputsMm,double[] OriginalEaseMm,double[] CandidateEaseMm,
    double[] FrontCoordinatesMm,double[] BackCoordinatesMm,string IntegritySha256);

/// <summary>Versioned sidecar; never a project, transfer authorization or production approval.</summary>
public static class ALineCandidateEvidenceStorage
{
    public const string Extension=".tcad-aline-evidence.json";
    public const string Assumptions="symmetric-halves-equal-hem-straight-segments-v1";
    private const string Format="tekstilcad-aline-candidate-evidence";
    private const int MaximumBytes=16*1024;
    private static bool Hash(string? s)=>s is { Length:64 } && s.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool Same(double a,double b)=>BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b);
    private static bool Same(IReadOnlyList<double> a,IReadOnlyList<double> b)=>a.Count==b.Count && a.Zip(b).All(pair=>Same(pair.First,pair.Second));

    public static ALineCandidateEvidence Create(Guid frontId,Guid backId,string sourceSha256,IReadOnlyList<double> inputs,IReadOnlyList<double> oldEase,IReadOnlyList<double> candidateEase,IReadOnlyList<double> frontCoordinates,IReadOnlyList<double> backCoordinates)
    {
        ArgumentNullException.ThrowIfNull(inputs);ArgumentNullException.ThrowIfNull(oldEase);ArgumentNullException.ThrowIfNull(candidateEase);ArgumentNullException.ThrowIfNull(frontCoordinates);ArgumentNullException.ThrowIfNull(backCoordinates);
        var evidence=new ALineCandidateEvidence(frontId,backId,sourceSha256,ALineExperimentalProvenance.CurrentAlgorithm,ALineWorkDraftStorage.BalanceMethod,Assumptions,
            inputs.ToArray(),oldEase.ToArray(),candidateEase.ToArray(),frontCoordinates.ToArray(),backCoordinates.ToArray(),"");
        ValidateShape(evidence);return evidence with { IntegritySha256=ComputeHash(evidence) };
    }
    private static void ValidateShape(ALineCandidateEvidence e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if(e.FrontPieceId==Guid.Empty || e.BackPieceId==Guid.Empty || e.FrontPieceId==e.BackPieceId || !Hash(e.SourceSha256) ||
           e.Algorithm!=ALineExperimentalProvenance.CurrentAlgorithm || e.Method!=ALineWorkDraftStorage.BalanceMethod || e.Assumptions!=Assumptions ||
           e.InputsMm is not { Length:11 } || e.OriginalEaseMm is not { Length:4 } || e.CandidateEaseMm is not { Length:4 } ||
           e.FrontCoordinatesMm is not { Length:10 } || e.BackCoordinatesMm is not { Length:10 })
            throw new InvalidDataException("Aday kanıtının kimlik, yöntem veya alanları geçersiz.");
        if(e.InputsMm.Any(x=>!double.IsFinite(x) || x<0 || x>10000) || e.OriginalEaseMm.Concat(e.CandidateEaseMm).Any(x=>!double.IsFinite(x) || x<0 || x>10000) ||
           e.FrontCoordinatesMm.Concat(e.BackCoordinatesMm).Any(x=>!double.IsFinite(x)))
            throw new InvalidDataException("Aday kanıtındaki sayı veya sınır geçersiz.");
        if(!Same(e.InputsMm.Skip(4).Take(4).ToArray(),e.OriginalEaseMm))throw new InvalidDataException("Kaynak bolluklar girdiyle uyuşmuyor.");
        // Recompute this particular restricted method, using the same binary64 operation order.
        static (double Front,double Back) Balance(double front,double back,double fe,double be) { var total=fe+be;var f=(back-front+total)/2;return (f,total-f); }
        var w=Balance(e.InputsMm[0],e.InputsMm[1],e.InputsMm[4],e.InputsMm[5]);var h=Balance(e.InputsMm[2],e.InputsMm[3],e.InputsMm[6],e.InputsMm[7]);
        if(!Same(e.CandidateEaseMm,new[]{w.Front,w.Back,h.Front,h.Back}))throw new InvalidDataException("Aday bollukları yöntemle uyuşmuyor.");
        double[] Panel(double waist,double hip)=>[0,0,waist/2,0,hip/2,e.InputsMm[8],e.InputsMm[10]/4,e.InputsMm[9],0,e.InputsMm[9]];
        var front=Panel(e.InputsMm[0]+w.Front,e.InputsMm[2]+h.Front);var back=Panel(e.InputsMm[1]+w.Back,e.InputsMm[3]+h.Back);
        if(!Same(front,e.FrontCoordinatesMm) || !Same(back,e.BackCoordinatesMm))throw new InvalidDataException("Panel geometrileri adayla uyuşmuyor.");
        if(e.InputsMm[0]<=0 || e.InputsMm[1]<=0 || e.InputsMm[2]<=0 || e.InputsMm[3]<=0 || e.InputsMm[8]<=0 || e.InputsMm[9]<=e.InputsMm[8] || e.InputsMm[10]<=0 ||
           e.InputsMm[0]+e.OriginalEaseMm[0]>e.InputsMm[2]+e.OriginalEaseMm[2] || e.InputsMm[1]+e.OriginalEaseMm[1]>e.InputsMm[3]+e.OriginalEaseMm[3] ||
           e.InputsMm[10]/4<=(e.InputsMm[2]+e.OriginalEaseMm[2])/2 || e.InputsMm[10]/4<=(e.InputsMm[3]+e.OriginalEaseMm[3])/2 ||
           e.InputsMm[0]+w.Front>e.InputsMm[2]+h.Front || e.InputsMm[1]+w.Back>e.InputsMm[3]+h.Back ||
           e.InputsMm[10]/4<=(e.InputsMm[2]+h.Front)/2 || e.InputsMm[10]/4<=(e.InputsMm[3]+h.Back)/2)
            throw new InvalidDataException("Adayın deneysel geometri sınırları sağlanmıyor.");
        Geometry.ValidateContour(ToPoints(e.FrontCoordinatesMm));Geometry.ValidateContour(ToPoints(e.BackCoordinatesMm));
    }
    private static Point2[] ToPoints(double[] xy)=>Enumerable.Range(0,xy.Length/2).Select(i=>new Point2(xy[2*i],xy[2*i+1])).ToArray();
    private static string ComputeHash(ALineCandidateEvidence e)
    {
        using var memory=new MemoryStream();using(var writer=new BinaryWriter(memory,new UTF8Encoding(false,true),true))
        {
            writer.Write("TekstilCAD.aline-candidate-evidence.v1");writer.Write(e.FrontPieceId.ToByteArray());writer.Write(e.BackPieceId.ToByteArray());
            writer.Write(e.SourceSha256);writer.Write(e.Algorithm);writer.Write(e.Method);writer.Write(e.Assumptions);
            foreach(var vector in new[]{e.InputsMm,e.OriginalEaseMm,e.CandidateEaseMm,e.FrontCoordinatesMm,e.BackCoordinatesMm})
            { writer.Write(vector.Length);foreach(var value in vector)writer.Write(BitConverter.DoubleToInt64Bits(value)); }
        }
        return Convert.ToHexString(SHA256.HashData(memory.ToArray()));
    }
    public static void Validate(ALineCandidateEvidence evidence)
    {
        ValidateShape(evidence);
        if(!Hash(evidence.IntegritySha256) || evidence.IntegritySha256!=ComputeHash(evidence))throw new InvalidDataException("Aday kanıtının bütünlük özeti uyuşmuyor.");
    }
    /// <summary>Exact source, input, candidate and both current panel coordinates must match. Edits invalidate the association.</summary>
    public static bool Matches(ALineCandidateEvidence evidence,ProjectDocument document,string sourceSha256,IReadOnlyList<double> inputs,IReadOnlyList<double> candidateEase)
    {
        Validate(evidence);ArgumentNullException.ThrowIfNull(document);ArgumentNullException.ThrowIfNull(inputs);ArgumentNullException.ThrowIfNull(candidateEase);
        if(sourceSha256!=evidence.SourceSha256 || !Same(inputs,evidence.InputsMm) || !Same(candidateEase,evidence.CandidateEaseMm) || document.Pieces is null)return false;
        bool Panel(Guid id,string side,double[] coordinates)
        {
            var found=document.Pieces.Where(p=>p is not null && p.Id==id).ToArray();if(found.Length!=1)return false;var p=found[0];
            return p.Closed && p.ExperimentalALine is { } marker && marker.Algorithm==evidence.Algorithm && marker.SourceSha256==sourceSha256 && marker.Side==side &&
                p.Points is not null && p.Points.Count==coordinates.Length/2 && p.Points.SelectMany(q=>new[]{q.X,q.Y}).Zip(coordinates).All(pair=>Same(pair.First,pair.Second));
        }
        return Panel(evidence.FrontPieceId,"front",evidence.FrontCoordinatesMm) && Panel(evidence.BackPieceId,"back",evidence.BackCoordinatesMm);
    }
    public static string Serialize(ALineCandidateEvidence evidence)
    {
        Validate(evidence);var json=JsonSerializer.Serialize(new { format=Format,version=1,evidence });
        if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new InvalidDataException("Aday kanıtı boyut sınırını aşıyor.");return json;
    }
    public static ALineCandidateEvidence Deserialize(string json)
    {
        try
        {
            if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new InvalidDataException("Aday kanıtı boyut sınırını aşıyor.");
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=5 });var root=doc.RootElement;
            static void Fields(JsonElement node,params string[] names)
            {
                if(node.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Aday kanıtı nesnesi geçersiz.");
                var found=new HashSet<string>(StringComparer.Ordinal);foreach(var p in node.EnumerateObject())if(!names.Contains(p.Name,StringComparer.Ordinal) || !found.Add(p.Name))throw new InvalidDataException("Bilinmeyen veya yinelenen aday alanı.");
                if(found.Count!=names.Length)throw new InvalidDataException("Aday kanıtı alanı eksik.");
            }
            Fields(root,"format","version","evidence");if(root.GetProperty("format").GetString()!=Format || root.GetProperty("version").GetInt32()!=1)throw new InvalidDataException("Aday kanıtı sürümü desteklenmiyor.");
            var node=root.GetProperty("evidence");Fields(node,"FrontPieceId","BackPieceId","SourceSha256","Algorithm","Method","Assumptions","InputsMm","OriginalEaseMm","CandidateEaseMm","FrontCoordinatesMm","BackCoordinatesMm","IntegritySha256");
            static double[] Values(JsonElement n,int count) { if(n.ValueKind!=JsonValueKind.Array || n.GetArrayLength()!=count)throw new InvalidDataException("Aday alan sayısı geçersiz.");return n.EnumerateArray().Select(x=>x.GetDouble()).ToArray(); }
            var evidence=new ALineCandidateEvidence(node.GetProperty("FrontPieceId").GetGuid(),node.GetProperty("BackPieceId").GetGuid(),node.GetProperty("SourceSha256").GetString()!,node.GetProperty("Algorithm").GetString()!,node.GetProperty("Method").GetString()!,node.GetProperty("Assumptions").GetString()!,Values(node.GetProperty("InputsMm"),11),Values(node.GetProperty("OriginalEaseMm"),4),Values(node.GetProperty("CandidateEaseMm"),4),Values(node.GetProperty("FrontCoordinatesMm"),10),Values(node.GetProperty("BackCoordinatesMm"),10),node.GetProperty("IntegritySha256").GetString()!);
            Validate(evidence);return evidence;
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("Aday kanıtı okunamadı veya doğrulanamadı.",ex); }
    }
    public static void SaveNew(string path,ALineCandidateEvidence evidence)
    {
        if(!path.EndsWith(Extension,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Aday kanıtı uzantısı geçersiz.");
        var full=Path.GetFullPath(path);ProjectStorage.EnsureWritableDestination(full);var text=Serialize(evidence);
        ProjectStorage.AtomicWriteIfUnchanged(full,text,new ProjectFileVersion(full,null));
    }
    public static ALineCandidateEvidence Load(string path)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(file.Length>MaximumBytes)throw new InvalidDataException("Aday kanıtı boyut sınırını aşıyor.");
        using var memory=new MemoryStream();var buffer=new byte[4096];int n;while((n=file.Read(buffer))>0) { if(memory.Length+n>MaximumBytes)throw new InvalidDataException("Aday kanıtı boyut sınırını aşıyor.");memory.Write(buffer,0,n); }
        try { return Deserialize(new UTF8Encoding(false,true).GetString(memory.ToArray())); }
        catch(DecoderFallbackException ex) { throw new InvalidDataException("Aday kanıtı UTF-8 metni geçersiz.",ex); }
    }
}
