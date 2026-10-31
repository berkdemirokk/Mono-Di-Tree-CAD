using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using TekstilCAD.Core;

[assembly: InternalsVisibleTo("TekstilCAD.Studio")]

namespace TekstilCAD.FileFormats;

public sealed record RecoveryCandidate(string Path, string? ProjectName, DateTime LastWriteTimeUtc, string? Error, bool IsLegacy = false)
{
    public bool IsRecoverable => Error is null;
}

public sealed record RecoveryScanError(string? Path, string Message);
public sealed record RecoveryScanResult(IReadOnlyList<RecoveryCandidate> Candidates, IReadOnlyList<RecoveryScanError> Errors);
public sealed record RecoveredDocument(ProjectDocument Document, string SourcePath, string ContentHash, bool CanArchive = true)
{
    public IReadOnlyList<RecoveryPropertyDraft> Drafts { get; init; } = Array.Empty<RecoveryPropertyDraft>();
    public RecoveryPendingDrawing? PendingDrawing { get; init; }
}

/// <summary>
/// A recovery session owns an exclusive OS file lease until disposed. Disposal preserves recovery data.
/// All cooperating application instances must use this service before reading or archiving recovery files.
/// This is not a security boundary against another program replacing paths outside this protocol.
/// </summary>
public sealed class RecoveryStore : IDisposable
{
    private readonly string directory;
    private readonly FileStream lease;
    private bool disposed;
    private const int MaximumScanEntries = 4096;
    private const int MaximumScanCandidates = 1024;
    private const long MaximumScanBytes = 128L * 1024 * 1024;
    private static readonly byte[] LeaseMarker = System.Text.Encoding.ASCII.GetBytes("TekstilCAD.Recovery.v1");
    public Guid SessionId { get; } = Guid.NewGuid();
    public string RecoveryPath { get; }
    public string DraftRecoveryPath => Path.ChangeExtension(RecoveryPath, ".tcr");
    private bool? useDraftEnvelope;
    private FileIdentity.OwnedFileStamp? savedIdentity;
    private string? savedHash;
    private readonly Action? beforeCommitForVerification;

    internal RecoveryStore(string directory, Action beforeCommitForVerification) : this(directory)
        => this.beforeCommitForVerification = beforeCommitForVerification ?? throw new ArgumentNullException(nameof(beforeCommitForVerification));

