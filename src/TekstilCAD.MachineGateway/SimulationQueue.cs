using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.MachineGateway;

public enum JobFormat { Dxf, Hpgl }
public enum JobStatus { Prepared, Simulated, Failed }

/// <summary>File simulator profile. Never represents validated physical-machine compatibility.</summary>
public sealed record MachineProfile(string Name, double UnitsPerMillimeter = 40, double Scale = 1)
{
    public bool IsVerified => false;
    public bool IsSimulator => true;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 200) throw new ArgumentException("Profil adı gerekli ve en fazla 200 karakter olmalıdır.");
        if (!double.IsFinite(UnitsPerMillimeter) || UnitsPerMillimeter <= 0 || UnitsPerMillimeter > 10000)
            throw new ArgumentOutOfRangeException(nameof(UnitsPerMillimeter));
        if (!double.IsFinite(Scale) || Scale <= 0 || Scale > 1000) throw new ArgumentOutOfRangeException(nameof(Scale));
    }
}

public sealed record SimulationJob(Guid Id, string ProjectName, MachineProfile Profile, JobFormat Format,
    JobStatus Status, string OutputPath, string ManifestPath, string Sha256, DateTimeOffset CreatedAt,
    int PieceCount, int PointCount, string? Error = null)
{
    public int ManifestVersion { get; init; } = 1;
    public bool SourceSnapshotUnconfirmed { get; init; }
}

/// <summary>Only writes local exchange files and simulates checksum verification. No machine I/O.</summary>
public sealed class SimulationQueue
{
    /// <summary>
    /// A file-free HPGL proposal bound to this queue, a detached selected source
    /// and the exact profile. Review remains informational and does not certify a machine.
    /// </summary>
    public sealed class PreparedHpglJob
    {
        internal PreparedHpglJob(SimulationQueue owner,ProjectDocument source,MachineProfile profile,HpglPreparedOutput output)
        { Owner=owner;Source=source;Profile=profile;Output=output; }
        internal SimulationQueue Owner { get; }
        internal ProjectDocument Source { get; }
        private int consumed;
        internal bool TryConsume() => Interlocked.CompareExchange(ref consumed,1,0)==0;
        public MachineProfile Profile { get; }
        public HpglPreparedOutput Output { get; }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly List<SimulationJob> jobs = [];
    private readonly Dictionary<Guid, string> manifestHashes = [];
    private readonly List<string> recoveryWarnings = [];
    private readonly string spoolDirectory;
    private readonly bool streamingHistory;
    public bool IsStreamingHistory => streamingHistory;
    private Guid lastPreparedId;
    private string? lastPreparedHash;
    // Instance-scoped hook for deterministic temporary-file ownership tests.
    internal Action<string>? BeforeManifestCommitForVerification { get; set; }
    internal Action<string>? AfterHistoryCommitForVerification { get; set; }
    private const int MaximumRecoveredJobs = 10_000;
    private const int MaximumManifestBytes = 128 * 1024;
    public IReadOnlyList<SimulationJob> Jobs => jobs.AsReadOnly();
    /// <summary>Read-only startup diagnostics. Recovery never sends, simulates, rewrites or deletes jobs.</summary>
    public IReadOnlyList<string> RecoveryWarnings => recoveryWarnings.AsReadOnly();
    public SimulationQueue(string spoolDirectory, bool streamingHistory = false)
    {
        this.spoolDirectory = ValidateOutputDirectory(spoolDirectory);
        this.streamingHistory = streamingHistory;
        if (!streamingHistory) RecoverJobs();
    }

