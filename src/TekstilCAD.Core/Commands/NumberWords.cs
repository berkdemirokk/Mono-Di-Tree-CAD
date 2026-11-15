namespace TekstilCAD.Core.Commands;

/// <summary>Result of reading one Turkish number phrase.</summary>
/// <param name="Value">Exact decimal value that was read.</param>
/// <param name="WordCount">How many input words the number consumed (always at least 1).</param>
/// <param name="HasSuffix">True when the last consumed word carried a glued suffix such as "yuze" or "bese".</param>
/// <param name="TooLarge">True when the integer part is above <see cref="NumberWords.MaxValue"/>; the value is still consumed.</param>
public readonly record struct NumberWordMatch(decimal Value, int WordCount, bool HasSuffix, bool TooLarge);

/// <summary>
/// Reads Turkish number words. Input words must already be lower-cased and ASCII-folded
/// (for example "yuz", "alti", "dort", "kirk", "bucuk", "virgul"). Pure, deterministic,
/// culture independent, never throws, and does bounded work per call.
/// </summary>
public static class NumberWords
{
    /// <summary>Largest integer value that is accepted without <see cref="NumberWordMatch.TooLarge"/>.</summary>
    public const int MaxValue = 99999;

    private const int MaxWordLength = 16;
    private const int MaxFractionDigits = 6;

    private enum Kind : byte { Zero, Digit, Tens, Hundred, Thousand, Bucuk, Yarim, Virgul }

    private readonly record struct Token(Kind Kind, int Value, bool Suffixed);

    private static readonly Dictionary<string, Token> Vocabulary = BuildVocabulary();

    private static readonly decimal[] PowersOfTen = [1m, 10m, 100m, 1_000m, 10_000m, 100_000m, 1_000_000m];

    /// <summary>
    /// Is this folded word part of the number vocabulary: a number word, "bucuk", "yarim",
    /// "virgul", or a number word with an attached suffix ("yuze", "bese", "onluk")?
    /// </summary>
    public static bool IsNumberWord(string? foldedWord) => Resolve(foldedWord, out _);

    /// <summary>
    /// Reads the longest valid Turkish number that starts at <paramref name="start"/>.
    /// Returns false (and a default match) when no number starts there.
    /// </summary>
    public static bool TryRead(IReadOnlyList<string> words, int start, out NumberWordMatch match)
    {
        match = default;
        try
        {
            return TryReadCore(words, start, out match);
        }
        catch (Exception)
        {
            // Defensive: a misbehaving list implementation must never break the parser.
            match = default;
            return false;
        }
    }

    private static bool TryReadCore(IReadOnlyList<string>? words, int start, out NumberWordMatch match)
    {
        match = default;
        if (words is null || start < 0 || start >= words.Count || !ResolveAt(words, start, out var first))
            return false;

        switch (first.Kind)
        {
            case Kind.Bucuk:
            case Kind.Virgul:
                return false;
            case Kind.Yarim:
                match = new NumberWordMatch(0.5m, 1, first.Suffixed, false);
                return true;
        }

        decimal value;
        int end;
        bool suffixed;
        bool isZero = first.Kind == Kind.Zero;
        if (isZero)
        {
            value = 0m;
            end = start + 1;
            suffixed = first.Suffixed;
        }
        else
        {
            if (!TryReadInteger(words, start, out var integer, out end, out suffixed))
                return false;
            value = integer;
        }

        var tooLarge = value > MaxValue;

        if (!suffixed && ResolveAt(words, end, out var next))
        {
            if (next.Kind == Kind.Bucuk && !isZero)
            {
                value += 0.5m;
                end++;
                suffixed = next.Suffixed;
            }
            else if (next.Kind == Kind.Virgul
                && TryReadFraction(words, end + 1, out var fraction, out var fractionEnd, out var fractionSuffixed))
            {
                value += fraction;
                end = fractionEnd;
                suffixed = fractionSuffixed;
            }
        }

        match = new NumberWordMatch(value, end - start, suffixed, tooLarge);
        return true;
    }

