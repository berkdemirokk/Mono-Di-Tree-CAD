using System.Collections.Immutable;

namespace TekstilCAD.Core.Commands;

/// <summary>How a "Tüm araçlar" entry behaves when the user picks it (FINAL_SPEC g.2).</summary>
public enum CommandCatalogueKind
{
    /// <summary>F: fills the command box with an example sentence; nothing runs until the user confirms.</summary>
    Fill,
    /// <summary>D: opens the existing window or function by raising the click of the real classic button.</summary>
    Open,
    /// <summary>X: only reachable in the detailed view; the entry names the control to focus there.</summary>
    Detailed,
}

/// <summary>One of the eight groups of the "Tüm araçlar" page. Instances are shared and compared by value.</summary>
public sealed record CommandCatalogueGroup
{
    public CommandCatalogueGroup(string id, string title)
    {
        Id = CommandCatalogueEntry.Require(id, nameof(id));
        Title = CommandCatalogueEntry.Require(title, nameof(title));
    }

    /// <summary>Stable ASCII identifier, never shown.</summary>
    public string Id { get; }

    /// <summary>Turkish title shown on the page.</summary>
    public string Title { get; }
}

/// <summary>
/// One line of the "Tüm araçlar" page. Immutable. The data lives in <see cref="CommandCatalogue"/>;
/// no UI type is referenced here so the catalogue can be tested without a window.
/// </summary>
public sealed record CommandCatalogueEntry
{
    public CommandCatalogueEntry(
        string id,
        CommandCatalogueGroup group,
        string title,
        string description,
        CommandCatalogueKind kind,
        IEnumerable<string>? examples = null,
        IEnumerable<string>? synonyms = null,
        string? classicButtonLabel = null,
        string? classicAccessibleName = null,
        string? detailedControl = null)
    {
        Id = Require(id, nameof(id));
        Group = group ?? throw new ArgumentNullException(nameof(group));
        Title = Require(title, nameof(title));
        Description = Require(description, nameof(description));
        Kind = kind;
        Examples = Clean(examples, nameof(examples));
        Synonyms = Clean(synonyms, nameof(synonyms));
        ClassicButtonLabel = string.IsNullOrWhiteSpace(classicButtonLabel) ? null : classicButtonLabel;
        ClassicAccessibleName = string.IsNullOrWhiteSpace(classicAccessibleName) ? null : classicAccessibleName;
        DetailedControl = string.IsNullOrWhiteSpace(detailedControl) ? null : detailedControl;
        if (kind == CommandCatalogueKind.Fill && Examples.IsEmpty)
            throw new ArgumentException("A fill entry needs at least one example.", nameof(examples));
        if (kind == CommandCatalogueKind.Open && ClassicButtonLabel is null)
            throw new ArgumentException("An open entry must name the classic button it raises.", nameof(classicButtonLabel));
        if (kind == CommandCatalogueKind.Detailed && DetailedControl is null)
            throw new ArgumentException("A detailed-view entry must name the control to focus.", nameof(detailedControl));
    }

    /// <summary>Stable ASCII identifier, for example <c>piece.mirror</c>. Never shown; never reused.</summary>
    public string Id { get; }

    public CommandCatalogueGroup Group { get; }

    /// <summary>Plain-Turkish name a beginner understands without CAD words. Shown as the button text.</summary>
    public string Title { get; }

    /// <summary>One sentence saying what happens.</summary>
    public string Description { get; }

    public CommandCatalogueKind Kind { get; }

    /// <summary>Sentences the command box grammar (FINAL_COMMANDS.md) accepts. May be empty for D and X.</summary>
    public ImmutableArray<string> Examples { get; }

    /// <summary>Everyday words that should find this entry ("kopyala", "ayna", "ölçü" ...). Not shown.</summary>
    public ImmutableArray<string> Synonyms { get; }

    /// <summary>
    /// Exact text (<c>Button.Content</c> as passed to <c>AddButton</c>) of the classic main-window button this entry
    /// stands for; the full name where the window shows a shortened label via <c>CompactLabel</c>; the glyph
    /// for glyph-only buttons. Null when no classic button exists (typed commands, check boxes, list).
    /// </summary>
    public string? ClassicButtonLabel { get; }

    /// <summary>Automation name of the classic button where it differs from the content (glyph buttons only).</summary>
    public string? ClassicAccessibleName { get; }

    /// <summary>For <see cref="CommandCatalogueKind.Detailed"/>: the control in the detailed view to focus.</summary>
    public string? DetailedControl { get; }

    internal static string Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", name);
        return value.Trim();
    }

    private static ImmutableArray<string> Clean(IEnumerable<string>? values, string name)
    {
        if (values is null) return ImmutableArray<string>.Empty;
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var value in values) builder.Add(Require(value, name));
        return builder.ToImmutable();
    }
}

/// <summary>
/// A classic button that deliberately has no "Tüm araçlar" entry because it only exists while its notice is shown
/// (header notices, the red error strip, the open-cancel button). The classic window keeps them as they are.
/// </summary>
public sealed record CommandCatalogueHeaderButton(string ClassicButtonLabel, string Where, string Reason);
