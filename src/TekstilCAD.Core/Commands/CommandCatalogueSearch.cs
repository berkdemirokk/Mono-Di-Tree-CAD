using System.Globalization;
using System.Text;

namespace TekstilCAD.Core.Commands;

/// <summary>
/// Turkish-aware text folding and ranking for the catalogue search. Same folding rules as FINAL_COMMANDS.md
/// section 1: tr-TR lower case (İ→i, I→ı), then ç ğ ı ö ş ü â are treated as c g i o s u a, quotes become an
/// apostrophe, repeated spaces collapse. Deterministic: equal scores keep catalogue order.
/// </summary>
public static class CommandCatalogueSearch
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Folded, lower-case, single-spaced form used for every comparison. Never null.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lower = text.Normalize(NormalizationForm.FormC).ToLower(Turkish);
        var sb = new StringBuilder(lower.Length);
        var space = false;
        foreach (var ch in lower)
        {
            var c = ch switch
            {
                'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u',
                'â' => 'a', 'î' => 'i', 'û' => 'u',
                '’' or '‘' or '´' or '`' => '\'',
                _ => ch,
            };
            if (c == '̇') continue; // stray combining dot of "i̇"
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Entries whose title, synonyms, description or examples match <paramref name="query"/>, best first.
    /// A blank query (or one with no letter or digit) returns nothing; use <see cref="CommandCatalogue.Entries"/> to list all.
    /// Every word of the query must be found somewhere in the entry.
    /// </summary>
    public static IReadOnlyList<CommandCatalogueEntry> Search(IReadOnlyList<CommandCatalogueEntry> entries, string? query)
    {
        var folded = Fold(query).Trim('.', '!', '?', ' ');
        if (folded.Length == 0 || !folded.Any(char.IsLetterOrDigit)) return Array.Empty<CommandCatalogueEntry>();
        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<(int Score, int Index, CommandCatalogueEntry Entry)>();
        for (var i = 0; i < entries.Count; i++)
        {
            var score = Score(entries[i], folded, words);
            if (score > 0) hits.Add((score, i, entries[i]));
        }
        hits.Sort(static (a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Index.CompareTo(b.Index));
        return hits.Select(h => h.Entry).ToArray();
    }

    private static int Score(CommandCatalogueEntry entry, string phrase, string[] words)
    {
        var title = Fold(entry.Title);
        var synonyms = entry.Synonyms.Select(Fold).ToArray();
        var examples = entry.Examples.Select(Fold).ToArray();
        var description = Fold(entry.Description);

        // Whole-phrase matches rank above any word-by-word match.
        var best = 0;
        if (title == phrase) best = 1000;
        else if (synonyms.Contains(phrase)) best = 950;
        else if (title.StartsWith(phrase, StringComparison.Ordinal)) best = 800;
        else if (synonyms.Any(s => s.StartsWith(phrase, StringComparison.Ordinal))) best = 760;
        else if (WordStart(title, phrase)) best = 700;
        else if (synonyms.Any(s => WordStart(s, phrase))) best = 660;
        else if (title.Contains(phrase, StringComparison.Ordinal)) best = 600;
        else if (synonyms.Any(s => s.Contains(phrase, StringComparison.Ordinal))) best = 560;
        if (best > 0) return best;

        var total = 0;
        foreach (var word in words)
        {
            var wordScore =
                WordStart(title, word) ? 70 :
                synonyms.Any(s => WordStart(s, word)) ? 66 :
                title.Contains(word, StringComparison.Ordinal) ? 60 :
                synonyms.Any(s => s.Contains(word, StringComparison.Ordinal)) ? 56 :
                examples.Any(e => e.Contains(word, StringComparison.Ordinal)) ? 30 :
                description.Contains(word, StringComparison.Ordinal) ? 20 : 0;
            if (wordScore == 0) return 0; // every word must be found
            total += wordScore;
        }
        // Whole phrase in an example or the description beats the loose word-by-word sum.
        if (examples.Any(e => e.Contains(phrase, StringComparison.Ordinal))) total += 300;
        else if (description.Contains(phrase, StringComparison.Ordinal)) total += 200;
        return Math.Min(total, 499);
    }

    private static bool WordStart(string text, string value)
    {
        var at = 0;
        while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0)
        {
            if (at == 0 || text[at - 1] == ' ') return true;
            at++;
        }
        return false;
    }
}
