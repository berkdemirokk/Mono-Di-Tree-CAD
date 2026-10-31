using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

/// <summary>Unapplied inspector text and the piece state against which it was entered.</summary>
public sealed record RecoveryPropertyDraft(Guid PieceId, string Name, string X, string Y,
    string BaseName, double BaseX, double BaseY);

/// <summary>A recovery snapshot. Draft text can be invalid or refer to a deleted piece.</summary>
public sealed record RecoveryPendingDrawing(string Tool, IReadOnlyList<Point2> Points);

public sealed record RecoverySnapshot(ProjectDocument Document, IReadOnlyList<RecoveryPropertyDraft> Drafts)
{
    public RecoveryPendingDrawing? PendingDrawing { get; init; }
}

internal static class RecoveryEnvelope
{
    internal const int MaximumBytes = 80 * 1024 * 1024;
    private const int MaximumTextLength = 16384;
    private const int MaximumTotalCharacters = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record DraftDto(Guid PieceId, string Name, string X, string Y, string BaseName, double BaseX, double BaseY);
    private sealed record EnvelopeDto(int Version, JsonElement Project, DraftDto[] Drafts);
    private sealed record PendingPointDto(double X, double Y);
    private sealed record PendingDrawingDto(string Tool, PendingPointDto[] Points);
    private sealed record EnvelopeV2Dto(int Version, JsonElement Project, DraftDto[] Drafts, PendingDrawingDto? PendingDrawing);

    internal static string Serialize(ProjectDocument document, IReadOnlyList<RecoveryPropertyDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ProjectStorage.Validate(document);
        Validate(drafts);
        var projectText = ProjectStorage.SerializeProject(document);
        if (Encoding.UTF8.GetByteCount(projectText) > 64 * 1024 * 1024)
            throw new InvalidDataException("Kurtarma projesi boyut sınırını aşıyor.");
        using var project = JsonDocument.Parse(projectText);
        var items = drafts.Select(d => new DraftDto(d.PieceId, Encode(d.Name), Encode(d.X), Encode(d.Y), Encode(d.BaseName), d.BaseX, d.BaseY)).ToArray();
        var text = JsonSerializer.Serialize(new EnvelopeDto(1, project.RootElement, items), Options);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("Kurtarma kaydı boyut sınırını aşıyor.");
        return text;
    }

