namespace TekstilCAD.Core.Commands;

/// <summary>
/// Thrown by <see cref="Plan.Apply"/> when the target no longer has the content the plan was calculated from. Nothing was changed.
/// The executor reports it as a stale preview (R22), not as a failed commit.
/// </summary>
public sealed class PlanStaleException : InvalidOperationException
{
    public PlanStaleException() : base(Refusals.TemplatePreviewStale) { }
}
