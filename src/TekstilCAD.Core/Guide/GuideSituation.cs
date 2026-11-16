namespace TekstilCAD.Core.Guide;

/// <summary>What the waiting command preview asks of the person.</summary>
public enum GuidePreviewKind
{
    /// <summary>An ordinary preview: Enter or Uygula applies it, Esc drops it.</summary>
    Normal = 0,
    /// <summary>A dangerous preview (for example deleting): Enter only moves focus, a separate red button applies it.</summary>
    Danger = 1,
    /// <summary>A question with answer buttons and no Uygula (for example "milimetre mi, santimetre mi?").</summary>
    Question = 2,
}

/// <summary>
/// Plain facts about what the person is doing right now, collected by the window and handed to
/// <see cref="GuideRules.NextStep"/>. Every property is a fact the window already knows; nothing here is
/// derived from a clock, a file or the project content. A default instance is "a brand-new, empty project
/// that is not first launch, with the Select tool and nothing pending".
/// Contradictory input never throws: <see cref="GuideRules"/> documents how each contradiction is read.
/// </summary>
public sealed record GuideSituation
{
    /// <summary>Number of pieces in the project. A negative value is read as zero.</summary>
    public int PieceCount { get; init; }

    /// <summary>A piece is selected. Ignored while the project has no pieces.</summary>
    public bool HasSelectedPiece { get; init; }

    /// <summary>The project has changes that are not saved to its file.</summary>
    public bool HasUnsavedChanges { get; init; }

    /// <summary>The project already belongs to a file on disk (it was saved or opened).</summary>
    public bool FilePathKnown { get; init; }

    /// <summary>A recovery warning is showing in the header.</summary>
    public bool HasRecoveryNotice { get; init; }

    /// <summary>The project was just brought in from another program and the person has not checked it yet.</summary>
    public bool ImportedWithWarnings { get; init; }

    /// <summary>This is the first time the program is opened on this computer.</summary>
    public bool IsFirstLaunch { get; init; }

    /// <summary>Display name of the active tool, for example "Seç", "Çizgi", "Nokta nokta çiz", "Dikdörtgen", "Eğri", "Yay". Code identifiers ("Çoklu çizgi", "Bézier") are accepted too. Unknown or empty means no drawing tool.</summary>
    public string? ActiveToolName { get; init; }

    /// <summary>A drawing has been started on the canvas and is not finished yet.</summary>
    public bool HasPendingDrawing { get; init; }

    /// <summary>
    /// The selected piece is a plain closed rectangle that the command box can resize (no grain line, corner mark,
    /// grading rule or curve data). Only then does the guide suggest "genişliği 95 yap"; otherwise that
    /// command would be refused and the person would be sent in a circle. Ignored without a selection.
    /// </summary>
    public bool SelectedCanResize { get; init; }

    /// <summary>What kind of answer the waiting preview needs. Only read while <see cref="HasPendingPreview"/> is true.</summary>
    public GuidePreviewKind PendingPreviewKind { get; init; }

    /// <summary>A written command has been understood and is waiting for Enter, Uygula or Esc.</summary>
    public bool HasPendingPreview { get; init; }

    /// <summary>The one reading of "no pieces": <see cref="IsEmptyProject"/> or a non-positive <see cref="PieceCount"/>.</summary>
    public bool HasNoPieces => IsEmptyProject || PieceCount <= 0;

    /// <summary>The project has no pieces. When true the project is read as having no pieces whatever <see cref="PieceCount"/> says.</summary>
    public bool IsEmptyProject { get; init; }

    /// <summary>The built-in example was loaded into this project and the project has no file yet.</summary>
    public bool ExampleLoaded { get; init; }

    /// <summary>
    /// The person has already changed a size or moved a piece at least once (taken from
    /// <see cref="FirstStepsTracker.HasTriedEditing"/>). It lets the guide teach editing first and talk about
    /// saving afterwards.
    /// </summary>
    public bool HasTriedEditing { get; init; }
}
