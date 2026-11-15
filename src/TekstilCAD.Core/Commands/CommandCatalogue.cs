using System.Collections.Immutable;

namespace TekstilCAD.Core.Commands;

/// <summary>
/// Everything the "Tüm araçlar" page lists, as data (FINAL_SPEC g, FINAL_PLAN PR 9). Pure: no window, no document.
/// The page itself (a later change) turns each entry into a button; F entries fill the command box, D entries raise
/// the click of the classic button named by <see cref="CommandCatalogueEntry.ClassicButtonLabel"/>, X entries open the
/// detailed view. Every button the classic main window creates is either named by exactly one entry or listed in
/// <see cref="HeaderButtons"/>; a unit test reads <c>MainWindow.cs</c> to keep that true.
/// </summary>
public static partial class CommandCatalogue
{
    /// <summary>The eight groups, in page order.</summary>
    public static ImmutableArray<CommandCatalogueGroup> Groups { get; } = GroupList.All;

    /// <summary>All entries in page order: group by group, in the order the page shows them.</summary>
    public static ImmutableArray<CommandCatalogueEntry> Entries { get; } = CommandCatalogueData.Build();

    /// <summary>Classic buttons that have no page entry because they only appear with their notice.</summary>
    public static ImmutableArray<CommandCatalogueHeaderButton> HeaderButtons { get; } = CommandCatalogueData.BuildHeaderButtons();

    /// <summary>The entry with this id, or null.</summary>
    public static CommandCatalogueEntry? Find(string id) =>
        Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    /// <summary>Entries of one group, in page order.</summary>
    public static IReadOnlyList<CommandCatalogueEntry> EntriesIn(CommandCatalogueGroup group) =>
        Entries.Where(e => e.Group == group).ToArray();

    /// <summary>The entry standing for the classic button with this exact label (full name for shortened labels), or null.</summary>
    public static CommandCatalogueEntry? FindByClassicLabel(string label) =>
        Entries.FirstOrDefault(e => string.Equals(e.ClassicButtonLabel, label, StringComparison.Ordinal));

    /// <summary>Folded form used by <see cref="Search"/> (tr-TR lower case, ç ğ ı ö ş ü â → c g i o s u a).</summary>
    public static string Fold(string? text) => CommandCatalogueSearch.Fold(text);

    /// <summary>
    /// Entries matching <paramref name="query"/> in title, synonyms, description or examples, best first;
    /// ties keep page order. Accent- and case-insensitive. A blank query returns an empty list.
    /// </summary>
    public static IReadOnlyList<CommandCatalogueEntry> Search(string? query) => CommandCatalogueSearch.Search(Entries, query);

    internal static class GroupList
    {
        public static readonly CommandCatalogueGroup GStart = new("start", "Başlangıç ve dosya");
        public static readonly CommandCatalogueGroup GDraw = new("draw", "Çizim");
        public static readonly CommandCatalogueGroup GPiece = new("piece", "Parçayı değiştir");
        public static readonly CommandCatalogueGroup GSize = new("size", "Ölçü, beden ve pastal");
        public static readonly CommandCatalogueGroup GExchange = new("exchange", "Başka programlarla alışveriş");
        public static readonly CommandCatalogueGroup GView = new("view", "Görünüm");
        public static readonly CommandCatalogueGroup GLayer = new("layer", "Katman ve liste");
        public static readonly CommandCatalogueGroup GHelp = new("help", "Yardım");

        public static readonly ImmutableArray<CommandCatalogueGroup> All = [GStart, GDraw, GPiece, GSize, GExchange, GView, GLayer, GHelp];
    }
}