    public RecoveryStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        RecoveryPath = Path.Combine(this.directory, $"session-{SessionId:N}.tcad");
        var lockPath = LockPath(RecoveryPath);
        ProjectStorage.EnsureWritableDestination(lockPath);
        Directory.CreateDirectory(this.directory);
        ProjectStorage.EnsureWritableDestination(lockPath);
        lease = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try { lease.Write(LeaseMarker); lease.Flush(true); }
        catch { lease.Dispose(); throw; }
    }

    public void Save(ProjectDocument document)
    {
        ThrowIfDisposed();
        if (useDraftEnvelope == true) throw new InvalidOperationException("Kurtarma oturumu biçimi değiştirilemez.");
        ProjectStorage.Validate(document);
        SaveOwned(RecoveryPath,ProjectStorage.SerializeProject(document),null);
        useDraftEnvelope = false;
    }

    /// <summary>Persist document and unapplied drafts together, without applying or validating draft text as coordinates.</summary>
    public void Save(ProjectDocument document, IReadOnlyList<RecoveryPropertyDraft> drafts)
    {
        ThrowIfDisposed();
        if (useDraftEnvelope == false) throw new InvalidOperationException("Kurtarma oturumu biçimi değiştirilemez.");
        var text = RecoveryEnvelope.Serialize(document, drafts);
        SaveOwned(DraftRecoveryPath,text,beforeCommitForVerification);
        useDraftEnvelope = true;
    }

    /// <summary>Atomically preserves project, inspector drafts and an unfinished drawing.</summary>
    public void Save(ProjectDocument document, IReadOnlyList<RecoveryPropertyDraft> drafts, RecoveryPendingDrawing? pendingDrawing)
    {
        ThrowIfDisposed();
        if (useDraftEnvelope == false) throw new InvalidOperationException("Kurtarma oturumu biçimi değiştirilemez.");
        var text = RecoveryEnvelope.Serialize(document, drafts, pendingDrawing);
        SaveOwned(DraftRecoveryPath,text,beforeCommitForVerification);
        useDraftEnvelope = true;
    }

    private void SaveOwned(string path,string text,Action? beforeCommit)
    {
        // Hash the bytes we asked the atomic writer to commit. Store the identity
        // captured from that writer's original temporary file only on success.
        var hash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        var identity=ProjectStorage.AtomicWriteOwned(path,text,savedIdentity,savedHash,beforeCommit);
        savedIdentity=identity;savedHash=hash;
    }
    /// <summary>Call only when this session's work was saved or explicitly discarded by the operator.</summary>
    public bool ClearOwn() => ClearOwn(null);

    // Per-call test callback runs while the verified file handle excludes writes and rename.
    internal bool ClearOwn(Action<string>? beforeDeleteForVerification)
    {
        ThrowIfDisposed();
        var path=useDraftEnvelope==true ? DraftRecoveryPath : RecoveryPath;
        ProjectStorage.EnsureWritableDestination(path);
        var deleted=RecoveryDeleteOwned.TryDelete(path,savedIdentity,savedHash,beforeDeleteForVerification);
        if(deleted){savedIdentity=null;savedHash=null;}
        return deleted;
    }
    public RecoveryScanResult Scan()
    {
        ThrowIfDisposed();
        return ScanDirectory(directory, useDraftEnvelope == true ? DraftRecoveryPath : RecoveryPath);
    }

    /// <summary>Missing folders are empty; per-file failures never hide other recovery candidates.</summary>
    public static RecoveryScanResult ScanDirectory(string directory, string? excludePath = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = new List<RecoveryCandidate>();
        var errors = new List<RecoveryScanError>();
        string fullDirectory;
        try
        {
            fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            ProjectStorage.EnsureWritableDestination(Path.Combine(fullDirectory, "recovery-scan.lock"));
        }
        catch (Exception ex) when (IsFileFailure(ex))
        {
            errors.Add(new(directory, ex.Message));
            return new(candidates, errors);
        }
        try
        {
            var inspected = 0; var considered = 0; long readBytes = 0;
            // Filter at enumeration so retained lease files do not consume the
            // candidate entry budget. Bound yielded entries across both formats.
            foreach (var path in EnumerateRecoveryFilesFairly(fullDirectory, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++inspected > MaximumScanEntries)
                { errors.Add(new(fullDirectory, "Kurtarma taramasında dosya giriş sınırına ulaşıldı; kalan dosyalar taranmadı.")); break; }
                var ext = Path.GetExtension(path);
                if (!string.Equals(ext, ".tcad", StringComparison.OrdinalIgnoreCase) && !IsEnvelope(path)) continue;
                if (string.Equals(path, excludePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (++considered > MaximumScanCandidates)
                { errors.Add(new(fullDirectory, "Kurtarma taramasında kayıt sayısı sınırına ulaşıldı; kalan kayıtlar taranmadı.")); break; }
                var inspectedCandidate = InspectCandidate(fullDirectory, path, MaximumScanBytes - readBytes, cancellationToken);
                if (inspectedCandidate.Deferred)
                { errors.Add(new(path, "Kurtarma taramasında toplam boyut sınırına ulaşıldı; bu ve kalan kayıtlar taranmadı.")); break; }
                readBytes += inspectedCandidate.BytesRead;
                if (inspectedCandidate.Candidate is { } candidate)
                {
                    candidates.Add(candidate);
                    if (candidate.Error is not null) errors.Add(new(path, candidate.Error));
                }
            }
        }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (IsFileFailure(ex)) { errors.Add(new(fullDirectory, "Kurtarma taraması tamamlanamadı: " + ex.Message)); }
        cancellationToken.ThrowIfCancellationRequested();
        return new(candidates.OrderByDescending(c => c.LastWriteTimeUtc).ToArray(), errors.ToArray());
    }

    // Shared with the paged scanner. Deferred candidates are retried on a fresh page.
    internal sealed record CandidateInspection(RecoveryCandidate? Candidate, long BytesRead, bool Deferred);
    internal static CandidateInspection InspectCandidate(string directory, string path, long remainingBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DateTime written = DateTime.MinValue;
        long readBytes = 0;
        try
        {
            ValidateCandidatePath(directory, path);
            using var candidateLease = TryAcquireCandidateLease(path);
            if (candidateLease is null) return new(null, 0, false);
            written = File.GetLastWriteTimeUtc(path);
            using var stableRead = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Reject an intrinsically oversized record before applying the aggregate
            // budget, so it cannot prevent later, valid records from being scanned.
            var maximumCandidateBytes = IsEnvelope(path) ? RecoveryEnvelope.MaximumBytes : 64 * 1024 * 1024;
            if (stableRead.Length > maximumCandidateBytes)
                throw new InvalidDataException("Kurtarma kaydı boyut sınırını aşıyor.");
            if (stableRead.Length > remainingBytes) return new(null, 0, true);
            readBytes = stableRead.Length;
            var document = IsEnvelope(path) ? ReadEnvelope(stableRead, token).Document : ReadProject(stableRead, token);
            token.ThrowIfCancellationRequested();
            return new(new(path, document.Name, written, null, !IsKnownLease(candidateLease)), readBytes, false);
        }
        catch (Exception ex) when (IsFileFailure(ex))
        { return new(new(path, null, written, ex.Message), readBytes, false); }
    }

    // Interleave formats so a large legacy backlog cannot consume the candidate
    // count limit before any newer draft-bearing record is considered, and vice
    // versa. This does not promise newest-first ordering or defeat byte limits.
    private static IEnumerable<string> EnumerateRecoveryFilesFairly(string directory, CancellationToken cancellationToken)
    {
        using var legacy = Directory.EnumerateFiles(directory, "session-*.tcad", SearchOption.TopDirectoryOnly).GetEnumerator();
        using var drafts = Directory.EnumerateFiles(directory, "session-*.tcr", SearchOption.TopDirectoryOnly).GetEnumerator();
        var legacyHasMore = true;
        var draftsHaveMore = true;
        while (legacyHasMore || draftsHaveMore)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (legacyHasMore)
            {
                legacyHasMore = legacy.MoveNext();
                if (legacyHasMore) yield return legacy.Current;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (draftsHaveMore)
            {
                draftsHaveMore = drafts.MoveNext();
                if (draftsHaveMore) yield return drafts.Current;
            }
        }
    }

    public RecoveredDocument LoadCandidate(string path)
    {
        ThrowIfDisposed();
        var candidate = ValidateOtherCandidate(path);
        using var candidateLease = TryAcquireCandidateLease(candidate)
            ?? throw new IOException("Bu kurtarma kaydı açık bir oturuma veya başka bir kurtarma işlemine ait. Tekrar tarayın.");
        // Block cooperating and ordinary Windows file writers while parsing and hashing the same file.
        using var stableRead = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
        RecoverySnapshot snapshot;
        if (IsEnvelope(candidate)) snapshot = ReadEnvelope(stableRead);
        else snapshot = new(ReadProject(stableRead, CancellationToken.None), Array.Empty<RecoveryPropertyDraft>());
        stableRead.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(stableRead));
        return new(snapshot.Document, candidate, hash, IsKnownLease(candidateLease)) { Drafts = snapshot.Drafts, PendingDrawing = snapshot.PendingDrawing };
    }

    /// <summary>
    /// Call only after the recovered document has been saved successfully to its permanent destination.
    /// A changed source or live lease is rejected. Archival failures leave the original recoverable file.
    /// </summary>
    public string ArchiveRecovered(RecoveredDocument recovered) => ArchiveRecovered(recovered,null);

    internal string ArchiveRecovered(RecoveredDocument recovered, Action<string>? afterLockedAndHashed, Action<string>? beforeArchiveCreation = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(recovered);
        var source = ValidateOtherCandidate(recovered.SourcePath);
        using var candidateLease = TryAcquireCandidateLease(source)
            ?? throw new IOException("Kurtarma kaydı başka bir işlem tarafından kullanılıyor; özgün kayıt korundu.");
        if(!recovered.CanArchive || !IsKnownLease(candidateLease))
            throw new InvalidOperationException("Eski sürüme ait kurtarma kaydının oturum durumu bilinmiyor; özgün dosya korunuyor.");

        var target = Path.Combine(directory, "archived", Path.GetFileNameWithoutExtension(source) + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(source));
        ProjectStorage.EnsureWritableDestination(source);
        ProjectStorage.EnsureWritableDestination(target);
        RecoveryArchiveAtomic.Move(source, target, recovered.ContentHash, afterLockedAndHashed, beforeArchiveCreation);
        return target;
    }

    private string ValidateOtherCandidate(string path)
    {
        var candidate = ValidateCandidatePath(directory, path);
        if (string.Equals(candidate, RecoveryPath, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate, DraftRecoveryPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Açık oturumun kendi kurtarma kaydı bu işlemle alınamaz.");
        return candidate;
    }

    private static string ValidateCandidatePath(string directory, string path)
    {
        var full = Path.GetFullPath(path);
        var name = Path.GetFileNameWithoutExtension(full);
        if (!string.Equals(Path.GetDirectoryName(full), directory, StringComparison.OrdinalIgnoreCase)
            || !(string.Equals(Path.GetExtension(full), ".tcad", StringComparison.OrdinalIgnoreCase) || IsEnvelope(full))
            || !name.StartsWith("session-", StringComparison.Ordinal)
            || !Guid.TryParseExact(name[8..], "N", out _))
            throw new InvalidOperationException("Kurtarma kaydı bu oturumun yönetilen klasöründe değil veya adı geçersiz.");
        ProjectStorage.EnsureWritableDestination(full);
        return full;
    }

    private static FileStream? TryAcquireCandidateLease(string path)
    {
        var lockPath = LockPath(path);
        ProjectStorage.EnsureWritableDestination(lockPath);
        try
        {
            // An active session holds this path exclusively. The identity probe
            // can encounter the same sharing violation as opening the lease;
            // both cases mean "live candidate", not a corrupt recovery file.
            if (OperatingSystem.IsWindows()) FileIdentity.RejectMultipleLinksForReplacement(lockPath);
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33) { return null; }
    }

    private static string LockPath(string path) => Path.ChangeExtension(path, ".lock");
    private static bool IsEnvelope(string path) => string.Equals(Path.GetExtension(path), ".tcr", StringComparison.OrdinalIgnoreCase);
    private static ProjectDocument ReadProject(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = ReadBounded(stream, 64 * 1024 * 1024, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = ProjectStorage.DeserializeProject(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static RecoverySnapshot ReadEnvelope(Stream stream, CancellationToken cancellationToken = default)
    {
        var bytes = ReadBounded(stream, RecoveryEnvelope.MaximumBytes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = RecoveryEnvelope.Deserialize(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static byte[] ReadBounded(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        if (stream.Length > maximumBytes) throw new InvalidDataException("Kurtarma kaydı boyut sınırını aşıyor.");
        using var memory = new MemoryStream((int)stream.Length);
        var buffer = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = stream.Read(buffer, 0, buffer.Length);
            if (count == 0) break;
            if (memory.Length + count > maximumBytes) throw new InvalidDataException("Kurtarma kaydı boyut sınırını aşıyor.");
            memory.Write(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return memory.ToArray();
    }
    private static bool IsKnownLease(FileStream stream)
    {
        if(stream.Length != LeaseMarker.Length) return false;
        stream.Position=0;var bytes=new byte[LeaseMarker.Length];
        stream.ReadExactly(bytes);return bytes.AsSpan().SequenceEqual(LeaseMarker);
    }
    private static bool IsFileFailure(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException
        or InvalidOperationException or ArgumentException or System.Security.SecurityException;
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lease.Dispose();
        // Empty lease files are deliberately retained. No recovery data is deleted on disposal.
    }
}
