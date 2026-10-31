using System.Security.Cryptography;
using System.Text.Json;

namespace TekstilCAD.MachineGateway;

/// <summary>
/// Experimental immutable job-state records. This type does not integrate with
/// SimulationQueue and does not prevent other processes from modifying files.
/// Callers must treat conflicts and incomplete scans as requiring review.
/// </summary>
public sealed class ImmutableJobHistory
{
    public enum InspectionStatus { Valid, Conflict, Invalid, LimitExceeded }
    public sealed record Inspection(InspectionStatus Status, string? BaseHash, string? HeadHash, byte[]? Payload, string? Error)
    { public int RecordCount { get; init; } }
    private sealed record VersionRecord(int Version, Guid JobId, string BaseHash, string ParentHash, string Nonce, byte[] Payload);
    private readonly string directory;
    private readonly Guid jobId;
    private const int MaxPayloadBytes = 128 * 1024;
    private const int MaxRecordBytes = 256 * 1024;
    private const int MaxAllowedRecords = 10_000;
    internal Action? BeforePublishForVerification { get; set; }
    internal Action? AfterTempWriteForVerification { get; set; }
    internal Action<string, string>? BeforeHandlePublishForVerification { get; set; }

    public ImmutableJobHistory(string jobDirectory)
    {
        directory = SimulationQueue.ValidateOutputDirectory(jobDirectory);
        if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out jobId) || jobId == Guid.Empty)
            throw new ArgumentException("İş klasörü geçerli bir kimlik taşımalıdır.", nameof(jobDirectory));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'));
    private static byte[] ReadBounded(string path, int limit)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Yönlendirilmiş kayıt kullanılamaz.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > limit) throw new InvalidDataException("Kayıt boyut sınırını aşıyor.");
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Kayıt boyut sınırını aşıyor.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    /// <summary>Bounded live scan, not an atomic filesystem snapshot. No files are changed.</summary>
    public Inspection Inspect(int maxRecords = MaxAllowedRecords, CancellationToken cancellationToken = default)
    {
        if (maxRecords < 1 || maxRecords > MaxAllowedRecords) throw new ArgumentOutOfRangeException(nameof(maxRecords));
        cancellationToken.ThrowIfCancellationRequested();
        string? baseHash = null;
        try
        {
            SimulationQueue.ValidateOutputDirectory(directory);
            var basePath = Path.Combine(directory, "manifest.json");
            var baseBytes = ReadBounded(basePath, MaxPayloadBytes);
            baseHash = Hash(baseBytes);
            var children = new Dictionary<string, string>(StringComparer.Ordinal);
            var records = new Dictionary<string, (string Parent, string Path)>(StringComparer.Ordinal);
            int inspected = 0;
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(path);
                if (!name.StartsWith("history-", StringComparison.Ordinal) && !name.StartsWith(".history-", StringComparison.Ordinal)) continue;
                if (++inspected > maxRecords) return new(InspectionStatus.LimitExceeded, baseHash, null, null, "Sürüm tarama sınırı aşıldı; kayıtlar korunuyor.");
                if (name.StartsWith(".history-", StringComparison.Ordinal))
                    return new(InspectionStatus.Invalid, baseHash, null, null, "Tamamlanmamış sürüm dosyası bulundu: " + name);
                if (!name.EndsWith(".json", StringComparison.Ordinal) || name.Length != 77)
                    return new(InspectionStatus.Invalid, baseHash, null, null, "Geçersiz sürüm dosyası adı: " + name);
                var namedHash = name.Substring(8, 64);
                if (!IsHash(namedHash)) return new(InspectionStatus.Invalid, baseHash, null, null, "Geçersiz sürüm özeti: " + name);
                var bytes = ReadBounded(path, MaxRecordBytes);
                if (Hash(bytes) != namedHash) return new(InspectionStatus.Invalid, baseHash, null, null, "Sürüm içeriği adıyla eşleşmiyor: " + name);
                var record = JsonSerializer.Deserialize<VersionRecord>(bytes) ?? throw new InvalidDataException("Sürüm kaydı boş.");
                if (record.Version != 1 || record.JobId != jobId || record.BaseHash != baseHash || !IsHash(record.ParentHash) ||
                    !Guid.TryParseExact(record.Nonce, "N", out _) || record.Payload is null || record.Payload.Length > MaxPayloadBytes)
                    return new(InspectionStatus.Invalid, baseHash, null, null, "Sürüm içeriği veya temel kayıt geçersiz: " + name);
                if (!children.TryAdd(record.ParentHash, namedHash))
                    return new(InspectionStatus.Conflict, baseHash, null, null, "Aynı ebeveynden birden fazla sürüm oluştu.");
                records.Add(namedHash, (record.ParentHash, path));
            }
            var head = baseHash;
            int traversed = 0;
            while (children.TryGetValue(head, out var child))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++traversed > records.Count) return new(InspectionStatus.Invalid, baseHash, null, null, "Sürüm zinciri döngü içeriyor.");
                head = child;
            }
            if (traversed != records.Count) return new(InspectionStatus.Invalid, baseHash, null, null, "Bağlantısız veya eksik sürüm var.");
            byte[] payload;
            if (head == baseHash) payload = baseBytes;
            else
            {
                var bytes = ReadBounded(records[head].Path, MaxRecordBytes);
                if (Hash(bytes) != head) return new(InspectionStatus.Invalid, baseHash, null, null, "Sürüm tarama sırasında değişti.");
                payload = JsonSerializer.Deserialize<VersionRecord>(bytes)!.Payload;
            }
            if (Hash(ReadBounded(basePath, MaxPayloadBytes)) != baseHash)
                return new(InspectionStatus.Invalid, baseHash, null, null, "Temel kayıt tarama sırasında değişti.");
            return new Inspection(InspectionStatus.Valid, baseHash, head, payload, null) { RecordCount = records.Count };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or NotSupportedException)
        { return new(InspectionStatus.Invalid, baseHash, null, null, ex.Message); }
    }

    /// <summary>
    /// Publishes a new unique version without replacing or deleting any file.
    /// Concurrent publishers can create a branch; a subsequent inspection is
    /// required and may report a conflict even after this method returned.
    /// </summary>
    public Inspection Append(string expectedHeadHash, byte[] payload, int maxRecords = MaxAllowedRecords, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!IsHash(expectedHeadHash)) throw new ArgumentException("Geçerli baş özeti gerekli.", nameof(expectedHeadHash));
        if (payload.Length > MaxPayloadBytes) throw new ArgumentOutOfRangeException(nameof(payload));
        var before = Inspect(maxRecords, cancellationToken);
        if (before.Status != InspectionStatus.Valid || before.HeadHash != expectedHeadHash)
            throw new InvalidOperationException("Sürüm değişti veya doğrulanamadı; kayıt uygulanmadı.");
        if (before.RecordCount >= maxRecords)
            throw new InvalidOperationException("Sürüm sınırına ulaşıldı; kayıt uygulanmadı.");
        // Concurrent writers can still race this bound; post-inspection reports it.
        var record = new VersionRecord(1, jobId, before.BaseHash!, before.HeadHash!, Guid.NewGuid().ToString("N"), payload.ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        if (bytes.Length > MaxRecordBytes) throw new InvalidOperationException("Sürüm kaydı boyut sınırını aşıyor.");
        cancellationToken.ThrowIfCancellationRequested();
        var hash = Hash(bytes);
        var temp = Path.Combine(directory, ".history-" + Guid.NewGuid().ToString("N") + ".tmp");
        var target = Path.Combine(directory, "history-" + hash + ".json");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Güvenli sürüm yayını için Windows dosya kimliği gereklidir.");
        BeforePublishForVerification?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        using (var publication = ImmutableHistoryPublication.Open(directory))
        {
            publication.Publish(Path.GetFileName(temp), Path.GetFileName(target), bytes, () =>
            {
                AfterTempWriteForVerification?.Invoke();
                BeforeHandlePublishForVerification?.Invoke(temp, target);
            }, cancellationToken);
        }
        // Publication already happened; cancellation cannot claim it did not.
        return Inspect(maxRecords, CancellationToken.None);
    }
}
