namespace TekstilCAD.Core.Guide;

/// <summary>The one thing the side card tells the person to do next.</summary>
/// <param name="Id">Stable identifier of the rule that produced the step; safe to store, test and use as an automation hint.</param>
/// <param name="Sentence">Short Turkish sentence for the "Sıradaki adım" box, sentence case, ends with a period.</param>
/// <param name="SuggestedCommand">A written command the command box may be filled with (never applied by itself); null when none fits.</param>
public sealed record GuideStep(string Id, string Sentence, string? SuggestedCommand);

/// <summary>One row of the documented rule table, in evaluation order (1 = checked first).</summary>
public sealed record GuideRuleInfo(int Order, string Id, string Condition, string Sentence, string? SuggestedCommand);
