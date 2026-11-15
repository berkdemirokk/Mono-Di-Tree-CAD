namespace TekstilCAD.Core.Guide;

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

    /// <summary>A written command has been understood and is waiting for Enter, Uygula or Esc.</summary>
    public bool HasPendingPreview { get; init; }

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
