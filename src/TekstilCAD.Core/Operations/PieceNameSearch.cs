using System.Globalization;
using System.Text;

namespace TekstilCAD.Core;

/// <summary>A piece found by name. The id is the only stable address; names can repeat.</summary>
public sealed record PieceMatch(Guid Id, string Name, int Index);

/// <summary>
/// Finds pieces by name for a command executor. Both searches return every match in document order,
/// so the caller can ask the user when there are several or none; nothing here picks one for them.
/// </summary>
public static class PieceNameSearch
{
    /// <summary>
    /// Same rule as the main window's piece search box: Turkish (tr-TR) culture, case-insensitive substring of the name
    /// (so "ı" pairs with "I" and "i" with "İ", while "i" does not match "I"). The query is trimmed as the box trims it.
    /// Two deliberate differences: a blank query matches nothing (the box shows everything), and the piece id is not searched
    /// (a command like "a taşı" must not match a hexadecimal id).
    /// </summary>
    public static IReadOnlyList<PieceMatch> FindPiecesByName(ProjectDocument document, string? text)
    {
        ArgumentNullException.ThrowIfNull(document);
        var query = text?.Trim() ?? "";
        if (query.Length == 0) return [];
        var compare = CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
        var matches = new List<PieceMatch>();
        for (var i = 0; i < document.Pieces.Count; i++)
        {
            var piece = document.Pieces[i];
            if (piece.Name is not null && compare.IndexOf(piece.Name, query, CompareOptions.IgnoreCase) >= 0)
                matches.Add(new PieceMatch(piece.Id, piece.Name, i));
        }
        return matches;
    }

    /// <summary>
    /// Accent-folded variant for typed or spoken text: "saga" finds "Sağa", "on beden" finds "Ön beden".
    /// Both sides go through <see cref="Fold"/> and are compared ordinally, so the result does not depend on the machine culture.
    /// Everything <see cref="FindPiecesByName"/> finds is not guaranteed to be found here and vice versa
    /// (folding also pairs "i" with "I"); callers that want the stricter result should search that way first.
    /// </summary>
    public static IReadOnlyList<PieceMatch> FindPiecesByNameFolded(ProjectDocument document, string? text)
    {
        ArgumentNullException.ThrowIfNull(document);
        var query = Fold((text ?? "").Trim());
        if (query.Length == 0) return [];
        var matches = new List<PieceMatch>();
        for (var i = 0; i < document.Pieces.Count; i++)
        {
            var piece = document.Pieces[i];
            if (piece.Name is not null && Fold(piece.Name).Contains(query, StringComparison.Ordinal))
                matches.Add(new PieceMatch(piece.Id, piece.Name, i));
        }
        return matches;
    }

    /// <summary>
    /// Folds text for accent-insensitive comparison: İ I ı i → i, Ö ö → o, Ç ç → c, Ğ ğ → g, Ş ş → s, Ü ü → u, other letters lower-cased,
    /// combining marks removed (so "ş" typed as s + U+0327 equals the single character). The result is meant for comparison only.
    /// </summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            // Dotless ı has no decomposition and 'İ' loses its dot above as a combining mark; both end as plain i.
            builder.Append(c is 'ı' or 'İ' ? 'i' : char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }
}