    // Integer part 1..999999: [group "bin"] [group], or bare "bin" [group], or a lone group.
    private static bool TryReadInteger(IReadOnlyList<string> words, int start, out int value, out int end, out bool suffixed)
    {
        value = 0;
        end = start;
        suffixed = false;

        int thousands;
        int cursor;
        if (TryReadGroup(words, start, out var group, out var groupEnd, out var groupSuffixed))
        {
            if (!groupSuffixed && ResolveAt(words, groupEnd, out var bin) && bin.Kind == Kind.Thousand)
            {
                thousands = group;
                cursor = groupEnd + 1;
                suffixed = bin.Suffixed;
            }
            else
            {
                value = group;
                end = groupEnd;
                suffixed = groupSuffixed;
                return true;
            }
        }
        else if (ResolveAt(words, start, out var lone) && lone.Kind == Kind.Thousand)
        {
            thousands = 1;
            cursor = start + 1;
            suffixed = lone.Suffixed;
        }
        else
        {
            return false;
        }

        value = thousands * 1000;
        end = cursor;
        if (!suffixed && TryReadGroup(words, cursor, out var lower, out var lowerEnd, out var lowerSuffixed))
        {
            value += lower;
            end = lowerEnd;
            suffixed = lowerSuffixed;
        }

        return true;
    }

    // One below-1000 group: [digit "yuz" | "yuz"] [tens] [ones]; at least one part, value 1..999.
    // A suffixed word is always the last word of the group.
    private static bool TryReadGroup(IReadOnlyList<string> words, int start, out int value, out int end, out bool suffixed)
    {
        value = 0;
        suffixed = false;
        var cursor = start;

        if (ResolveAt(words, cursor, out var token))
        {
            if (token.Kind == Kind.Digit && !token.Suffixed
                && ResolveAt(words, cursor + 1, out var hundred) && hundred.Kind == Kind.Hundred)
            {
                value = token.Value * 100;
                cursor += 2;
                suffixed = hundred.Suffixed;
            }
            else if (token.Kind == Kind.Hundred)
            {
                value = 100;
                cursor++;
                suffixed = token.Suffixed;
            }
        }

        if (!suffixed && ResolveAt(words, cursor, out token) && token.Kind == Kind.Tens)
        {
            value += token.Value;
            cursor++;
            suffixed = token.Suffixed;
        }

        if (!suffixed && ResolveAt(words, cursor, out token) && token.Kind == Kind.Digit)
        {
            value += token.Value;
            cursor++;
            suffixed = token.Suffixed;
        }

        end = cursor;
        return cursor > start;
    }

    // Fraction after "virgul": zero or more "sifir", then at most one group. At most 6 digits in total.
    private static bool TryReadFraction(IReadOnlyList<string> words, int start, out decimal fraction, out int end, out bool suffixed)
    {
        fraction = 0m;
        end = start;
        suffixed = false;

        var cursor = start;
        var zeros = 0;
        while (ResolveAt(words, cursor, out var token) && token.Kind == Kind.Zero)
        {
            zeros++;
            cursor++;
            if (zeros > MaxFractionDigits)
                return false;
            if (token.Suffixed)
            {
                end = cursor;
                suffixed = true;
                return true;
            }
        }

        if (TryReadGroup(words, cursor, out var group, out var groupEnd, out var groupSuffixed))
        {
            var digits = DigitCount(group);
            var total = zeros + digits;
            if (total > MaxFractionDigits)
                return false;
            fraction = group / PowersOfTen[total];
            end = groupEnd;
            suffixed = groupSuffixed;
            return true;
        }

        if (zeros == 0)
            return false;

        end = cursor;
        return true;
    }

    private static int DigitCount(int value)
    {
        var count = 1;
        while (value >= 10)
        {
            value /= 10;
            count++;
        }
        return count;
    }

    private static bool ResolveAt(IReadOnlyList<string> words, int index, out Token token)
    {
        token = default;
        return index >= 0 && index < words.Count && Resolve(words[index], out token);
    }

