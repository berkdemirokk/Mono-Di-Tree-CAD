namespace TekstilCAD.Core.Commands;

/// <summary>The pieces that are selected when a command is planned. Selection is an input of the planner, not part of the document.</summary>
/// <param name="PieceIds">Selected piece ids in selection order; empty when nothing is selected. An id that is not in the document counts as not selected.</param>
public sealed record Selection(IReadOnlyList<Guid> PieceIds)
{
    /// <summary>Nothing selected.</summary>
    public static Selection None { get; } = new(Array.Empty<Guid>());

    /// <summary>Exactly one piece selected.</summary>
    public static Selection Of(Guid pieceId) => new(new[] { pieceId });

    /// <summary>Number of selected ids (0 for a null list).</summary>
    public int Count => PieceIds?.Count ?? 0;
}

/// <summary>
/// An immutable snapshot of everything a plan depends on besides the selection. The planner only reads it; it never changes
/// <see cref="Document"/> (the caller keeps owning the live document, so the view holds the reference, not a copy, and the
/// <see cref="PieceStamp"/> of a plan detects every later change). Facts about the window (a drawing in progress, unapplied drafts,
/// a file operation, an incomplete recovery) are plain flags so that the planner stays free of any user interface.
/// </summary>
public sealed class DocumentView
{
    /// <summary>Creates a view of <paramref name="document"/> at the project epoch <paramref name="projectEpoch"/> (the window's project epoch Guid).</summary>
    public DocumentView(ProjectDocument document, Guid projectEpoch)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        ProjectEpoch = projectEpoch;
    }

    /// <summary>The live document, to be treated as read-only.</summary>
    public ProjectDocument Document { get; }

    /// <summary>Changes whenever the window loads or replaces the project; part of every stamp.</summary>
    public Guid ProjectEpoch { get; }

    /// <summary>A mouse drawing has started and is not finished (R07).</summary>
    public bool DrawingInProgress { get; init; }

    /// <summary>Points the pending drawing has so far (used by "çizimi kapat" and "çizimi açık bırak").</summary>
    public int DrawingPointCount { get; init; }

    /// <summary>Typed position or name boxes of the inspector hold changes that were not applied (R20).</summary>
    public bool HasUnappliedDrafts { get; init; }

    /// <summary>A recovery restore is incomplete (R21).</summary>
    public bool RecoveryIncomplete { get; init; }

    /// <summary>A file operation (save, export, import) is running (R09).</summary>
    public bool FileOperationInProgress { get; init; }

    /// <summary>Steps that can be undone.</summary>
    public int UndoCount { get; init; }

    /// <summary>Steps that can be redone.</summary>
    public int RedoCount { get; init; }

    /// <summary>Short description of the step "geri al" would undo (for example "25 mm sağa taşıma"), or null when unknown.</summary>
    public string? LastUndoLabel { get; init; }

    /// <summary>Layer that receives a new piece, already resolved the way the window does it (selected layer when visible and unlocked, else the default layer); null means the default layer.</summary>
    public Guid? NewPieceLayerId { get; init; }

    /// <summary>File name of the open project without folders, or null when it was never saved.</summary>
    public string? FileName { get; init; }

    /// <summary>The project has changes that were not saved.</summary>
    public bool HasUnsavedChanges { get; init; }
}
