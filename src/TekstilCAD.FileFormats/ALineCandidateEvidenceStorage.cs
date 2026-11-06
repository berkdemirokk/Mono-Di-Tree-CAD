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

    // All callers must keep supplied collections and documents stable until capture
    // completes. Every later validation, comparison and serialization uses only copies.
    private static double[] CopyExact(IReadOnlyList<double> values,int count)
    {
        ArgumentNullException.ThrowIfNull(values);
        if(values.Count!=count)throw new InvalidDataException("Aday alan sayısı geçersiz.");
        var copy=new double[count];for(var i=0;i<count;i++)copy[i]=values[i];return copy;
    }
    private static ALineCandidateEvidence Capture(ALineCandidateEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence with { InputsMm=CopyExact(evidence.InputsMm,11),OriginalEaseMm=CopyExact(evidence.OriginalEaseMm,4),CandidateEaseMm=CopyExact(evidence.CandidateEaseMm,4),
            FrontCoordinatesMm=CopyExact(evidence.FrontCoordinatesMm,10),BackCoordinatesMm=CopyExact(evidence.BackCoordinatesMm,10) };
    }
    public static ALineCandidateEvidence Create(Guid frontId,Guid backId,string sourceSha256,IReadOnlyList<double> inputs,IReadOnlyList<double> oldEase,IReadOnlyList<double> candidateEase,IReadOnlyList<double> frontCoordinates,IReadOnlyList<double> backCoordinates)
    {
        ArgumentNullException.ThrowIfNull(inputs);ArgumentNullException.ThrowIfNull(oldEase);ArgumentNullException.ThrowIfNull(candidateEase);ArgumentNullException.ThrowIfNull(frontCoordinates);ArgumentNullException.ThrowIfNull(backCoordinates);
        var evidence=new ALineCandidateEvidence(frontId,backId,sourceSha256,ALineExperimentalProvenance.CurrentAlgorithm,ALineWorkDraftStorage.BalanceMethod,Assumptions,
            CopyExact(inputs,11),CopyExact(oldEase,4),CopyExact(candidateEase,4),CopyExact(frontCoordinates,10),CopyExact(backCoordinates,10),"");
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
        var originalFront=ToPoints(Panel(e.InputsMm[0]+e.OriginalEaseMm[0],e.InputsMm[2]+e.OriginalEaseMm[2]));
        var originalBack=ToPoints(Panel(e.InputsMm[1]+e.OriginalEaseMm[1],e.InputsMm[3]+e.OriginalEaseMm[3]));
        Geometry.ValidateContour(originalFront);Geometry.ValidateContour(originalBack);
        var frontPoints=ToPoints(e.FrontCoordinatesMm);var backPoints=ToPoints(e.BackCoordinatesMm);
        Geometry.ValidateContour(frontPoints);Geometry.ValidateContour(backPoints);
        var frontUpper=Geometry.Distance(frontPoints[1],frontPoints[2]);var backUpper=Geometry.Distance(backPoints[1],backPoints[2]);
        var frontLower=Geometry.Distance(frontPoints[2],frontPoints[3]);var backLower=Geometry.Distance(backPoints[2],backPoints[3]);
        var bound=1e-10*Math.Max(1,Math.Max(frontUpper+frontLower,backUpper+backLower));
        if(Math.Abs(frontUpper-backUpper)>bound || Math.Abs(frontLower-backLower)>bound)throw new InvalidDataException("Aday yan aralıkları sayısal kontrolde eşleşmedi.");
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
    public static void Validate(ALineCandidateEvidence evidence)=>ValidateSnapshot(Capture(evidence));
    private static void ValidateSnapshot(ALineCandidateEvidence evidence)
    {
        ValidateShape(evidence);
        if(!Hash(evidence.IntegritySha256) || evidence.IntegritySha256!=ComputeHash(evidence))throw new InvalidDataException("Aday kanıtının bütünlük özeti uyuşmuyor.");
    }
    private sealed record PanelSnapshot(Guid Id,bool Closed,ALineExperimentalProvenance? Marker,Point2[] Points);
    /// <summary>The caller must keep inputs and document stable during capture. Later comparison uses only detached copies.</summary>
    public static bool Matches(ALineCandidateEvidence evidence,ProjectDocument document,string sourceSha256,IReadOnlyList<double> inputs,IReadOnlyList<double> candidateEase)
        =>MatchesCore(evidence,document,sourceSha256,inputs,candidateEase,null);
    internal static bool MatchesForVerification(ALineCandidateEvidence evidence,ProjectDocument document,string sourceSha256,IReadOnlyList<double> inputs,IReadOnlyList<double> candidateEase,Action afterCapture)
        =>MatchesCore(evidence,document,sourceSha256,inputs,candidateEase,afterCapture);
    private static bool MatchesCore(ALineCandidateEvidence evidence,ProjectDocument document,string sourceSha256,IReadOnlyList<double> inputs,IReadOnlyList<double> candidateEase,Action? afterCapture)
    {
        var captured=Capture(evidence);ArgumentNullException.ThrowIfNull(document);
        var capturedInputs=CopyExact(inputs,11);var capturedEase=CopyExact(candidateEase,4);
        if(document.Pieces is null || document.Pieces.Count>ProjectStorage.MaximumPieces)return false;
        PanelSnapshot? Panel(Guid id)
        {
            PatternPiece? match=null;
            foreach(var p in document.Pieces) { if(p is null)return null;if(p.Id!=id)continue;if(match is not null)return null;match=p; }
            if(match?.Points is not { Count:5 })return null;
            var points=new Point2[5];for(var i=0;i<points.Length;i++)points[i]=match.Points[i];
            return new(match.Id,match.Closed,match.ExperimentalALine,points);
        }
        var front=Panel(captured.FrontPieceId);var back=Panel(captured.BackPieceId);afterCapture?.Invoke();
        ValidateSnapshot(captured);
        if(sourceSha256!=captured.SourceSha256 || !Same(capturedInputs,captured.InputsMm) || !Same(capturedEase,captured.CandidateEaseMm))return false;
        bool MatchPanel(PanelSnapshot? panel,string side,double[] coordinates)=>panel is not null && panel.Closed &&
            panel.Marker is { } marker && marker.Algorithm==captured.Algorithm && marker.SourceSha256==sourceSha256 && marker.Side==side &&
            panel.Points.SelectMany(q=>new[]{q.X,q.Y}).Zip(coordinates).All(pair=>Same(pair.First,pair.Second));
        return MatchPanel(front,"front",captured.FrontCoordinatesMm) && MatchPanel(back,"back",captured.BackCoordinatesMm);
    }
    /// <summary>The caller must keep the supplied arrays stable until capture completes.</summary>
    public static string Serialize(ALineCandidateEvidence evidence)=>SerializeCore(evidence,null);
    internal static string SerializeForVerification(ALineCandidateEvidence evidence,Action afterCapture)=>SerializeCore(evidence,afterCapture);
    private static string SerializeCore(ALineCandidateEvidence evidence,Action? afterCapture)
    {
        var captured=Capture(evidence);afterCapture?.Invoke();ValidateSnapshot(captured);
        var json=JsonSerializer.Serialize(new { format=Format,version=1,evidence=captured });
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
