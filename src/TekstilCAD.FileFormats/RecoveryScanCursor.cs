namespace TekstilCAD.FileFormats;

/// <summary>A page of a live, non-snapshot recovery directory scan.</summary>
public sealed record RecoveryScanPage(IReadOnlyList<RecoveryCandidate> Candidates,
    IReadOnlyList<RecoveryScanError> Errors, bool HasMore);

/// <summary>
/// Experimental bounded scanner. Not connected to the desktop UI. Directory mutations
/// may cause omissions or duplicates; candidate opening must be independently verified.
/// Single-threaded: callers must not read or dispose concurrently.
/// </summary>
public sealed class RecoveryScanCursor : IDisposable
{
    private readonly string directory;
    private readonly string? excludePath;
    private readonly int candidateLimit, entryLimit;
    private readonly long byteLimit;
    private readonly IEnumerator<string> paths;
    private string? pending;
    private bool ended, disposed;

    public RecoveryScanCursor(string directory, string? excludePath = null, int maxCandidatesPerPage = 50,
        long maxBytesPerPage = 128L * 1024 * 1024, int maxEntriesPerPage = 4096)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (maxCandidatesPerPage < 1 || maxCandidatesPerPage > 1024) throw new ArgumentOutOfRangeException(nameof(maxCandidatesPerPage));
        if (maxEntriesPerPage < 1 || maxEntriesPerPage > 4096) throw new ArgumentOutOfRangeException(nameof(maxEntriesPerPage));
        // A valid envelope must fit on an otherwise empty page.
        if (maxBytesPerPage < RecoveryEnvelope.MaximumBytes || maxBytesPerPage > 128L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxBytesPerPage));
        this.directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        this.excludePath = excludePath is null ? null : Path.GetFullPath(excludePath);
        candidateLimit = maxCandidatesPerPage; entryLimit = maxEntriesPerPage; byteLimit = maxBytesPerPage;
        ProjectStorage.EnsureWritableDestination(Path.Combine(this.directory, "recovery-scan.lock"));
        paths = FairPaths(this.directory).GetEnumerator();
    }

    public RecoveryScanPage ReadNextPage(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var candidates = new List<RecoveryCandidate>();
        var errors = new List<RecoveryScanError>();
        long bytes = 0; int entries = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            while (!ended && entries < entryLimit && candidates.Count < candidateLimit)
            {
                token.ThrowIfCancellationRequested();
                if (pending is null)
                {
                    if (!paths.MoveNext()) { ended = true; break; }
                    pending = paths.Current;
                }
                var path = pending;
                if (string.Equals(path, excludePath, StringComparison.OrdinalIgnoreCase))
                { pending = null; entries++; continue; }
                var item = RecoveryStore.InspectCandidate(directory, path, byteLimit - bytes, token);
                if (item.Deferred)
                {
                    if (bytes > 0) break; // Preserve the path for the next page.
                    // Larger than a full page: retain visibility without an infinite retry.
                    var error = "Kurtarma kaydı sayfa boyut sınırını aşıyor; özgün dosya korundu.";
                    candidates.Add(new(path, null, DateTime.MinValue, error));
                    errors.Add(new(path, error)); pending = null; entries++; continue;
                }
                pending = null; entries++; bytes += item.BytesRead;
                if (item.Candidate is { } candidate)
                {
                    candidates.Add(candidate);
                    if (candidate.Error is not null) errors.Add(new(path, candidate.Error));
                }
            }
            token.ThrowIfCancellationRequested();
            return new(candidates.AsReadOnly(), errors.AsReadOnly(), !ended);
        }
        catch (OperationCanceledException) { Dispose(); throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ended = true;
            errors.Add(new(directory, "Kurtarma taraması tamamlanamadı: " + ex.Message));
            return new(candidates.AsReadOnly(), errors.AsReadOnly(), false);
        }
    }

    private static IEnumerable<string> FairPaths(string directory)
    {
        using var legacy = Directory.EnumerateFiles(directory, "session-*.tcad", SearchOption.TopDirectoryOnly).GetEnumerator();
        using var drafts = Directory.EnumerateFiles(directory, "session-*.tcr", SearchOption.TopDirectoryOnly).GetEnumerator();
        bool a = true, b = true;
        while (a || b)
        {
            if (a) { a = legacy.MoveNext(); if (a) yield return legacy.Current; }
            if (b) { b = drafts.MoveNext(); if (b) yield return drafts.Current; }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; paths.Dispose();
    }
}
