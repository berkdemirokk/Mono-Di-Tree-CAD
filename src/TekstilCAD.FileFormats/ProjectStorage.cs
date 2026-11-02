using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.FileFormats;

/// <summary>A content version tied to one normalized path. Null SHA256 means the file did not exist.</summary>
public sealed record ProjectFileVersion(string FullPath, string? Sha256);
public sealed record ProjectLoadResult(ProjectDocument Project, ProjectFileVersion Version)
{
    /// <summary>Format from the exact byte snapshot used to load the project; null denotes the unversioned legacy form.</summary>
    public int? FormatVersion { get; init; }
}
public sealed class ProjectConflictException : IOException
{
    public ProjectConflictException() : base("Bu proje başka bir oturumda değiştirildi veya silindi. Değişiklikleriniz korunuyor; dosyayı yeniden açın ya da farklı kaydedin.") { }
}

public static class ProjectStorage
{
    // A repository-wide allocation decision is still outstanding. The scoped
    // override is solely for isolated synthetic verification, never UI consent.
    private static readonly AsyncLocal<bool> Version10VerificationOverride=new();
    internal static bool Version10WriteAvailable => Version10VerificationOverride.Value;
    internal static IDisposable EnableVersion10ForVerification()
    {
        var previous=Version10VerificationOverride.Value;Version10VerificationOverride.Value=true;
        return new VerificationScope(()=>Version10VerificationOverride.Value=previous);
    }
    private sealed class VerificationScope(Action cleanup) : IDisposable
    {
        private Action? cleanup=cleanup;
        public void Dispose()=>Interlocked.Exchange(ref cleanup,null)?.Invoke();
    }
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public const int MaximumPieces = 10_000;
    public const int MaximumPointsPerPiece = 10_000;
    public const int MaximumTotalPoints = 200_000;
    private const int MaximumProjectBytes = 64 * 1024 * 1024;
    public static void Save(ProjectDocument project, string path)
    {
        Validate(project);
        AtomicWrite(path, SerializeProject(project));
    }
    public static ProjectDocument Load(string path)
    {
        return LoadWithVersion(path).Project;
    }
    /// <summary>Parses and fingerprints the same byte snapshot, so callers cannot bind a document to a later version.</summary>
    public static ProjectLoadResult LoadWithVersion(string path) => LoadWithVersion(path, CancellationToken.None);
    public static ProjectLoadResult LoadWithVersion(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(path);
        var bytes = ReadSnapshot(full, cancellationToken) ?? throw new FileNotFoundException("Proje dosyası bulunamadı.", full);
        var project = DeserializeProject(bytes, cancellationToken);
        using var shape=JsonDocument.Parse(DecodeProjectText(bytes));
        var format=shape.RootElement.ValueKind==JsonValueKind.Array ? shape.RootElement[0].GetInt32() : (int?)null;
        return new(project, new(full, Convert.ToHexString(SHA256.HashData(bytes)))) { FormatVersion=format };
    }
    internal static ProjectDocument DeserializeProject(byte[] bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > MaximumProjectBytes) throw new InvalidDataException("Proje dosyası 64 MB sınırını aşıyor.");
        ProjectDocument project;
        long rotatedVisiblePoints=0;
        try
        {
            using var json = JsonDocument.Parse(DecodeProjectText(bytes));
            var root = json.RootElement;
            RejectDuplicateProperties(root, cancellationToken);
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() != 2 || root[0].ValueKind != JsonValueKind.Number || !root[0].TryGetInt32(out var version) || version is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10) || root[1].ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Desteklenmeyen veya geçersiz proje biçimi.");
                ValidateSerializedIdentityAndPoints(root[1]);
                if (!root[1].TryGetProperty("GradingRules", out var rules) || rules.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Sürümlü projede serileme listesi eksik veya geçersiz; dosya açılmadı.");
                if (version == 1 && HasSerializedMetadata(root[1])) throw new InvalidDataException("Parça bilgileri için sürüm 2 gereklidir.");
                ValidateSerializedGrainlines(root[1], version);
                ValidateSerializedNotches(root[1],version);
                var hasPhysical=HasPhysicalField(root[1]);
                var hasPhoto=root[1].TryGetProperty("PhotoTraceHistory",out _);
                if(version==8 && hasPhysical==hasPhoto)throw new InvalidDataException("Sürüm 8 biçimi belirsiz veya karışık.");
                if(version==9 && hasPhysical)throw new InvalidDataException("Sürüm 9 fiziksel kontur içeremez.");
                if(version==10 && !hasPhysical && !hasPhoto)throw new InvalidDataException("Sürüm 10 ek bilgi içermiyor.");
                ValidateSerializedPhysicalFeatures(root[1],version==9 || (version==8 && hasPhoto) ? 0 : version);
                ValidateSerializedPhotoTraceHistory(root[1],version==8 && hasPhysical ? 0 : version,cancellationToken);
                if (version < 3 && root[1].TryGetProperty("NestingSnapshot", out _))
                    throw new InvalidDataException("Pastal geçmişi için sürüm 3 gereklidir.");
                if (version == 3 || (version >= 4 && root[1].TryGetProperty("NestingSnapshot", out _))) ValidateSerializedNestingSnapshot(root[1]);
                var hasTrace=root[1].TryGetProperty("NestingSnapshot",out var nesting) && nesting.ValueKind==JsonValueKind.Object && nesting.TryGetProperty("RotationTrace",out _);
                if((version==7 && !hasTrace) || (version<7 && hasTrace))throw new InvalidDataException("Döndürme izi için sürüm 7 gereklidir.");
                if(hasTrace)
                {
                    var raw=nesting.GetProperty("RotationTrace");
                    if(raw.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Döndürme izi nesnesi eksik veya geçersiz.");
                    foreach(var piece in root[1].GetProperty("Pieces").EnumerateArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if(piece.ValueKind!=JsonValueKind.Object || !piece.TryGetProperty("Points",out var points) || points.ValueKind!=JsonValueKind.Array ||
                           points.GetArrayLength()>MaximumPointsPerPiece || (rotatedVisiblePoints+=points.GetArrayLength())>MaximumTotalPoints)
                            throw new InvalidDataException("Görünür yerleşim nokta sınırı geçersiz.");
                    }
                    // Reject missing/unknown/duplicate trace fields before model
                    // constructors or JSON defaults can synthesize any values.
                    RotatedNestingTraceCodec.ValidateRawStructure(Encoding.UTF8.GetBytes(raw.GetRawText()),rotatedVisiblePoints,cancellationToken);
                }
                ValidateSerializedImportProvenance(root[1], version);
                project = root[1].Deserialize<ProjectDocument>(Options) ?? throw new InvalidDataException("Proje dosyası boş.");
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                ValidateSerializedIdentityAndPoints(root);
                ValidateSerializedGrainlines(root, 0);
                ValidateSerializedNotches(root,0);
                ValidateSerializedPhysicalFeatures(root,0);
                ValidateSerializedPhotoTraceHistory(root,0,cancellationToken);
                ValidateSerializedImportProvenance(root, 0);
                if (HasSerializedMetadata(root)) throw new InvalidDataException("Parça bilgileri için sürüm 2 gereklidir.");
                if (root.TryGetProperty("NestingSnapshot", out _)) throw new InvalidDataException("Pastal geçmişi için sürüm 3 gereklidir.");
                project = root.Deserialize<ProjectDocument>(Options) ?? throw new InvalidDataException("Proje dosyası boş.");
            }
            else throw new InvalidDataException("Geçersiz proje biçimi.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Proje dosyası geçersiz veya bu sürümün tanımadığı alanlar içeriyor. Veri kaybını önlemek için dosya açılmadı.", ex); }
        Validate(project,cancellationToken);
        // Validate above rechecks the deserialized trace and historical layout via
        // RotatedProjectValidation. Raw required fields and the combined point
        // budget were already checked before model defaults could be applied.
        cancellationToken.ThrowIfCancellationRequested();
        return project;
    }
    // Reject malformed byte sequences instead of silently replacing characters in saved names.
    // Detect UTF-32 before UTF-16 because the little-endian BOM has the same prefix.
    private static string DecodeProjectText(byte[] bytes)
    {
        Encoding encoding; int offset;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }))
        { encoding = new UTF32Encoding(false, false, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
        { encoding = new UTF32Encoding(true, false, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        { encoding = new UTF8Encoding(false, true); offset = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
        else { encoding = new UTF8Encoding(false, true); offset = 0; }
        try { return encoding.GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException ex)
        { throw new InvalidDataException("Proje dosyasının metin kodlaması bozuk; dosya açılmadı.", ex); }
    }
    private static bool HasSerializedMetadata(JsonElement project) => project.TryGetProperty("Pieces", out var pieces) && pieces.ValueKind == JsonValueKind.Array && pieces.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.Object && p.TryGetProperty("Metadata", out _));
    private static void ValidateSerializedPhotoTraceHistory(JsonElement project,int version,CancellationToken cancellationToken)
    {
        var present=project.TryGetProperty("PhotoTraceHistory",out var history);
        if(version<8)
        {
            if(present)throw new InvalidDataException("Fotoğraf taslağı geçmişi için sürüm 8 gereklidir.");
            return;
        }
        if(!present && version==10)return;
        if(!present || history.ValueKind!=JsonValueKind.Array || history.GetArrayLength() is <1 or >512)
            throw new InvalidDataException("Fotoğraf taslağı geçmişi eksik veya geçersiz.");
        static void Point(JsonElement p)
        {
            if(p.ValueKind!=JsonValueKind.Object || !p.TryGetProperty("X",out var x) || x.ValueKind!=JsonValueKind.Number || !x.TryGetDouble(out var xv) || !double.IsFinite(xv) ||
               !p.TryGetProperty("Y",out var y) || y.ValueKind!=JsonValueKind.Number || !y.TryGetDouble(out var yv) || !double.IsFinite(yv) ||
               (p.TryGetProperty("IsFinite",out var finite) && finite.ValueKind!=JsonValueKind.True) ||
               p.EnumerateObject().Any(v=>v.Name is not ("X" or "Y" or "IsFinite")))
                throw new InvalidDataException("Fotoğraf taslağı noktası eksik veya geçersiz.");
        }
        string[] required=["AlgorithmVersion","PieceId","SourceSha256","SourceByteLength","SourceOrientation","SourceWidth","SourceHeight","PreviewWidth","PreviewHeight","ReferenceStart","ReferenceEnd","ReferenceLengthMm","TracePixels","GeneratedPoints"];
        long total=0;var hasPlane=false;
        foreach(var record in history.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(record.ValueKind!=JsonValueKind.Object || record.EnumerateObject().Any(p=>!required.Contains(p.Name,StringComparer.Ordinal) && p.Name!="PlaneCalibration") ||
               required.Any(name=>!record.TryGetProperty(name,out _)))throw new InvalidDataException("Fotoğraf taslağı kaydı eksik veya bilinmeyen alan içeriyor.");
            foreach(var name in new[] { "AlgorithmVersion","SourceOrientation","SourceWidth","SourceHeight","PreviewWidth","PreviewHeight" })
                if(record.GetProperty(name).ValueKind!=JsonValueKind.Number || !record.GetProperty(name).TryGetInt32(out _))throw new InvalidDataException("Fotoğraf taslağı tamsayı alanı geçersiz.");
            var algorithm=record.GetProperty("AlgorithmVersion").GetInt32();var planePresent=record.TryGetProperty("PlaneCalibration",out var plane);
            if(algorithm==1 && planePresent || algorithm==2 && (!planePresent || version<9) || algorithm is not (1 or 2))
                throw new InvalidDataException("Fotoğraf taslağı algoritması ve düzlem kaydı uyuşmuyor.");
            if(algorithm==2)
            {
                hasPlane=true;
                if(plane.ValueKind!=JsonValueKind.Object || plane.EnumerateObject().Any(p=>p.Name is not ("Corners" or "WidthMm" or "HeightMm")) ||
                   !plane.TryGetProperty("Corners",out var corners) || corners.ValueKind!=JsonValueKind.Array || corners.GetArrayLength()!=4 ||
                   !plane.TryGetProperty("WidthMm",out var width) || width.ValueKind!=JsonValueKind.Number || !width.TryGetDouble(out var w) || !double.IsFinite(w) ||
                   !plane.TryGetProperty("HeightMm",out var height) || height.ValueKind!=JsonValueKind.Number || !height.TryGetDouble(out var h) || !double.IsFinite(h) || (total+=4)>200_000)
                    throw new InvalidDataException("Fotoğraf düzlem kaydı eksik veya geçersiz.");
                foreach(var corner in corners.EnumerateArray())Point(corner);
            }
            if(record.GetProperty("SourceByteLength").ValueKind!=JsonValueKind.Number || !record.GetProperty("SourceByteLength").TryGetInt64(out _) ||
               record.GetProperty("SourceSha256").ValueKind!=JsonValueKind.String || record.GetProperty("PieceId").ValueKind!=JsonValueKind.String ||
               !record.GetProperty("PieceId").TryGetGuid(out _) || record.GetProperty("ReferenceLengthMm").ValueKind!=JsonValueKind.Number ||
               !record.GetProperty("ReferenceLengthMm").TryGetDouble(out var length) || !double.IsFinite(length))
                throw new InvalidDataException("Fotoğraf taslağı bilgisi geçersiz.");
            Point(record.GetProperty("ReferenceStart"));Point(record.GetProperty("ReferenceEnd"));
            foreach(var name in new[] { "TracePixels","GeneratedPoints" })
            {
                var list=record.GetProperty(name);
                if(list.ValueKind!=JsonValueKind.Array || list.GetArrayLength() is <2 or >512 || (total+=list.GetArrayLength())>200_000)
                    throw new InvalidDataException("Fotoğraf taslağı nokta sınırı aşıldı.");
                var i=0;foreach(var point in list.EnumerateArray()) { if((i++&255)==0)cancellationToken.ThrowIfCancellationRequested();Point(point); }
            }
        }
        if(version==9 && !hasPlane)throw new InvalidDataException("Sürüm 9 için fotoğraf düzlem kaydı gereklidir.");
    }
    // A v5 record is complete or the document cannot be opened. Constructor
    // defaults must never turn missing historical information into facts.
    private static void ValidateSerializedImportProvenance(JsonElement project, int version)
    {
        var present = project.TryGetProperty("ImportProvenance", out var provenance);
        if (version < 5)
        {
            if (present) throw new InvalidDataException("DXF kaynak geçmişi için sürüm 5 gereklidir; dosya açılmadı.");
            return;
        }
        if(version>=6 && !present)return;
        if (!present || provenance.ValueKind != JsonValueKind.Object ||
            !provenance.TryGetProperty("SourceFileName", out var name) || name.ValueKind != JsonValueKind.String ||
            !provenance.TryGetProperty("SourceSha256", out var sha) || sha.ValueKind != JsonValueKind.String ||
            !provenance.TryGetProperty("SourceByteLength", out var length) || length.ValueKind != JsonValueKind.Number || !length.TryGetInt64(out _) ||
            !provenance.TryGetProperty("MillimetersPerUnit", out var scale) || scale.ValueKind != JsonValueKind.Number || !scale.TryGetDouble(out var s) || !double.IsFinite(s) ||
            !provenance.TryGetProperty("Warnings", out var warnings) || warnings.ValueKind != JsonValueKind.Array ||
            !provenance.TryGetProperty("PieceIds", out var ids) || ids.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("DXF kaynak geçmişi eksik veya geçersiz; dosya açılmadı.");
        foreach (var warning in warnings.EnumerateArray())
            if (warning.ValueKind != JsonValueKind.String) throw new InvalidDataException("DXF kaynak uyarısı geçersiz.");
        foreach (var id in ids.EnumerateArray())
            if (id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out _)) throw new InvalidDataException("DXF kaynak parça kimliği geçersiz.");
    }
    private static bool HasPhysicalField(JsonElement project) => project.TryGetProperty("Pieces",out var pieces) && pieces.ValueKind==JsonValueKind.Array &&
        pieces.EnumerateArray().Any(p=>p.ValueKind==JsonValueKind.Object && p.TryGetProperty("PhysicalFeatures",out _));
    private static void ValidateSerializedPhysicalFeatures(JsonElement project,int version)
    {
        if(!project.TryGetProperty("Pieces",out var pieces) || pieces.ValueKind!=JsonValueKind.Array)return;
        var found=false;
        foreach(var piece in pieces.EnumerateArray())
        {
            if(piece.ValueKind!=JsonValueKind.Object || !piece.TryGetProperty("PhysicalFeatures",out var features))continue;
            if(version<8 || features.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Fiziksel kontur bilgisi için sürüm 8 gereklidir.");
            found=true;
            if(!features.TryGetProperty("SourcePoints",out var points) || points.ValueKind!=JsonValueKind.Array || points.GetArrayLength()>MaximumPointsPerPiece)
                throw new InvalidDataException("Fiziksel kontur noktaları eksik.");
            foreach(var point in points.EnumerateArray())
                if(point.ValueKind!=JsonValueKind.Object || !point.TryGetProperty("X",out var x) || x.ValueKind!=JsonValueKind.Number || !point.TryGetProperty("Y",out var y) || y.ValueKind!=JsonValueKind.Number || (point.TryGetProperty("IsFinite",out var finite) && finite.ValueKind!=JsonValueKind.True))
                    throw new InvalidDataException("Fiziksel kontur koordinatları eksik.");
        }
        if(version==8 && !found)throw new InvalidDataException("Sürüm 8 fiziksel kontur bilgisi içermiyor.");
    }
    private static void ValidateSerializedNotches(JsonElement project,int version)
    {
        if(!project.TryGetProperty("Pieces",out var pieces) || pieces.ValueKind!=JsonValueKind.Array)return;
        var found=false;
        foreach(var piece in pieces.EnumerateArray())
        {
            if(piece.ValueKind!=JsonValueKind.Object || !piece.TryGetProperty("Notches",out var marks))continue;
            if(version<6)throw new InvalidDataException("Çentik işaretleri için sürüm 6 gereklidir.");
            found=true;
            if(marks.ValueKind!=JsonValueKind.Array || marks.GetArrayLength() is <1 or >MaximumPointsPerPiece)
                throw new InvalidDataException("Çentik listesi eksik veya geçersiz.");
            foreach(var mark in marks.EnumerateArray())
            {
                if(mark.ValueKind!=JsonValueKind.Object || !mark.TryGetProperty("VertexIndex",out var index) || index.ValueKind!=JsonValueKind.Number || !index.TryGetInt32(out _) ||
                   !mark.TryGetProperty("Position",out var point) || point.ValueKind!=JsonValueKind.Object ||
                   !point.TryGetProperty("X",out var x) || x.ValueKind!=JsonValueKind.Number || !x.TryGetDouble(out var px) || !double.IsFinite(px) ||
                   !point.TryGetProperty("Y",out var y) || y.ValueKind!=JsonValueKind.Number || !y.TryGetDouble(out var py) || !double.IsFinite(py) ||
                   (point.TryGetProperty("IsFinite",out var finite) && finite.ValueKind!=JsonValueKind.True))
                    throw new InvalidDataException("Çentik konumu veya indeksi eksik ya da geçersiz.");
            }
        }
        if(version==6 && !found)throw new InvalidDataException("Sürüm 6 çentik işareti içermiyor.");
    }
    // The field is never valid in older formats, even as null. In v4 absence is
    // distinct from an explicitly malformed/null value; each existing object and
    // coordinate must be complete before the serializer can apply defaults.
    private static void ValidateSerializedGrainlines(JsonElement project, int version)
    {
        if (!project.TryGetProperty("Pieces", out var pieces) || pieces.ValueKind != JsonValueKind.Array) return;
        foreach (var piece in pieces.EnumerateArray())
        {
            if (piece.ValueKind != JsonValueKind.Object || !piece.TryGetProperty("Grainline", out var line)) continue;
            if (version < 4) throw new InvalidDataException("İplik yönü için sürüm 4 gereklidir; dosya açılmadı.");
            if (line.ValueKind != JsonValueKind.Object || !line.TryGetProperty("NeedsReview", out var review) ||
                review.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("İplik yönü veya inceleme durumu eksik ya da geçersiz.");
            foreach (var name in new[] { "Start", "End" })
            {
                if (!line.TryGetProperty(name, out var point) || point.ValueKind != JsonValueKind.Object ||
                    !point.TryGetProperty("X", out var x) || x.ValueKind != JsonValueKind.Number || !x.TryGetDouble(out var px) || !double.IsFinite(px) ||
                    !point.TryGetProperty("Y", out var y) || y.ValueKind != JsonValueKind.Number || !y.TryGetDouble(out var py) || !double.IsFinite(py) ||
                    (point.TryGetProperty("IsFinite", out var finite) && finite.ValueKind != JsonValueKind.True))
                    throw new InvalidDataException("İplik yönü koordinatı eksik veya geçersiz.");
            }
        }
    }
    // Constructors have defaults, so verify every saved v3 field before binding.
    // Missing values must never silently turn into plausible zeroes or empty GUIDs.
    private static void ValidateSerializedNestingSnapshot(JsonElement project)
    {
        if (!project.TryGetProperty("NestingSnapshot", out var snapshot) || snapshot.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Sürüm 3 pastal geçmişi eksik veya geçersiz.");
        foreach (var name in new[] { "FabricWidth", "UsedLength", "Gap", "EdgeMargin" })
            if (!snapshot.TryGetProperty(name, out var number) || number.ValueKind != JsonValueKind.Number)
                throw new InvalidDataException("Pastal ölçüsü eksik veya geçersiz.");
        foreach (var name in new[] { "Algorithm", "LayoutFingerprint" })
            if (!snapshot.TryGetProperty(name, out var text) || text.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Pastal geçmişi bilgisi eksik veya geçersiz.");
        if (!snapshot.TryGetProperty("Placements", out var placements) || placements.ValueKind != JsonValueKind.Array || placements.GetArrayLength() is < 1 or > MaximumPieces)
            throw new InvalidDataException("Pastal eşlemesi eksik veya sınır dışında.");
        foreach (var placement in placements.EnumerateArray())
        {
            if (placement.ValueKind != JsonValueKind.Object ||
                !placement.TryGetProperty("SourcePieceId", out var source) || source.ValueKind != JsonValueKind.String ||
                !placement.TryGetProperty("PlacedPieceId", out var placed) || placed.ValueKind != JsonValueKind.String ||
                !placement.TryGetProperty("CopyNumber", out var copy) || copy.ValueKind != JsonValueKind.Number || !copy.TryGetInt32(out _))
                throw new InvalidDataException("Pastal eşlemesi eksik veya geçersiz.");
        }
    }
    // Legacy files include piece IDs and may include the computed IsFinite field. Never
    // silently synthesize an ID or ignore an inconsistent computed field on load.
    private static void ValidateSerializedIdentityAndPoints(JsonElement project)
    {
        // Legacy 0.3 files include Name and Pieces, but predate GradingRules.
        // Do not turn missing saved fields into model constructor defaults.
        if (!project.TryGetProperty("Name", out var name) || name.ValueKind != JsonValueKind.String ||
            !project.TryGetProperty("Pieces", out var pieceList) || pieceList.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Proje adı veya parça listesi eksik ya da geçersiz; dosya açılmadı.");
        static void Points(JsonElement owner, string property)
        {
            if (!owner.TryGetProperty(property, out var points) || points.ValueKind != JsonValueKind.Array) return;
            foreach (var point in points.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Object || !point.TryGetProperty("X", out var x) || x.ValueKind != JsonValueKind.Number || !point.TryGetProperty("Y", out var y) || y.ValueKind != JsonValueKind.Number)
                    throw new InvalidDataException("Nokta koordinatı eksik veya geçersiz; dosya açılmadı.");
                if (point.TryGetProperty("IsFinite", out var finite) && finite.ValueKind != JsonValueKind.True)
                    throw new InvalidDataException("Nokta geçerlilik bilgisi tutarsız; dosya açılmadı.");
            }
        }
        if (project.TryGetProperty("Pieces", out var pieces) && pieces.ValueKind == JsonValueKind.Array)
            foreach (var piece in pieces.EnumerateArray())
            {
                if (piece.ValueKind != JsonValueKind.Object) continue;
                if (!piece.TryGetProperty("Id", out _))
                    throw new InvalidDataException("Parça kimliği eksik; veri kaybını önlemek için dosya açılmadı.");
                if (!piece.TryGetProperty("Name", out var pieceName) || pieceName.ValueKind != JsonValueKind.String ||
                    !piece.TryGetProperty("Points", out var piecePoints) || piecePoints.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Parça adı veya nokta listesi eksik ya da geçersiz; dosya açılmadı.");
                if (!piece.TryGetProperty("Closed", out var closed) || closed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("Kontur kapanış bilgisi eksik veya geçersiz; dosya açılmadı.");
                Points(piece, "Points");
            }
        if (project.TryGetProperty("GradingRules", out var rules) && rules.ValueKind == JsonValueKind.Array)
            foreach (var rule in rules.EnumerateArray())
                if (rule.ValueKind == JsonValueKind.Object)
                {
                    if (!rule.TryGetProperty("SourceClosed", out var closed) || closed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("Serileme kaynağının kapanış bilgisi eksik veya geçersiz; dosya açılmadı.");
                    Points(rule, "SourcePoints"); Points(rule, "Deltas");
                }
    }
    // Duplicate keys can hide data when a JSON reader selects only the last occurrence.
    internal static void RejectDuplicateProperties(JsonElement element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("Proje dosyasında yinelenen alan var; veri kaybını önlemek için dosya açılmadı.");
                RejectDuplicateProperties(property.Value, cancellationToken);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item, cancellationToken);
        }
        else if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number) && number == 0)
        {
            // Reject a nonzero decimal that would silently deserialize to zero.
            // Digits in the exponent do not make a zero mantissa nonzero.
            foreach (var c in element.GetRawText())
            {
                if (c is 'e' or 'E') break;
                if (c is >= '1' and <= '9')
                    throw new InvalidDataException("Proje sayısı sıfıra yuvarlanacak kadar küçük; veri kaybını önlemek için dosya açılmadı.");
            }
        }
    }
    public static ProjectFileVersion CaptureVersion(string path) => CaptureVersion(path, CancellationToken.None);
    public static ProjectFileVersion CaptureVersion(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(path);
        try
        {
            using var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new(full, CaptureVersionHash(input, cancellationToken));
        }
        catch (FileNotFoundException) { cancellationToken.ThrowIfCancellationRequested(); return new(full, null); }
        catch (DirectoryNotFoundException) { cancellationToken.ThrowIfCancellationRequested(); return new(full, null); }
    }
    // Hash the same opened stream incrementally, without allocating the entire
    // project on each of the two pre-commit version checks.
    internal static string CaptureVersionHash(Stream input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Length > MaximumProjectBytes) throw new InvalidDataException("Proje dosyası 64 MB sınırını aşıyor.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += count;
            if (total > MaximumProjectBytes) throw new InvalidDataException("Proje dosyası okunurken 64 MB sınırını aştı.");
            hash.AppendData(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    /// <summary>
    /// Optimistic save coordinated with this library's writers across processes. The token is path-bound.
    /// Noncooperating applications and alternate hard-link/short-name aliases are outside this mutex contract.
    /// </summary>
    public static ProjectFileVersion SaveIfUnchanged(ProjectDocument project, string path, ProjectFileVersion expectedVersion)
        => SaveIfUnchanged(project,path,expectedVersion,CancellationToken.None,null);

    public static ProjectFileVersion SaveIfUnchanged(ProjectDocument project, string path, ProjectFileVersion expectedVersion, CancellationToken cancellationToken)
        => SaveIfUnchanged(project,path,expectedVersion,cancellationToken,null);

    // Per-call hook is for deterministic competing-writer verification only.
    internal static ProjectFileVersion SaveIfUnchanged(ProjectDocument project, string path, ProjectFileVersion expectedVersion, Action? beforeCommitForVerification)
        => SaveIfUnchanged(project,path,expectedVersion,CancellationToken.None,beforeCommitForVerification);

    internal static ProjectFileVersion SaveIfUnchanged(ProjectDocument project, string path, ProjectFileVersion expectedVersion, CancellationToken cancellationToken, Action? beforeCommitForVerification)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(expectedVersion);
        EnsureWritableDestination(path);
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(full, expectedVersion.FullPath, comparison))
            throw new ArgumentException("Kayıt sürümü farklı bir dosyaya ait.", nameof(expectedVersion));
        Validate(project,cancellationToken);
        var text = SerializeProject(project,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = AcquireWriteLease(full,cancellationToken);
        EnsureWritableDestination(full);
        var current = CaptureVersion(full,cancellationToken);
        if (!string.Equals(current.Sha256, expectedVersion.Sha256, StringComparison.Ordinal))
            throw new ProjectConflictException();
        void CheckImmediatelyBeforeCommit()
        {
            beforeCommitForVerification?.Invoke();
            // This second snapshot narrows the window for a noncooperating writer,
            // but does not lock the destination through the path-based rename.
            // A writer can still modify or replace it after this check.
            var latest = CaptureVersion(full,cancellationToken);
            if (!string.Equals(latest.Sha256, expectedVersion.Sha256, StringComparison.Ordinal))
                throw new ProjectConflictException();
        }
        AtomicWriteCoreWithMode(full, text, cancellationToken, beforeFinalCancellationCheck:CheckImmediatelyBeforeCommit, failIfExists:expectedVersion.Sha256 is null);
        return new(full, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }
    // Array shape makes old ProjectDocument readers reject rather than drop rules.
    internal static string SerializeProject(ProjectDocument project, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requires10=project.Pieces.Any(p=>p.PhysicalFeatures is not null) || project.PhotoTraceHistory is not null;
        if(requires10 && !Version10WriteAvailable)
            throw new InvalidOperationException("Proje biçimi sürüm 10 için sürüm tahsisi doğrulanmadı; dosya yazılmadı.");
        var text = requires10
            ? JsonSerializer.Serialize<object[]>([10, project], Options)
            : project.NestingSnapshot?.RotationTrace is not null
            ? JsonSerializer.Serialize<object[]>([7, project], Options)
            : project.Pieces.Any(p=>p.Notches is not null)
            ? JsonSerializer.Serialize<object[]>([6, project], Options)
            : project.ImportProvenance is not null
            ? JsonSerializer.Serialize<object[]>([5, project], Options)
            : project.Pieces.Any(p => p.Grainline is not null)
            ? JsonSerializer.Serialize<object[]>([4, project], Options)
            : project.NestingSnapshot is not null
            ? JsonSerializer.Serialize<object[]>([3, project], Options)
            : project.Pieces.Any(p => p.Metadata is not null)
            ? JsonSerializer.Serialize<object[]>([2, project], Options)
            : project.GradingRules.Count == 0 ? JsonSerializer.Serialize(project, Options)
            : JsonSerializer.Serialize<object[]>([1, project], Options);
        // Never replace a usable project with bytes this version cannot reopen.
        if (Encoding.UTF8.GetByteCount(text) > MaximumProjectBytes)
            throw new InvalidDataException("Proje dosyası 64 MB sınırını aşıyor; mevcut dosya korunuyor.");
        if(project.NestingSnapshot?.RotationTrace is not null || project.PhotoTraceHistory is not null || project.Pieces.Any(p=>p.PhysicalFeatures is not null))
        {
            var reread=DeserializeProject(Encoding.UTF8.GetBytes(text),cancellationToken);
            if(!DocumentSession.ContentEquals(project,reread))
                throw new InvalidDataException("Döndürme izi dosyada kayıpsız yeniden okunamadı; mevcut dosya korunuyor.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }
    private static byte[]? ReadSnapshot(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > MaximumProjectBytes) throw new InvalidDataException("Proje dosyası 64 MB sınırını aşıyor.");
            using var memory = new MemoryStream((int)input.Length);
            var buffer = new byte[81920];
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The file may grow after the initial length check, including on
                // platforms where sharing modes cannot exclude every writer.
                if (memory.Length + count > MaximumProjectBytes)
                    throw new InvalidDataException("Proje dosyası okunurken 64 MB sınırını aştı.");
                memory.Write(buffer, 0, count);
            }
            // The common case is a stable file whose initial length exactly
            // matches the completed snapshot. Reuse that exact-sized buffer;
            // otherwise copy only the bytes actually read (e.g. if it changed).
            if (memory.TryGetBuffer(out var snapshot) && snapshot.Offset == 0 &&
                snapshot.Array is { } exact && snapshot.Count == exact.Length)
                return exact;
            return memory.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    // Persistence accepts draft contours; manufacturability must be checked by the production operation.
    public static void Validate(ProjectDocument project) => ValidateCore(project, allowInvalidPieceNameUtf16: false);
    public static void Validate(ProjectDocument project,CancellationToken token) => ValidateCore(project,allowInvalidPieceNameUtf16:false,token);

    // DXF layer planning explicitly reports and replaces unsupported name characters;
    // the permanent project format must continue rejecting lossy UTF-16 strings.
    internal static void ValidateForDxfExport(ProjectDocument project) => ValidateForDxfExport(project,CancellationToken.None);
    internal static void ValidateForDxfExport(ProjectDocument project,CancellationToken token)
    {
        ValidateCore(project,allowInvalidPieceNameUtf16:true,token);
        if(project.Pieces.Any(p=>p.PhysicalFeatures is not null))
            throw new InvalidDataException("Fiziksel kontur ve çentik kimlikleri DXF dışa aktarımında korunamıyor; aktarım yapılmadı.");
        if(project.PhotoTraceHistory is not null)
            throw new InvalidDataException("Fotoğraf geçmişi DXF dışa aktarımında korunamıyor; aktarım yapılmadı.");
    }

    private static void ValidateCore(ProjectDocument project, bool allowInvalidPieceNameUtf16,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(project);
        project.ImportProvenance?.Validate();
        if(project.PhotoTraceHistory is { } history)
        {
            if(history.Count is <1 or >512 || history.Any(x=>x is null) || history.Select(x=>x.PieceId).Distinct().Count()!=history.Count)
                throw new InvalidDataException("Fotoğraf taslağı geçmişi eksik veya yinelenen kimlik içeriyor.");
            long photoPoints=0;
            foreach(var photo in history)
            {
                token.ThrowIfCancellationRequested();photo.Validate();
                photoPoints+=(long)photo.TracePixels.Count+photo.GeneratedPoints.Count+(photo.PlaneCalibration is null ? 0 : 4);
                if(photoPoints>200_000)throw new InvalidDataException("Fotoğraf taslağı geçmişinin nokta sınırı aşıldı.");
            }
        }
        if (project.Name is null || project.Name.Length > 1024 || !IsWellFormedUtf16(project.Name)) throw new InvalidDataException("Geçersiz proje adı veya eksik Unicode karakteri.");
        if (project.Pieces is null || project.Pieces.Count > MaximumPieces) throw new InvalidDataException("Geçersiz parça listesi.");
        var ids = new HashSet<Guid>(); var physicalIds=new HashSet<Guid>(); long total = 0; long totalNotches=0;
        foreach (var piece in project.Pieces)
        {
            token.ThrowIfCancellationRequested();
            if (piece is null || piece.Points is null || piece.Points.Count > MaximumPointsPerPiece || piece.Points.Count < (piece.Closed ? 3 : 2))
                throw new InvalidDataException("Geçersiz kontur veya nokta sınırı aşıldı.");
            if (piece.Name is null || piece.Name.Length > 1024 || (!allowInvalidPieceNameUtf16 && !IsWellFormedUtf16(piece.Name))) throw new InvalidDataException("Geçersiz parça adı veya eksik Unicode karakteri.");
            if (piece.Metadata is { } metadata)
            {
                static bool ValidText(string? value) => value is null || (value.Length is > 0 and <= 128 && value == value.Trim() && !value.Any(char.IsControl) && IsWellFormedUtf16(value));
                if (metadata.Role is not (null or "Kesim" or "Yardımcı") || !ValidText(metadata.Size) || !ValidText(metadata.Material) || metadata.Quantity is < 1 or > 100000 || (metadata.Role is null && metadata.Size is null && metadata.Material is null && metadata.Quantity is null))
                    throw new InvalidDataException("Geçersiz parça üretim bilgileri.");
            }
            if (piece.Grainline is { } grainline)
            {
                try { Geometry.Validate(grainline.Start); Geometry.Validate(grainline.End); }
                catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("İplik yönü koordinatları CAD çalışma sınırları içinde olmalıdır.", ex); }
                if (grainline.Start == grainline.End) throw new InvalidDataException("İplik yönünün iki ucu farklı olmalıdır.");
            }
            if(piece.Notches is { } marks)
            {
                if(marks.Count<1 || marks.Count>MaximumPointsPerPiece || (totalNotches+=marks.Count)>MaximumTotalPoints)
                    throw new InvalidDataException("Çentik sayısı geçersiz veya sınırı aşıyor.");
                var indices=new HashSet<int>();
                foreach(var mark in marks)
                {
                    if(mark is null || mark.VertexIndex<0 || mark.VertexIndex>=piece.Points.Count ||
                       (piece.Closed && piece.Points.Count>1 && piece.Points[0]==piece.Points[^1] && mark.VertexIndex==piece.Points.Count-1) ||
                       !indices.Add(mark.VertexIndex) || mark.Position!=piece.Points[mark.VertexIndex])
                        throw new InvalidDataException("Çentik indeksi veya kaynak kontur konumu geçersiz.");
                    try { Geometry.Validate(mark.Position); }
                    catch(ArgumentOutOfRangeException ex) { throw new InvalidDataException("Çentik koordinatı geçersiz.",ex); }
                }
            }
            piece.PhysicalFeatures?.Validate(piece);
            if(piece.PhysicalFeatures is { } physical)
                foreach(var id in physical.EdgeIds.Concat(physical.Notches.Select(n=>n.Id)))
                    if(!physicalIds.Add(id))throw new InvalidDataException("Fiziksel kenar ve çentik kimlikleri projede benzersiz olmalıdır.");
            if (piece.Id == Guid.Empty || !ids.Add(piece.Id)) throw new InvalidDataException("Parça kimlikleri benzersiz ve dolu olmalıdır.");
            total += piece.Points.Count;
            if (total > MaximumTotalPoints) throw new InvalidDataException("Proje toplam nokta sınırını aşıyor.");
            try { foreach (var point in piece.Points) Geometry.Validate(point); }
            catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("Koordinatlar CAD çalışma sınırları içinde olmalıdır.", ex); }
        }
        if (project.GradingRules is null || project.GradingRules.Count > MaximumPieces) throw new InvalidDataException("Geçersiz serileme kuralları.");
        var ruleIds = new HashSet<Guid>(); long rulePoints = 0;
        foreach (var rule in project.GradingRules)
        {
            token.ThrowIfCancellationRequested();
            if (rule is null || !ids.Contains(rule.SourcePieceId) || !ruleIds.Add(rule.SourcePieceId) || rule.SourcePoints is null || rule.Deltas is null || rule.SourcePoints.Count != rule.Deltas.Count || rule.SourcePoints.Count < (rule.SourceClosed ? 3 : 2) || rule.SourcePoints.Count > MaximumPointsPerPiece)
                throw new InvalidDataException("Geçersiz serileme kaynağı veya nokta listesi.");
            rulePoints += rule.SourcePoints.Count;
            if (rulePoints > MaximumTotalPoints) throw new InvalidDataException("Serileme noktası sınırı aşıldı.");
            try { foreach (var point in rule.SourcePoints.Concat(rule.Deltas)) Geometry.Validate(point); }
            catch (ArgumentOutOfRangeException ex) { throw new InvalidDataException("Serileme değerleri çalışma sınırları içinde olmalıdır.", ex); }
        }
        if (project.NestingSnapshot is { } snapshot)
        {
            if(snapshot.RotationTrace is not null)RotatedProjectValidation.Verify(project,token);
            // This is historical planning metadata, not a certificate that the
            // current geometry or a physical machine is production ready.
            const double limit = 100_000_000;
            if (!double.IsFinite(snapshot.FabricWidth) || snapshot.FabricWidth <= 0 || snapshot.FabricWidth > limit ||
                !double.IsFinite(snapshot.UsedLength) || snapshot.UsedLength <= 0 || snapshot.UsedLength > limit ||
                !double.IsFinite(snapshot.Gap) || snapshot.Gap < 0 || snapshot.Gap > limit ||
                !double.IsFinite(snapshot.EdgeMargin) || snapshot.EdgeMargin < 0 ||
                snapshot.EdgeMargin * 2 >= snapshot.FabricWidth || snapshot.EdgeMargin * 2 > snapshot.UsedLength)
                throw new InvalidDataException("Pastal geçmişindeki kumaş ölçüleri geçersiz.");
            if (snapshot.Algorithm != NestingSnapshot.ShelfAlgorithm || snapshot.LayoutFingerprint is not { Length: 64 } fingerprint ||
                fingerprint.Any(c => !((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'))))
                throw new InvalidDataException("Pastal algoritması veya geometri özeti geçersiz.");
            if (snapshot.Placements is null || snapshot.Placements.Count is < 1 or > MaximumPieces)
                throw new InvalidDataException("Pastal eşleme sayısı geçersiz.");
            var placedIds = new HashSet<Guid>();
            var sourceCopies = new HashSet<(Guid, int)>();
            foreach (var placement in snapshot.Placements)
                if (placement is null || placement.SourcePieceId == Guid.Empty || placement.PlacedPieceId == Guid.Empty ||
                    placement.CopyNumber is < 1 or > 100_000 || !placedIds.Add(placement.PlacedPieceId) ||
                    !sourceCopies.Add((placement.SourcePieceId, placement.CopyNumber)))
                    throw new InvalidDataException("Pastal parça eşlemesi geçersiz veya yinelenmiş.");
            if (string.Equals(snapshot.LayoutFingerprint, NestingSnapshot.ComputeLayoutFingerprint(project), StringComparison.Ordinal) &&
                !placedIds.SetEquals(ids))
                throw new InvalidDataException("Güncel pastal özeti ile yerleştirilmiş parça eşlemeleri uyuşmuyor.");
            // Do not require historical IDs in the current document: deletion or
            // edits intentionally make the saved historical snapshot stale.
        }
    }
    // JSON encoding otherwise replaces isolated UTF-16 surrogate code units, silently changing saved text.
    private static bool IsWellFormedUtf16(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false;
            }
            else if (char.IsLowSurrogate(c)) return false;
        }
        return true;
    }
    /// <summary>Reject protected production data and link-based aliases before any filesystem mutation.</summary>
    public static void EnsureWritableDestination(string path) => EnsureWritableDestinationCore(path,
        [@"C:\WearCadfe", @"C:\Hpgl", @"C:\Nester", @"C:\Nester1", @"C:\NST"]);

    // Root list is injectable only for isolated workspace tests. Production always
    // supplies the protected directories above.
    internal static void EnsureWritableDestinationCore(string path, IReadOnlyList<string> protectedRoots)
    {
        // Win32 resolves trailing dots/spaces as aliases; reject before normalization.
        if (path.Split(['\\', '/']).Any(segment => segment.Length > 0 && segment is not "." and not ".." && (segment.EndsWith(' ') || segment.EndsWith('.'))))
            throw new InvalidOperationException("Nokta veya boşlukla biten yol bileşenlerine yazılamaz.");
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal) || full.IndexOf(':', 2) >= 0)
            throw new InvalidOperationException("Ağ/aygıt yollarına veya alternatif veri akışlarına yazılamaz.");
        var candidateDevice=OperatingSystem.IsWindows() ? WindowsLongPath.RequireOrdinaryDrive(full) : null;
        var rootDevices=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        bool IsProtected(string candidate, IReadOnlyList<string> roots) => roots.Any(root =>
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (candidate.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            if (!OperatingSystem.IsWindows()) return false;
            var drive=Path.GetPathRoot(normalized)!;
            if (!rootDevices.TryGetValue(drive,out var rootDevice))
                rootDevices[drive]=rootDevice=WindowsLongPath.RequireOrdinaryDrive(normalized);
            return WindowsLongPath.IsProtectedOnSameVolume(candidate,candidateDevice!,normalized,rootDevice);
        });
        if (IsProtected(full, protectedRoots))
                throw new InvalidOperationException("WearCAD ve üretim veri klasörleri salt okunurdur.");
        try
        {
            var targetAttributes = File.GetAttributes(full);
            if ((targetAttributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Bağlantılı dosyaya yazılamaz.");
            if (OperatingSystem.IsWindows() && (targetAttributes & FileAttributes.Directory) != 0 &&
                IsProtected(WindowsLongPath.ResolveExisting(full), protectedRoots))
                throw new InvalidOperationException("WearCAD ve üretim veri klasörleri salt okunurdur.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        var parent = new DirectoryInfo(Path.GetDirectoryName(full)!);
        while (parent is not null)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(parent.FullName); }
            catch (FileNotFoundException) { parent = parent.Parent; continue; }
            catch (DirectoryNotFoundException) { parent = parent.Parent; continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Bağlantılı klasöre yazılamaz.");
            // Path.GetFullPath does not expand Windows 8.3 aliases. Resolve each
            // existing ancestor, including when the requested leaf does not exist.
            // Failure to resolve is an error, never permission to write.
            if (OperatingSystem.IsWindows() && IsProtected(WindowsLongPath.ResolveExisting(parent.FullName), protectedRoots))
                throw new InvalidOperationException("WearCAD ve üretim veri klasörleri salt okunurdur.");
            parent = parent.Parent;
        }
    }
    internal static void AtomicWrite(string path, string text) => AtomicWrite(path, text, CancellationToken.None);
    internal static void AtomicWrite(string path, string text, CancellationToken cancellationToken)
        => AtomicWrite(path, text, cancellationToken, null);
    // Scoped boundary callback is used only by an explicitly constructed verification store.
    internal static void AtomicWrite(string path, string text, CancellationToken cancellationToken, Action? beforeCommitForVerification)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritableDestination(path);
        var target = Path.GetFullPath(path);
        using var lease = AcquireWriteLease(target,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        AtomicWriteCore(target, text, cancellationToken, beforeCommitForVerification);
    }
    // Recovery cleanup needs the identity captured from the committed temporary file,
    // not an identity obtained from a mutable path after the commit.
    internal static FileIdentity.OwnedFileStamp? AtomicWriteOwned(string path,string text,
        FileIdentity.OwnedFileStamp? expectedIdentity,string? expectedHash,Action? beforeCommitForVerification = null)
    {
        EnsureWritableDestination(path);
        var target=Path.GetFullPath(path);
        using var lease=AcquireWriteLease(target);
        if(expectedIdentity is null && expectedHash is not null)
            throw new IOException("Önceki kurtarma kaydının kimliği doğrulanamıyor; kayıt korunuyor.");
        void CheckPrevious()
        {
            if(expectedIdentity is null)return; // The final move uses fail-if-exists.
            if(!OperatingSystem.IsWindows() || expectedHash is null)
                throw new IOException("Önceki kurtarma kaydının kimliği doğrulanamıyor; kayıt korunuyor.");
            try
            {
                using var input=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read);
                if(FileIdentity.CaptureOwnedFile(input.SafeFileHandle)!=expectedIdentity.Value || input.Length>RecoveryEnvelope.MaximumBytes ||
                    !string.Equals(Convert.ToHexString(SHA256.HashData(input)),expectedHash,StringComparison.Ordinal))
                    throw new IOException("Kurtarma kaydı dışarıda değişti; yeni kayıt korunuyor.");
            }
            catch(FileNotFoundException ex) { throw new IOException("Önceki kurtarma kaydı bulunamadı; kayıt uygulanmadı.",ex); }
            catch(DirectoryNotFoundException ex) { throw new IOException("Önceki kurtarma klasörü bulunamadı; kayıt uygulanmadı.",ex); }
        }
        CheckPrevious();
        // This second check narrows but cannot eliminate the external writer race:
        // the destination is not locked across the final path-based replacement.
        void BeforeCommit() { beforeCommitForVerification?.Invoke();CheckPrevious(); }
        return AtomicWriteCoreWithMode(target,text,CancellationToken.None,BeforeCommit,failIfExists:expectedIdentity is null);
    }
    /// <summary>Write only while the destination matches the path-bound content token.
    /// Cooperative writers share a mutex; external writers and aliases remain outside this contract.</summary>
    public static void AtomicWriteIfUnchanged(string path, string text, ProjectFileVersion expectedVersion)
        => AtomicWriteIfUnchanged(path, text, expectedVersion, CancellationToken.None);
    internal static void AtomicWriteIfUnchanged(string path, string text, ProjectFileVersion expectedVersion, CancellationToken cancellationToken)
        => AtomicWriteIfUnchangedCore(path, text, expectedVersion, cancellationToken, null, null);

    // Per-call boundary observers are private and supplied only by deterministic
    // tests. No shared hook can interfere with another writer or a production call.
    private static void AtomicWriteIfUnchangedCore(string path, string text, ProjectFileVersion expectedVersion,
        CancellationToken cancellationToken, Action? beforeFinalCancellationCheck, Action? afterFinalCancellationCheck)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        EnsureWritableDestination(path);
        var target = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(target, expectedVersion.FullPath, comparison))
            throw new ArgumentException("Kayıt sürümü farklı bir dosyaya ait.", nameof(expectedVersion));
        using var lease = AcquireWriteLease(target,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWritableDestination(target);
        var current = CaptureVersion(target,cancellationToken);
        if (!string.Equals(current.Sha256, expectedVersion.Sha256, StringComparison.Ordinal))
            throw new ProjectConflictException();
        cancellationToken.ThrowIfCancellationRequested();
        void CheckImmediatelyBeforeCommit()
        {
            beforeFinalCancellationCheck?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            // Recheck after preparing the temporary file, so an external change
            // during preparation is not silently replaced. This only narrows
            // the race: path-based replacement still follows the check.
            var latest = CaptureVersion(target,cancellationToken);
            if (!string.Equals(latest.Sha256, expectedVersion.Sha256, StringComparison.Ordinal))
                throw new ProjectConflictException();
        }
        AtomicWriteCoreWithMode(target, text, cancellationToken, CheckImmediatelyBeforeCommit, afterFinalCancellationCheck, failIfExists:expectedVersion.Sha256 is null);
    }
    private static FileIdentity.OwnedFileStamp? AtomicWriteCore(string target, string text, CancellationToken cancellationToken = default,
        Action? beforeFinalCancellationCheck = null, Action? afterFinalCancellationCheck = null,
        Action? afterCommitForVerification = null)
        => AtomicWriteCoreWithMode(target,text,cancellationToken,beforeFinalCancellationCheck,afterFinalCancellationCheck,afterCommitForVerification);

    private static FileIdentity.OwnedFileStamp? AtomicWriteCoreWithMode(string target, string text, CancellationToken cancellationToken = default,
        Action? beforeFinalCancellationCheck = null, Action? afterFinalCancellationCheck = null,
        Action? afterCommitForVerification = null, bool failIfExists = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Validate before creating a directory or staging file. The default
        // UTF-8 encoder would silently replace isolated UTF-16 surrogates.
        var utf8 = new UTF8Encoding(false, true);
        utf8.GetByteCount(text);
        EnsureWritableDestination(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var committed = false;
        FileIdentity.OwnedFileStamp? ownedTemp = null;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (OperatingSystem.IsWindows()) ownedTemp = FileIdentity.CaptureOwnedFile(stream.SafeFileHandle);
                using (var writer = new StreamWriter(stream, utf8, leaveOpen: true)) { writer.Write(text); writer.Flush(); }
                stream.Flush(true);
            }
            // The move is the commit point. A cancellation after this check may
            // race with it and must not be reported as a rolled-back write.
            beforeFinalCancellationCheck?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            afterFinalCancellationCheck?.Invoke();
            // Replacing one name of a hard-linked file changes the link metadata of
            // all its aliases, potentially including a protected installation.
            // Check as close to the move as possible. Non-cooperating path changes
            // can still race this check; it is not a filesystem lock.
            if (OperatingSystem.IsWindows())
            {
                if (ownedTemp is not { } stamp || !FileIdentity.IsOwnedFile(temp, stamp))
                    throw new IOException("Geçici dosya değişti; kayıt uygulanmadı.");
                FileIdentity.RejectMultipleLinksForReplacement(target);
            }
            File.Move(temp, target, !failIfExists);
            committed = true;
            afterCommitForVerification?.Invoke();
            return ownedTemp;
        }
        finally
        {
            if (!committed)
            {
                // Best effort only: never mask the original error, and never
                // unlink an unowned or no-longer-verifiable temporary path.
                try
                {
                    if (OperatingSystem.IsWindows() && ownedTemp is { } stamp && FileIdentity.IsOwnedFile(temp, stamp))
                        File.Delete(temp);
                }
                catch { /* retain uncertain temp for inspection */ }
            }
        }
    }
    internal static IDisposable AcquireWriteLease(string fullPath, CancellationToken cancellationToken = default, Action? beforeWaitForVerification = null, Action<bool>? afterAcquireForVerification = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var mutex = new Mutex(false, (OperatingSystem.IsWindows() ? @"Global\" : "") + "TekstilCAD.ProjectWrite." + hash);
        try
        {
            beforeWaitForVerification?.Invoke();
            int signaled;var abandoned = false;
            try
            {
                signaled = cancellationToken.CanBeCanceled
                    ? WaitHandle.WaitAny([mutex, cancellationToken.WaitHandle], TimeSpan.FromSeconds(10))
                    : mutex.WaitOne(TimeSpan.FromSeconds(10)) ? 0 : WaitHandle.WaitTimeout;
            }
            catch (AbandonedMutexException) { signaled = 0; abandoned = true; }
            if (signaled == 1) cancellationToken.ThrowIfCancellationRequested();
            if (signaled == WaitHandle.WaitTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException("Dosya başka bir oturumda kaydediliyor. Lütfen kısa süre sonra yeniden deneyin.");
            }
            // A cancellation can race acquisition; release on this same thread.
            try
            {
                afterAcquireForVerification?.Invoke(abandoned);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch { mutex.ReleaseMutex(); throw; }
            return new WriteLease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }
    private sealed class WriteLease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}

