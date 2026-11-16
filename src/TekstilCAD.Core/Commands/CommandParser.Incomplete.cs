namespace TekstilCAD.Core.Commands;

public static partial class CommandParser
{
    // ================================================================================================================
    // Two whole phrases in one line ("kaydet ve sil", "geri al yinele"): two jobs
    // ================================================================================================================

    private static bool TryMultiPhrase(State s)
    {
        var words = s.Raw;
        if (words.Count < 2 || words.Count > 12) return false;
        foreach (var t in words) if (t.Kind != TokKind.Word) return false;
        var texts = words.Select(t => t.Text).ToArray();
        // can[i] = the words from i to the end split into whole phrases (a lone "ve" between phrases is skipped); parts[i] = phrase count.
        var parts = new int[texts.Length + 1];
        Array.Fill(parts, -1);
        parts[texts.Length] = 0;
        for (var i = texts.Length - 1; i >= 0; i--)
        {
            if (texts[i] == "ve" && i + 1 < texts.Length && parts[i + 1] >= 0) { parts[i] = parts[i + 1]; continue; }
            for (var j = i + 1; j <= texts.Length; j++)
            {
                if (parts[j] < 0) continue;
                var key = string.Join(' ', texts, i, j - i);
                if (!Phrases.ContainsKey(key)) continue;
                parts[i] = parts[j] + 1;
                break;
            }
        }
        return parts[0] >= 2;
    }

    // ================================================================================================================
    // Plausible prefixes ("120 x", "dikdört", "20 sağ"): the refusal is kept but flagged IsIncomplete
    // ================================================================================================================

    private static readonly HashSet<string> IncompleteCodes = new(StringComparer.Ordinal)
    {
        Refusals.NotUnderstood, Refusals.NumberMissing, Refusals.TwoMeasuresNeeded, Refusals.EmptyName, Refusals.NamedPieceMove,
    };

    /// <summary>Endings tried after the typed text to see whether it could become a working command.</summary>
    private static readonly string[] Endings =
    {
        string.Empty, " 120 80", " 120 x 80", " 80", " 80 dikdörtgen", " 100 çizgi", " 100", " 10 sağa taşı", " 10 yukarı", " 5 aşağı",
        " santim yukarı", " santim sağa", " mm sağa", " milim sağa", " cm", " mm", " 50 konumuna taşı", " pastal", " payı",
        " 80 yap", " seç", " sil", " olsun", " yap", " tema", " 3 adım geri", " adım geri", " geri", " 60 yap",
        " 2 yap", " kaydır", " aracı", " 90 derece", " döndür", " beden büyüt", " 10 çizgi", " x 80 dikdörtgen", " çarpı seksen dikdörtgen",
        " dikdörtgen", " yukarı", " aşağı", " sağa", " konumuna taşı", " üst aynala", " aynala", " santim aşağı", "0 yukarı", "0 konumuna taşı", "0 çizgi", "0 sağa",
    };

    private static readonly Lazy<string[]> PrefixVocabulary = new(() =>
    {
        var all = new SortedSet<string>(KnownWords.Value, StringComparer.Ordinal);
        foreach (var unit in new[] { "mm", "milim", "milimetre", "cm", "santim", "santimetre", "metre", "tur", "bucuk", "yarim", "virgul",
            "sifir", "bir", "iki", "uc", "dort", "bes", "alti", "yedi", "sekiz", "dokuz", "on", "yirmi", "otuz", "kirk", "elli", "altmis",
            "yetmis", "seksen", "doksan", "yuz", "bin" }) all.Add(unit);
        foreach (var d in DidYouMeanByFolded.Keys) all.Add(d);
        return all.ToArray();
    });

    private static bool CouldGrowIntoCommand(ParseResult result, string text, ParserContext context)
    {
        if (result.Kind != ParseKind.Refusal || result.Code is null || !IncompleteCodes.Contains(result.Code)) return false;
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 100) return false;
        var norm = TurkishText.Normalize(trimmed);
        if (norm.Length == 0 || norm == "?") return false;

        var folded = TurkishText.Fold(norm);
        if (Phrases.Keys.Any(k => k.StartsWith(folded, StringComparison.Ordinal))) return true;

        // "adı olsun" already ends in its verb: the empty name is final, not unfinished typing.
        if (result.Code == Refusals.EmptyName && RenameVerbs.Contains(folded.Substring(folded.LastIndexOf(' ') + 1))) return false;

        var bases = new List<string> { norm };
        var last = norm[^1];
        if (last == '\'') bases.Add(norm + "e");
        else if (last is ',' or '.') bases.Add(norm + "5");

        // The last word may be the start of a longer word.
        var start = norm.Length;
        while (start > 0 && char.IsLetter(norm[start - 1])) start--;
        if (start < norm.Length)
        {
            var typed = TurkishText.Fold(norm.Substring(start));
            var count = 0;
            foreach (var word in PrefixVocabulary.Value)
            {
                if (word.Length <= typed.Length || !word.StartsWith(typed, StringComparison.Ordinal)) continue;
                bases.Add(norm.Substring(0, start) + word);
                if (++count >= 60) break;
            }
        }

        foreach (var b in bases)
        {
            foreach (var ending in Endings)
            {
                var attempt = ParseCore(b + ending, context);
                if (attempt.Kind != ParseKind.Refusal) return true;
            }
        }
        return false;
    }
}
