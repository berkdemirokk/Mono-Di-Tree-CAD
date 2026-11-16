namespace TekstilCAD.Core;

/// <summary>Machine-readable reason for a refused piece operation. The Turkish text for the user is in <see cref="PieceOperationResult.Error"/>.</summary>
public enum PieceOperationError
{
    None,
    /// <summary>The piece id is not in the document.</summary>
    PieceNotFound,
    /// <summary>A number or text argument is not usable (NaN, too small, empty, wrong form).</summary>
    InvalidValue,
    /// <summary>The result would leave the ±100,000,000 mm working area.</summary>
    OutOfRange,
    /// <summary>The piece (or the target layer) is locked.</summary>
    LayerLocked,
    /// <summary>The target layer does not exist or is hidden.</summary>
    LayerUnavailable,
    /// <summary>A project limit (pieces, points) would be exceeded.</summary>
    LimitExceeded,
    /// <summary>The operation cannot be applied to this kind of piece (analytic curves).</summary>
    Unsupported,
    /// <summary>The result cannot be represented with the required numeric precision.</summary>
    Precision,
    /// <summary>The resulting contour would not be a valid shape.</summary>
    InvalidShape
}

/// <summary>
/// Outcome of one <see cref="PieceOperations"/> call: either an edited copy of the document
/// or a Turkish refusal. Operations never throw for user mistakes and never change their input.
/// The result is a preview; apply it with <see cref="ApplyTo"/> inside <see cref="DocumentSession.Execute"/>,
/// for example <c>session.Execute(result.ApplyTo, ProjectStorage.Validate)</c>.
/// </summary>
public sealed class PieceOperationResult
{
    private readonly ProjectDocument? source;
    private readonly ProjectDocument? edited;
    private bool? changes;

    private PieceOperationResult(ProjectDocument? source, ProjectDocument? edited, Guid? pieceId, string summary,
        IReadOnlyList<string> warnings, PieceOperationError code, string? error, string? detail)
    {
        this.source = source; this.edited = edited; PieceId = pieceId; Summary = summary;
        Warnings = warnings; ErrorCode = code; Error = error; ErrorDetail = detail;
    }

    public bool Succeeded => ErrorCode == PieceOperationError.None;
    public PieceOperationError ErrorCode { get; }
    /// <summary>Turkish refusal text saying what happened and what to do; null on success.</summary>
    public string? Error { get; }
    /// <summary>Technical detail of the underlying failure, for logs; may be non-Turkish. Null when there is none.</summary>
    public string? ErrorDetail { get; }
    /// <summary>One Turkish sentence describing the edit, suitable for a preview and a status line. Empty on failure.</summary>
    public string Summary { get; }
    /// <summary>Non-fatal Turkish notes (for example a grading rule that no longer matches the geometry).</summary>
    public IReadOnlyList<string> Warnings { get; }
    /// <summary>The whole document after the edit. It is a private copy; treat it as read-only. Null on failure.</summary>
    public ProjectDocument? Document => edited;
    /// <summary>The piece that was created or changed; null for a deletion and on failure.</summary>
    public Guid? PieceId { get; }
    /// <summary>The created or changed piece inside <see cref="Document"/>; null for a deletion and on failure.</summary>
    public PatternPiece? Piece => PieceId is { } id ? edited?.Pieces.FirstOrDefault(p => p.Id == id) : null;

    /// <summary>False when the operation succeeded but would leave the document bit-for-bit unchanged (no undo step would be recorded).</summary>
    public bool ChangesDocument
    {
        get
        {
            if (!Succeeded) return false;
            return changes ??= !DocumentSession.ContentEquals(source!, edited!);
        }
    }

    /// <summary>
    /// Writes the previewed edit into <paramref name="target"/>. Intended as the action of
    /// <see cref="DocumentSession.Execute"/>. Refuses with <see cref="InvalidOperationException"/> when the
    /// target no longer has the content the operation was calculated from, so an out-of-date preview is never applied.
    /// </summary>
    public void ApplyTo(ProjectDocument target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Succeeded) throw new InvalidOperationException(Error);
        if (!DocumentSession.ContentEquals(source!, target))
            throw new InvalidOperationException("Proje, işlem önizlemesinden sonra değişti; işlem uygulanmadı. Komutu yeniden verin.");
        var fresh = edited!.Clone();
        target.Pieces = fresh.Pieces;
        target.GradingRules = fresh.GradingRules;
    }

    internal static PieceOperationResult Success(ProjectDocument source, ProjectDocument edited, Guid? pieceId, string summary, IReadOnlyList<string>? warnings = null)
        => new(source.Clone(), edited, pieceId, summary, warnings ?? [], PieceOperationError.None, null, null); // snapshot: the caller may keep editing its own document

    internal static PieceOperationResult Failure(PieceOperationError code, string error, string? detail = null)
        => new(null, null, null, "", [], code, error, detail);
}
