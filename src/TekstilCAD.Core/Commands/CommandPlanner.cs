namespace TekstilCAD.Core.Commands;

/// <summary>
/// Turns one parsed <see cref="Command"/>, a <see cref="DocumentView"/> and the <see cref="Selection"/> into a
/// <see cref="Commands.Plan"/> (one-line preview, dashed frame, stamp) or a <see cref="Refusal"/> with its R-code. Pure and
/// deterministic: no I/O, no clock, no culture dependence, no change to its inputs, and it never throws (an unexpected
/// failure becomes R28a). It calls the UI-free <see cref="PieceOperations"/> for the numbers, so the preview and the edit share them.
/// Grammar and sentences: tasarim/FINAL_COMMANDS.md.
/// </summary>
public static class CommandPlanner
{
    /// <summary>Plans <paramref name="command"/>. <paramref name="unitAssumed"/> is <see cref="ParseResult.UnitAssumed"/>: no unit was written, so millimetres were assumed.</summary>
    public static PlanResult Plan(Command command, DocumentView view, Selection? selection, bool unitAssumed = false)
        => Run(view, selection, unitAssumed, context => context.PlanCommand(command));

    /// <summary>Plans a parse result: a command is planned, a refusal or question is passed through unchanged.</summary>
    public static PlanResult Plan(ParseResult parsed, DocumentView view, Selection? selection)
    {
        if (parsed is null) return PlanResult.Refused(Refusals.Unrecognized(string.Empty));
        if (!parsed.IsCommand || parsed.Command is null)
            return PlanResult.Refused(parsed.Notice ?? Refusals.Unrecognized(string.Empty));
        return Plan(parsed.Command, view, selection, parsed.UnitAssumed);
    }

    /// <summary>
    /// Plans a copy of the selected piece, <paramref name="offsetXMm"/> to the right and <paramref name="offsetYMm"/> downward
    /// (default 20 mm each). The command box hands "çoğalt" to the duplicate window; this entry serves callers that want the
    /// pure copy (undo step included) with the same preview, frame and stamp.
    /// </summary>
    public static PlanResult PlanDuplicate(DocumentView view, Selection? selection,
        double offsetXMm = PieceOperations.DefaultDuplicateOffsetMm, double offsetYMm = PieceOperations.DefaultDuplicateOffsetMm)
        => Run(view, selection, false, context => context.PlanDuplicate(offsetXMm, offsetYMm));

    private static PlanResult Run(DocumentView view, Selection? selection, bool unitAssumed, Func<PlannerContext, PlanResult> body)
    {
        try
        {
            if (view is null) return PlanResult.Refused(Refusals.Failed("Proje bilgisi okunamadı"));
            return body(new PlannerContext(view, selection ?? Selection.None, unitAssumed));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return PlanResult.Refused(Refusals.Failed("Komut hesaplanamadı"));
        }
    }
}