    private static bool Resolve(string? word, out Token token)
    {
        token = default;
        if (string.IsNullOrEmpty(word) || word.Length > MaxWordLength)
            return false;
        return Vocabulary.TryGetValue(word, out token);
    }

    private static Dictionary<string, Token> BuildVocabulary()
    {
        var map = new Dictionary<string, Token>(StringComparer.Ordinal);

        // Back/Rounded drive vowel harmony of the last vowel of the ORIGINAL (unfolded) word;
        // Voiceless is true when the original word ends in a voiceless consonant (t, k, s, c(ç), p, h, f, ş).
        void Add(string stem, Kind kind, int value, bool back, bool rounded, bool voiceless, string? softStem = null)
        {
            map[stem] = new Token(kind, value, false);
            var suffixedToken = new Token(kind, value, true);
            var vowelEnd = IsVowel(stem[^1]);
            var dativeStem = softStem ?? stem;

            // Dative: -e / -a after consonants, -ye / -ya after vowels.
            var dative = vowelEnd ? (back ? "ya" : "ye") : (back ? "a" : "e");
            map[dativeStem + dative] = suffixedToken;
            if (softStem is not null && stem == "dort")
                map[dativeStem + (back ? "e" : "a")] = suffixedToken; // tolerant: "dorda" next to "dorde"

            // "-lik" family (four-way harmony, folded: lik / luk).
            map[stem + (rounded ? "luk" : "lik")] = suffixedToken;

            // Ablative: -den / -dan, or -ten / -tan after voiceless consonants (the voiced spelling is tolerated).
            var vowel = back ? "an" : "en";
            map[stem + "d" + vowel] = suffixedToken;
            if (voiceless)
                map[stem + "t" + vowel] = suffixedToken;
        }

        Add("sifir", Kind.Zero, 0, back: true, rounded: false, voiceless: false);

        Add("bir", Kind.Digit, 1, back: false, rounded: false, voiceless: false);
        Add("iki", Kind.Digit, 2, back: false, rounded: false, voiceless: false);
        Add("uc", Kind.Digit, 3, back: false, rounded: true, voiceless: true);
        Add("dort", Kind.Digit, 4, back: false, rounded: true, voiceless: true, softStem: "dord");
        Add("bes", Kind.Digit, 5, back: false, rounded: false, voiceless: true);
        Add("alti", Kind.Digit, 6, back: true, rounded: false, voiceless: false);
        Add("yedi", Kind.Digit, 7, back: false, rounded: false, voiceless: false);
        Add("sekiz", Kind.Digit, 8, back: false, rounded: false, voiceless: false);
        Add("dokuz", Kind.Digit, 9, back: true, rounded: true, voiceless: false);

        Add("on", Kind.Tens, 10, back: true, rounded: true, voiceless: false);
        Add("yirmi", Kind.Tens, 20, back: false, rounded: false, voiceless: false);
        Add("otuz", Kind.Tens, 30, back: true, rounded: true, voiceless: false);
        Add("kirk", Kind.Tens, 40, back: true, rounded: false, voiceless: true);
        Add("elli", Kind.Tens, 50, back: false, rounded: false, voiceless: false);
        Add("altmis", Kind.Tens, 60, back: true, rounded: false, voiceless: true);
        Add("yetmis", Kind.Tens, 70, back: false, rounded: false, voiceless: true);
        Add("seksen", Kind.Tens, 80, back: false, rounded: false, voiceless: false);
        Add("doksan", Kind.Tens, 90, back: true, rounded: false, voiceless: false);

        Add("yuz", Kind.Hundred, 100, back: false, rounded: true, voiceless: false);
        Add("bin", Kind.Thousand, 1000, back: false, rounded: false, voiceless: false);

        Add("bucuk", Kind.Bucuk, 0, back: true, rounded: true, voiceless: true, softStem: "bucug");
        Add("yarim", Kind.Yarim, 0, back: true, rounded: false, voiceless: false);

        map["virgul"] = new Token(Kind.Virgul, 0, false);
        return map;
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';
}