    private void RecoverJobs()
    {
        // Directory.Exists hides access and I/O errors, making an unavailable
        // history look like an empty first run. Probe explicitly and distinguish
        // a genuinely absent directory from an inaccessible or non-directory path.
        try
        {
            if ((File.GetAttributes(spoolDirectory) & FileAttributes.Directory) == 0)
            { Warn("Kuyruk yolu bir klasör değil; iş geçmişi yüklenemedi."); return; }
        }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Warn("Kuyruk klasörüne erişilemedi; iş geçmişi yüklenemedi: " + ex.Message); return; }
        try
        {
            var inspected = 0;
            foreach (var directory in Directory.EnumerateDirectories(spoolDirectory))
            {
                if (++inspected > MaximumRecoveredJobs)
                { Warn("Kuyruk tarama sınırına ulaşıldı; kalan klasörler yüklenmedi."); break; }
                try
                {
                    ValidateOutputDirectory(directory);
                    if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var directoryId))
                        throw new InvalidDataException("İş klasörü geçerli bir kimlik taşımıyor.");
                    var manifest = Path.Combine(directory, "manifest.json");
                    RejectReparseFile(manifest);
                    using var stream = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length > MaximumManifestBytes) throw new InvalidDataException("İş kaydı boyut sınırını aşıyor.");
                    using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
                    var job = json.RootElement.Deserialize<SimulationJob>(JsonOptions)
                        ?? throw new InvalidDataException("İş kaydı boş.");
                    ValidateRecoveredJob(job, directoryId, directory, manifest);
                    if (json.RootElement.TryGetProperty("Profile", out var profileJson))
                    {
                        if (profileJson.TryGetProperty("IsVerified", out var verified) && verified.ValueKind != JsonValueKind.False)
                            throw new InvalidDataException("Simülasyon profili fiziksel doğrulama iddiası taşıyamaz.");
                        if (profileJson.TryGetProperty("IsSimulator", out var simulator) && simulator.ValueKind != JsonValueKind.True)
                            throw new InvalidDataException("İş yalnız dosya simülatörüne ait olmalıdır.");
                    }
                    job = job with { Sha256 = job.Sha256.ToUpperInvariant() };
                    stream.Position = 0;
                    var manifestHash = Convert.ToHexString(SHA256.HashData(stream));
                    RejectReparseFile(job.OutputPath);
                    if (!File.Exists(job.OutputPath))
                    {
                        job = job with { Status = JobStatus.Failed, Error = "Çıktı dosyası eksik; iş yeniden hazırlanmalı." };
                        Warn($"{directoryId:N}: çıktı dosyası eksik; önceki disk kaydı korundu.");
                    }
                    jobs.Add(job);
                    manifestHashes.Add(job.Id, manifestHash);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or NotSupportedException)
                { Warn($"{Path.GetFileName(directory)}: iş yüklenemedi. {ex.Message}"); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Warn("Kuyruk klasörü bütünüyle taranamadı: " + ex.Message); }
        jobs.Sort((a, b) => { var date = a.CreatedAt.CompareTo(b.CreatedAt); return date != 0 ? date : a.Id.CompareTo(b.Id); });
    }

    /// <summary>One observed directory. An error row is retained even for incomplete jobs.</summary>
    public sealed class HistoryEntry
    {
        internal HistoryEntry(SimulationQueue owner, string path, SimulationJob? job, string? error, string? hash)
        { Owner = owner; Path = path; Job = job; Error = error; ManifestHash = hash; }
        internal SimulationQueue Owner { get; }
        public string Path { get; }
        public SimulationJob? Job { get; }
        public string? Error { get; }
        public string? ManifestHash { get; }
    }
    public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Entries, bool Complete, string? Error);

    /// <summary>Live, forward-only scan. Additions and removals during enumeration may be missed.
    /// Restart to observe changes. The caller must serialize queue and session operations.</summary>
    public sealed class HistoryScanSession : IDisposable
    {
        private readonly SimulationQueue owner;
        private IEnumerator<string>? directories;
        private readonly List<HistoryEntry> pending = [];
        private bool complete;
        private bool disposed;
        private string? error;
        internal HistoryScanSession(SimulationQueue owner) { this.owner = owner; }
        public HistoryPage ReadNextPage(int pageSize = 100, CancellationToken cancellationToken = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(HistoryScanSession));
            if (pageSize < 1 || pageSize > 1000) throw new ArgumentOutOfRangeException(nameof(pageSize));
            cancellationToken.ThrowIfCancellationRequested();
            if (!complete && error is null && directories is null)
            {
                try
                {
                    try
                    {
                        if ((File.GetAttributes(owner.spoolDirectory) & FileAttributes.Directory) == 0)
                            throw new IOException("Kuyruk yolu bir klasör değil.");
                    }
                    catch (FileNotFoundException) { complete = true; }
                    catch (DirectoryNotFoundException) { complete = true; }
                    if (!complete) directories = Directory.EnumerateDirectories(owner.spoolDirectory).GetEnumerator();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex.Message; }
            }
            while (!complete && error is null && pending.Count < pageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!directories!.MoveNext()) { complete = true; directories.Dispose(); directories = null; break; }
                    pending.Add(owner.ReadHistoryDirectory(directories.Current));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { error = ex.Message; directories?.Dispose(); directories = null; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            // A caller may choose a smaller size after cancellation; retain the remainder.
            var count = Math.Min(pageSize, pending.Count);
            var entries = pending.GetRange(0, count).ToArray();
            pending.RemoveRange(0, count);
            return new HistoryPage(entries, complete && pending.Count == 0, error);
        }
        public void Dispose() { if (disposed) return; disposed = true; directories?.Dispose(); directories = null; pending.Clear(); }
    }
    public HistoryScanSession StartHistoryScan() => new(this);
    public HistoryEntry GetHistoryEntry(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Geçerli iş kimliği gerekli.", nameof(id));
        return ReadHistoryDirectory(Path.Combine(spoolDirectory, id.ToString("N")));
    }
    public HistoryEntry GetPreparedHistoryEntry(SimulationJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var entry = GetHistoryEntry(job.Id);
        if (entry.Job != job || job.Id != lastPreparedId || lastPreparedHash is null || entry.ManifestHash != lastPreparedHash)
            throw new InvalidOperationException("Hazırlanan iş kaydı değişti; tekrar hazırlayın.");
        return entry;
    }
    private HistoryEntry ReadHistoryDirectory(string directory)
    {
        try
        {
            ValidateOutputDirectory(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) throw new InvalidDataException("İş klasörü geçerli bir kimlik taşımıyor.");
            var manifest = Path.Combine(directory, "manifest.json");
            RejectReparseFile(manifest);
            using var stream = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumManifestBytes) throw new InvalidDataException("İş kaydı boyut sınırını aşıyor.");
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            var job = json.RootElement.Deserialize<SimulationJob>(JsonOptions) ?? throw new InvalidDataException("İş kaydı boş.");
            ValidateRecoveredJob(job, id, directory, manifest);
            if (json.RootElement.TryGetProperty("Profile", out var profile))
            {
                if (profile.TryGetProperty("IsVerified", out var verified) && verified.ValueKind != JsonValueKind.False) throw new InvalidDataException("Simülasyon profili fiziksel doğrulama iddiası taşıyamaz.");
                if (profile.TryGetProperty("IsSimulator", out var simulator) && simulator.ValueKind != JsonValueKind.True) throw new InvalidDataException("İş yalnız dosya simülatörüne ait olmalıdır.");
            }
            stream.Position = 0;
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            job = job with { Sha256 = job.Sha256.ToUpperInvariant() };
            RejectReparseFile(job.OutputPath);
            var error = !File.Exists(job.OutputPath) ? "Çıktı dosyası eksik; iş yeniden hazırlanmalı." : null;
            return new HistoryEntry(this, directory, job, error, hash);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or NotSupportedException)
        { return new HistoryEntry(this, directory, null, ex.Message.Length > 2048 ? ex.Message[..2048] : ex.Message, null); }
    }
    private SimulationJob RequireHistoryEntry(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!ReferenceEquals(entry.Owner, this) || entry.Job is null || entry.ManifestHash is null)
            throw new InvalidOperationException("İş kaydı kullanılamıyor; yeniden tarayın.");
        var expected = Path.Combine(spoolDirectory, entry.Job.Id.ToString("N"));
        if (!IsExpectedPath(entry.Path, expected)) throw new InvalidOperationException("İş yolu eşleşmiyor.");
        return entry.Job;
    }
    public HistoryEntry ConfirmHistoryEntry(HistoryEntry entry)
    {
        var job = RequireHistoryEntry(entry) with { SourceSnapshotUnconfirmed = false };
        var writtenHash = SaveManifest(job, entry.ManifestHash);
        return VerifyCommittedHistoryEntry(job, writtenHash);
    }
    public HistoryEntry SimulateHistoryEntry(HistoryEntry entry)
    {
        var job = RequireHistoryEntry(entry);
        try
        {
            ValidateOutputDirectory(Path.GetDirectoryName(job.OutputPath)!);
            RejectReparseFile(job.OutputPath);
            if (!File.Exists(job.OutputPath) || Checksum(job.OutputPath) != job.Sha256)
                throw new InvalidDataException("Çıktı dosyası eksik veya özeti değişmiş.");
            job = job with { Status = JobStatus.Simulated, Error = null };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { job = job with { Status = JobStatus.Failed, Error = ex.Message }; }
        var writtenHash = SaveManifest(job, entry.ManifestHash);
        return VerifyCommittedHistoryEntry(job, writtenHash);
    }

    private HistoryEntry VerifyCommittedHistoryEntry(SimulationJob job, string writtenHash)
    {
        try
        {
            AfterHistoryCommitForVerification?.Invoke(job.ManifestPath);
            var observed = GetHistoryEntry(job.Id);
            if (observed.Job == job && observed.ManifestHash == writtenHash) return observed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException("İşlem uygulanmış olabilir; kayıt sonrasında doğrulanamadı. Durumu yeniden tarayarak kontrol edin.", ex);
        }
        throw new InvalidOperationException("İşlem uygulanmış olabilir; kayıt sonrasında değişti veya doğrulanamadı. Durumu yeniden tarayarak kontrol edin.");
    }

    private static void ValidateRecoveredJob(SimulationJob job, Guid directoryId, string directory, string manifest)
    {
        if (job.ManifestVersion != 1 || job.Id == Guid.Empty || job.Id != directoryId || !Enum.IsDefined(job.Format) || !Enum.IsDefined(job.Status))
            throw new InvalidDataException("İş sürümü, kimliği veya durumu desteklenmiyor.");
        if (job.Profile is null || job.ProjectName is null || job.ProjectName.Length > 1024 || job.Error?.Length > 4096)
            throw new InvalidDataException("İş açıklaması veya profili geçersiz.");
        job.Profile.Validate();
        if (job.PieceCount < 1 || job.PieceCount > ProjectStorage.MaximumPieces || job.PointCount < 2L * job.PieceCount || job.PointCount > ProjectStorage.MaximumTotalPoints || job.CreatedAt == default)
            throw new InvalidDataException("İş geometri sayımları veya zamanı geçersiz.");
        if (job.Sha256 is null || job.Sha256.Length != 64 || !job.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("İş dosyasının SHA256 özeti geçersiz.");
        var output = Path.Combine(directory, job.Format == JobFormat.Dxf ? "preview.dxf" : "preview.plt");
        if (!IsExpectedPath(job.ManifestPath, manifest) || !IsExpectedPath(job.OutputPath, output))
            throw new InvalidDataException("İş yolları kendi kuyruk klasörüyle eşleşmiyor; kayıt otomatik taşınmadı.");
    }

    private static bool IsExpectedPath(string? actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(actual) || !Path.IsPathFullyQualified(actual) || actual.Split('\\', '/').Any(p => p is "." or "..")) return false;
        return string.Equals(Path.GetFullPath(actual), expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void RejectReparseFile(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Yönlendirilmiş iş dosyası kullanılamaz.");
    }

    private void Warn(string warning)
    {
        if (warning.Length > 2048) warning = warning[..2048] + "…";
        if (recoveryWarnings.Count < 200) recoveryWarnings.Add(warning);
        else if (recoveryWarnings.Count == 200) recoveryWarnings.Add("Ek kuyruk uyarıları sınır nedeniyle gösterilmiyor.");
    }

    /// <summary>
    /// Captures the selected source and prepares scaled HPGL without creating files.
    /// The caller must prevent source mutation during capture. This queue is not
    /// thread-safe; serialize its operations and review only the returned output.
    /// </summary>
    public PreparedHpglJob PrepareHpglReview(ProjectDocument document,MachineProfile profile,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);profile.Validate();ProjectStorage.Validate(document);
        cancellationToken.ThrowIfCancellationRequested();
        var source=document.Clone();ProjectStorage.Validate(source);cancellationToken.ThrowIfCancellationRequested();
        if(source.Pieces.Count==0)throw new InvalidDataException("Üretim simülasyonu için çizim gerekli.");
        var scaled=source.Clone();
        foreach(var piece in scaled.Pieces)
        for(var i=0;i<piece.Points.Count;i++)
        {
            if((i&255)==0)cancellationToken.ThrowIfCancellationRequested();
            var point=piece.Points[i]*profile.Scale;Geometry.Validate(point);piece.Points[i]=point;
        }
        var output=HpglFormat.Prepare(scaled,profile.UnitsPerMillimeter,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new PreparedHpglJob(this,source,profile,output);
    }

    /// <summary>
    /// Writes a reviewed proposal only for its originating queue and matching
    /// source and profile. Cancellation before replacement preserves the target;
    /// after replacement, the manifest is completed without a rollback claim.
    /// Source comparison requires the caller to keep it stable during this call.
    /// A failed manifest write may leave the generated local file for inspection.
    /// </summary>
    public SimulationJob CommitPreparedHpgl(PreparedHpglJob prepared,ProjectDocument document,MachineProfile profile,
        bool sourceSnapshotUnconfirmed=false,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(prepared);ArgumentNullException.ThrowIfNull(document);ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();ProjectStorage.Validate(document);
        if(!ReferenceEquals(prepared.Owner,this) || prepared.Profile!=profile || !DocumentSession.ContentEquals(prepared.Source,document))
            throw new InvalidOperationException("Hazırlanan HPGL işi bu kuyruk, kaynak veya profil ile eşleşmiyor; yeniden hazırlayın.");
        cancellationToken.ThrowIfCancellationRequested();
        // A review authorizes one commit attempt only. A failed or partially
        // written attempt needs a fresh review; never retry it implicitly.
        if(!prepared.TryConsume())throw new InvalidOperationException("Bu HPGL incelemesi zaten kullanıldı; yeni bir iş için yeniden hazırlayın.");
        var id=Guid.NewGuid();var directory=ValidateOutputDirectory(Path.Combine(spoolDirectory,id.ToString("N")));
        Directory.CreateDirectory(directory);
        var output=Path.Combine(directory,"preview.plt");var version=ProjectStorage.CaptureVersion(output);
        if(version.Sha256 is not null)throw new IOException("Yeni iş çıktı dosyası zaten mevcut; işlem uygulanmadı.");
        HpglFormat.WritePrepared(prepared.Output,output,version,cancellationToken);
        // Record the hash of the reviewed bytes, not a subsequently substituted file.
        var reviewedHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prepared.Output.Text)));
        if(Checksum(output)!=reviewedHash)throw new IOException("Hazırlanan HPGL dosyası yazımdan sonra değişti; iş kaydı oluşturulmadı.");
        var job=new SimulationJob(id,prepared.Source.Name,prepared.Profile,JobFormat.Hpgl,JobStatus.Prepared,output,
            Path.Combine(directory,"manifest.json"),reviewedHash,DateTimeOffset.UtcNow,
            prepared.Source.Pieces.Count,prepared.Source.Pieces.Sum(p=>p.Points.Count)) { SourceSnapshotUnconfirmed=sourceSnapshotUnconfirmed };
        var writtenHash = SaveManifest(job);
        if (streamingHistory) { lastPreparedId = job.Id; lastPreparedHash = writtenHash; }
        else jobs.Add(job);
        return job;
    }

    public SimulationJob Prepare(ProjectDocument document, MachineProfile profile, JobFormat format, bool sourceSnapshotUnconfirmed = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        ProjectStorage.Validate(document);
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (document.Pieces.Count == 0 || document.Pieces.All(p => p.Points.Count < 2))
            throw new InvalidDataException("Üretim simülasyonu için çizim gerekli.");
        // DXF has no cancellable writer; this token is only honored for HPGL.
        var hpglCancellation = format == JobFormat.Hpgl ? cancellationToken : CancellationToken.None;
        hpglCancellation.ThrowIfCancellationRequested();
        var scaled = document.Clone();
        foreach (var piece in scaled.Pieces)
        {
            for (var i = 0; i < piece.Points.Count; i++)
            {
                hpglCancellation.ThrowIfCancellationRequested();
                var point = piece.Points[i] * profile.Scale;
                Geometry.Validate(point);
                piece.Points[i] = point;
            }
        }
        hpglCancellation.ThrowIfCancellationRequested();
        var id = Guid.NewGuid();
        var directory = ValidateOutputDirectory(Path.Combine(spoolDirectory, id.ToString("N")));
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, format == JobFormat.Dxf ? "preview.dxf" : "preview.plt");
        if (format == JobFormat.Dxf) DxfFormat.Export(scaled, output);
        else HpglFormat.Export(scaled, output, profile.UnitsPerMillimeter, hpglCancellation);
        var job = new SimulationJob(id, document.Name, profile, format, JobStatus.Prepared, output,
            Path.Combine(directory, "manifest.json"), Checksum(output), DateTimeOffset.UtcNow,
            scaled.Pieces.Count, scaled.Pieces.Sum(p => p.Points.Count)) { SourceSnapshotUnconfirmed = sourceSnapshotUnconfirmed };
        var writtenHash = SaveManifest(job);
        if (streamingHistory) { lastPreparedId = job.Id; lastPreparedHash = writtenHash; }
        else jobs.Add(job);
        return job;
    }

    public SimulationJob ConfirmSourceSnapshot(Guid id)
    {
        var index = jobs.FindIndex(j => j.Id == id);
        if (index < 0) throw new KeyNotFoundException("Kuyruk işi bulunamadı.");
        var job = jobs[index] with { SourceSnapshotUnconfirmed = false };
        SaveManifest(job);
        jobs[index] = job;
        return job;
    }

    public SimulationJob Simulate(Guid id)
    {
        var index = jobs.FindIndex(j => j.Id == id);
        if (index < 0) throw new KeyNotFoundException("Kuyruk işi bulunamadı.");
        var job = jobs[index];
        try
        {
            ValidateOutputDirectory(Path.GetDirectoryName(job.OutputPath)!);
            RejectReparseFile(job.OutputPath);
            if (!File.Exists(job.OutputPath) || Checksum(job.OutputPath) != job.Sha256)
                throw new InvalidDataException("Çıktı dosyası eksik veya özeti değişmiş.");
            job = job with { Status = JobStatus.Simulated, Error = null };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { job = job with { Status = JobStatus.Failed, Error = ex.Message }; }
        SaveManifest(job);
        jobs[index] = job;
        return job;
    }

    private static string Checksum(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

    private string SaveManifest(SimulationJob job, string? selectedHash = null)
    {
        ValidateOutputDirectory(Path.GetDirectoryName(job.ManifestPath)!);
        RejectReparseFile(job.ManifestPath);
        // Every writer holds this per-job lock while comparing and replacing the manifest.
        // Keep the lock file: deleting it would allow another process to lock a different inode.
        var lockPath = job.ManifestPath + ".lock";
        RejectReparseFile(lockPath);
        if (OperatingSystem.IsWindows()) FileIdentity.RejectMultipleLinksForReplacement(lockPath);
        using var manifestLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RejectReparseFile(job.ManifestPath);
        var expectedHash = selectedHash;
        if (expectedHash is null && !streamingHistory) manifestHashes.TryGetValue(job.Id, out expectedHash);
        if (expectedHash is not null)
        {
            if (!File.Exists(job.ManifestPath) || Checksum(job.ManifestPath) != expectedHash)
                throw new InvalidOperationException("İş kaydı başka bir işlem tarafından değiştirildi; kuyruğu yeniden açın.");
        }
        else if (File.Exists(job.ManifestPath))
            throw new InvalidOperationException("İş kaydı zaten mevcut; kuyruğu yeniden açın.");
        var temporary = job.ManifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var committed = false;
        FileIdentity.OwnedFileStamp? ownedTemp = null;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(job, JsonOptions);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (OperatingSystem.IsWindows()) ownedTemp = FileIdentity.CaptureOwnedFile(output.SafeFileHandle);
                output.Write(bytes);
            }
            BeforeManifestCommitForVerification?.Invoke(temporary);
            if (OperatingSystem.IsWindows())
            {
                if (ownedTemp is not { } stamp || !FileIdentity.IsOwnedFile(temporary, stamp))
                    throw new IOException("Geçici iş kaydı değişti; kayıt uygulanmadı.");
                FileIdentity.RejectMultipleLinksForReplacement(job.ManifestPath);
            }
            File.Move(temporary, job.ManifestPath, true);
            committed = true;
            var writtenHash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!streamingHistory) manifestHashes[job.Id] = writtenHash;
            return writtenHash;
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    if (OperatingSystem.IsWindows() && ownedTemp is { } stamp && FileIdentity.IsOwnedFile(temporary, stamp))
                        File.Delete(temporary);
                }
                catch { /* retain uncertain temp for inspection */ }
            }
        }
    }

    public static string ValidateOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\") || path.IndexOf(':', 2) >= 0)
            throw new ArgumentException("Mutlak yerel disk klasörü gerekli; ağ ve aygıt yolları desteklenmez.");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        try { ProjectStorage.EnsureWritableDestination(Path.Combine(full, "manifest.json")); }
        catch (InvalidOperationException ex) { throw new UnauthorizedAccessException(ex.Message, ex); }
        if (full.Length <= 2) throw new ArgumentException("Disk kökü yerine bir alt klasör seçin.");
        foreach (var root in new[] { @"C:\WearCadfe", @"C:\Hpgl", @"C:\Nester", @"C:\Nester1", @"C:\NST" })
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("WearCAD referans klasörlerine yazılamaz.");
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Yönlendirilmiş klasörlere yazılamaz.");
        return full;
    }
}