    internal static string Serialize(ProjectDocument document, IReadOnlyList<RecoveryPropertyDraft> drafts, RecoveryPendingDrawing? pending)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ProjectStorage.Validate(document);
        Validate(drafts);
        ValidatePendingDrawing(pending);
        var projectText = ProjectStorage.SerializeProject(document);
        using var project = JsonDocument.Parse(projectText);
        var items = drafts.Select(d => new DraftDto(d.PieceId, Encode(d.Name), Encode(d.X), Encode(d.Y), Encode(d.BaseName), d.BaseX, d.BaseY)).ToArray();
        var drawing = pending is null ? null : new PendingDrawingDto(pending.Tool, pending.Points.Select(p => new PendingPointDto(p.X, p.Y)).ToArray());
        var text = JsonSerializer.Serialize(new EnvelopeV2Dto(2, project.RootElement, items, drawing), Options);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("Kurtarma kaydı boyut sınırını aşıyor.");
        return text;
    }

    internal static void ValidatePendingDrawing(RecoveryPendingDrawing? pending)
    {
        if (pending is null) return;
        var max = pending.Tool switch { "Çizgi" or "Dikdörtgen" => 1, "Bézier" => 3, "Yay" => 2, "Çoklu çizgi" => 10000, _ => 0 };
        if (max == 0 || pending.Points is null || pending.Points.Count < 1 || pending.Points.Count > max)
            throw new InvalidDataException("Tamamlanmamış çizim aracı veya nokta sayısı geçersiz.");
        foreach (var point in pending.Points)
            try { Geometry.Validate(point); }
            catch (ArgumentException ex) { throw new InvalidDataException("Tamamlanmamış çizim noktası geçersiz.", ex); }
    }

    internal static RecoverySnapshot Deserialize(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Kurtarma kaydı boyut sınırını aşıyor.");
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            ProjectStorage.RejectDuplicateProperties(json.RootElement);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out _) || !root.TryGetProperty("Project", out _) || !root.TryGetProperty("Drafts", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Kurtarma kaydı eksik.");
            if (list.GetArrayLength() > ProjectStorage.MaximumPieces) throw new InvalidDataException("Kurtarma taslağı sayısı sınırı aşıyor.");
            foreach (var entry in list.EnumerateArray())
                if (entry.ValueKind != JsonValueKind.Object || new[] { "PieceId", "Name", "X", "Y", "BaseName", "BaseX", "BaseY" }.Any(p => !entry.TryGetProperty(p, out _)))
                    throw new InvalidDataException("Kurtarma taslağı eksik.");
            var versionJson = root.GetProperty("Version");
            if (versionJson.ValueKind != JsonValueKind.Number || !versionJson.TryGetInt32(out var version) || version is not (1 or 2))
                throw new InvalidDataException("Kurtarma sürümü desteklenmiyor.");
            JsonElement projectJson; DraftDto[] draftDtos; RecoveryPendingDrawing? pending = null;
            if (version == 1)
            {
                var dto = root.Deserialize<EnvelopeDto>(Options) ?? throw new InvalidDataException("Kurtarma kaydı boş.");
                projectJson = dto.Project; draftDtos = dto.Drafts ?? throw new InvalidDataException("Kurtarma taslakları eksik.");
            }
            else
            {
                if (!root.TryGetProperty("PendingDrawing", out var drawingJson)) throw new InvalidDataException("Tamamlanmamış çizim alanı eksik.");
                if (drawingJson.ValueKind != JsonValueKind.Null)
                {
                    if (drawingJson.ValueKind != JsonValueKind.Object || !drawingJson.TryGetProperty("Tool", out var tool) || tool.ValueKind != JsonValueKind.String ||
                        !drawingJson.TryGetProperty("Points", out var points) || points.ValueKind != JsonValueKind.Array || points.GetArrayLength() > 10000)
                        throw new InvalidDataException("Tamamlanmamış çizim geçersiz.");
                    foreach (var point in points.EnumerateArray())
                        if (point.ValueKind != JsonValueKind.Object || !point.TryGetProperty("X", out var x) || x.ValueKind != JsonValueKind.Number || !point.TryGetProperty("Y", out var y) || y.ValueKind != JsonValueKind.Number)
                            throw new InvalidDataException("Tamamlanmamış çizim noktası eksik.");
                }
                var dto = root.Deserialize<EnvelopeV2Dto>(Options) ?? throw new InvalidDataException("Kurtarma kaydı boş.");
                projectJson = dto.Project; draftDtos = dto.Drafts ?? throw new InvalidDataException("Kurtarma taslakları eksik.");
                if (dto.PendingDrawing is { } drawing)
                    pending = new RecoveryPendingDrawing(drawing.Tool, drawing.Points.Select(p => new Point2(p.X, p.Y)).ToArray());
                ValidatePendingDrawing(pending);
            }
            var project = ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(projectJson.GetRawText()));
            var drafts = draftDtos.Select(d => new RecoveryPropertyDraft(d.PieceId, Decode(d.Name), Decode(d.X), Decode(d.Y), Decode(d.BaseName), d.BaseX, d.BaseY)).ToArray();
            Validate(drafts);
            return new RecoverySnapshot(project, drafts) { PendingDrawing = pending };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or OverflowException)
        { throw new InvalidDataException("Kurtarma kaydı geçersiz; özgün kayıt korundu.", ex); }
    }

    private static void Validate(IReadOnlyList<RecoveryPropertyDraft> drafts)
    {
        if (drafts.Count > ProjectStorage.MaximumPieces) throw new InvalidDataException("Kurtarma taslağı sayısı sınırı aşıyor.");
        var ids = new HashSet<Guid>(); long total = 0;
        foreach (var d in drafts)
        {
            if (d is null || d.PieceId == Guid.Empty || !ids.Add(d.PieceId) || !double.IsFinite(d.BaseX) || !double.IsFinite(d.BaseY))
                throw new InvalidDataException("Kurtarma taslağı kimliği veya başlangıç konumu geçersiz.");
            foreach (var value in new[] { d.Name, d.X, d.Y, d.BaseName })
            {
                if (value is null || value.Length > MaximumTextLength) throw new InvalidDataException("Kurtarma taslağı metin sınırını aşıyor.");
                total += value.Length;
            }
        }
        if (total > MaximumTotalCharacters) throw new InvalidDataException("Kurtarma taslakları toplam boyut sınırını aşıyor.");
    }

    // Preserve every UTF-16 code unit, including incomplete surrogate pairs typed in a textbox.
    private static string Encode(string text)
    {
        var bytes = new byte[text.Length * 2];
        for (var i = 0; i < text.Length; i++) { bytes[2*i] = (byte)text[i]; bytes[2*i+1] = (byte)(text[i] >> 8); }
        return Convert.ToBase64String(bytes);
    }
    private static string Decode(string encoded)
    {
        if (encoded is null || encoded.Length > ((MaximumTextLength * 2 + 2) / 3) * 4) throw new InvalidDataException("Kurtarma taslağı metin sınırını aşıyor.");
        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length % 2 != 0 || bytes.Length > MaximumTextLength * 2) throw new InvalidDataException("Kurtarma taslağı metni geçersiz.");
        var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)(bytes[2*i] | bytes[2*i+1] << 8);
        return new string(chars);
    }
}
