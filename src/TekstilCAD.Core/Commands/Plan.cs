namespace TekstilCAD.Core.Commands;

/// <summary>What a plan will do. One kind per family of commands.</summary>
public enum PlanKind
{
    AddRectangle, AddLine,
    MovePiece, MovePieceTo, RotatePiece, MirrorPiece,
    ResizePiece, RenamePiece, DeletePiece, DuplicatePiece,
    SelectPiece,
    Undo, Redo,
    View, Tool, FinishDrawing,
    File, Window,
    Help, CancelPreview, RenameProject,
}

/// <summary>The approval class of FINAL_COMMANDS.md section 1, item 9.</summary>
public enum PlanClass
{
    /// <summary>G: changes the project; preview, Enter, one undo step.</summary>
    Geometry,
    /// <summary>D: dangerous or not reversible by itself (delete, discard a drawing, overwrite, export); asks with an explicit button.</summary>
    Dangerous,
    /// <summary>N: does not change the project (select, view, tool, help); preview and Enter.</summary>
    NoChange,
    /// <summary>F: a file or window flow that asks its own questions afterwards.</summary>
    Window,
}

/// <summary>
/// The outcome of planning one command: the exact one-line preview sentence, the dashed frame, the stamp and, for edits that
/// are pure in Core, the edit itself. A plan never changes anything until <see cref="Apply"/> runs. Instances come only from
/// <see cref="CommandPlanner"/>.
/// </summary>
public sealed class Plan
{
    private readonly ProjectDocument? source;
    private readonly ProjectDocument? edited;

    internal Plan(PlanKind kind, PlanClass planClass, Command command, string preview, PieceStamp stamp,
        ProjectDocument? source = null, ProjectDocument? edited = null)
    {
        Kind = kind;
        Class = planClass;
        Command = command;
        Preview = preview;
        Stamp = stamp;
        this.source = source;
        this.edited = edited;
    }

    /// <summary>The command this plan was made from.</summary>
    public Command Command { get; }

    public PlanKind Kind { get; }

    public PlanClass Class { get; }

    /// <summary>
    /// The one-line Turkish sentence shown before anything happens. It is built from the same numbers the edit will use,
    /// names the target piece, states a rotation direction and shows millimetres and centimetres together; when the text had no
    /// unit it ends with "Birim yazmadınız; milimetre sayıldı.".
    /// </summary>
    public string Preview { get; }

    /// <summary>
    /// Short permanent notes the result row must keep after the change (for example that the grainline and the grading rule
    /// travel with the piece, or that a grading rule no longer fits). Never empty text; may be an empty list.
    /// </summary>
    public IReadOnlyList<string> Caveats { get; internal init; } = Array.Empty<string>();

    /// <summary>The dashed frame for the canvas (red for a deletion); empty when nothing can be drawn.</summary>
    public GhostScene Ghost { get; internal init; } = GhostScene.Empty;

    /// <summary>What the plan was calculated from. Check it with <see cref="PieceStamp.IsCurrent"/> before applying.</summary>
    public PieceStamp Stamp { get; }

    /// <summary>Command texts the box offers next to the preview (for example "90 sola döndür", or the centimetre reading of a bare number).</summary>
    public IReadOnlyList<RefusalButton> Alternatives { get; internal init; } = Array.Empty<RefusalButton>();

    /// <summary>The sentence for the result row after the change was applied ("Dikdörtgen çizildi: 120 × 80 mm."); also the undo step name.</summary>
    public string ResultText { get; internal init; } = "";

    /// <summary>Noun used in refusal texts of the executor ("Taşıma", "Çizim").</summary>
    public string ActionName { get; internal init; } = "";

    /// <summary>The piece the plan acts on (or selects); null when it acts on no single piece.</summary>
    public Guid? TargetPieceId { get; internal init; }

    /// <summary>The piece an applied plan creates; select it afterwards. Null for plans that create nothing.</summary>
    public Guid? CreatedPieceId { get; internal init; }

    /// <summary>The target is the selected piece, so the executor must check the selection and its layer lock.</summary>
    public bool NeedsSelectedPiece { get; internal init; }

    /// <summary>The plan adds a piece, so the executor must check that a visible, unlocked layer can take it.</summary>
    public bool AddsPiece { get; internal init; }

    /// <summary>
    /// What to do for an edit that Core does not apply (move, rotate, mirror: the grainline and grading-rule transforms live in
    /// the Desktop project); null otherwise. The preview, ghost and stamp of such a plan are still complete.
    /// </summary>
    public PlanOperation? Operation { get; internal init; }

    /// <summary>For selecting plans: the selection after the plan ran; null when the selection is cleared. Meaningful when <see cref="Kind"/> is SelectPiece.</summary>
    public Guid? SelectionAfter => Kind == PlanKind.SelectPiece ? TargetPieceId : null;

    /// <summary>The plan asks for an explicit button before it runs (delete, discard a drawing, export): Enter only moves the focus to "Vazgeç".</summary>
    public bool RequiresConfirmation => Class == PlanClass.Dangerous;

    /// <summary>The preview is shown in the danger colour. Same rule as <see cref="RequiresConfirmation"/>.</summary>
    public bool Danger => Class == PlanClass.Dangerous;

    /// <summary>True for the plans the executor commits as one undo step through its guard ladder: a Core edit (<see cref="AppliesInCore"/>) or a Desktop edit (<see cref="Operation"/>). Select, undo, view, tool, drawing, file and window plans take other routes.</summary>
    public bool IsProjectEdit => Class == PlanClass.Geometry && (edited is not null || Operation is not null);

    /// <summary>True when <see cref="Apply"/> changes the project in Core (add, resize, rename, delete, duplicate).</summary>
    public bool AppliesInCore => edited is not null;

    /// <summary>
    /// Writes the planned edit into <paramref name="target"/>; shaped as the action of <c>DocumentSession.Execute</c>
    /// (<c>session.Execute(plan.Apply, ProjectStorage.Validate)</c> is one undo step). Refuses with
    /// <see cref="InvalidOperationException"/> when the target no longer has the content the plan was calculated from (<see cref="PlanStaleException"/>), and when
    /// the plan is not applied in Core (see <see cref="Operation"/>). The caller should check <see cref="Stamp"/> first.
    /// </summary>
    public void Apply(ProjectDocument target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (edited is null || source is null)
            throw new InvalidOperationException("Bu komut Core içinde uygulanmaz; masaüstü uygulayıcısı kendi dönüşümüyle uygular.");
        if (!DocumentSession.ContentEquals(source, target))
            throw new PlanStaleException();
        var fresh = edited.Clone();
        target.Name = fresh.Name;
        target.Pieces = fresh.Pieces;
        target.GradingRules = fresh.GradingRules;
    }

    /// <summary>A copy of the document the edit would produce, or null when the plan is not applied in Core. For tests and previews.</summary>
    public ProjectDocument? EditedDocument() => edited?.Clone();
}
