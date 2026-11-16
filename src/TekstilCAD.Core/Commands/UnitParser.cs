namespace TekstilCAD.Core.Commands;

/// <summary>Length units understood by the command parser, ordered from smallest to largest.</summary>
public enum LengthUnit
{
    Millimeter = 0,
    Centimeter = 1,
    Meter = 2,
}

/// <summary>
/// Recognises folded (lower-case, ASCII) Turkish unit words and compound measures.
/// Pure, deterministic, culture independent and never throws.
/// </summary>
public static class UnitParser
{
    private const int MaxWordLength = 24;

    private static readonly Dictionary<string, (LengthUnit Unit, bool HasSuffix)> Words = BuildWords();

    /// <summary>
    /// Folded unit word, optionally with a glued suffix, to unit. <paramref name="hasSuffix"/> tells that
    /// a suffix was glued (for example "santimlik"). The short stems mm, cm and m accept no glued suffix.
    /// </summary>
    public static bool TryParseUnitWord(string? foldedWord, out LengthUnit unit, out bool hasSuffix)
    {
        unit = default;
        hasSuffix = false;
        if (string.IsNullOrEmpty(foldedWord) || foldedWord.Length > MaxWordLength)
            return false;
        if (!Words.TryGetValue(foldedWord, out var entry))
            return false;
        unit = entry.Unit;
        hasSuffix = entry.HasSuffix;
        return true;
    }

    /// <summary>Millimeters in one unit: 1, 10 or 1000. An undefined enum value is treated as millimeters.</summary>
    public static decimal MillimetersPerUnit(LengthUnit unit) => unit switch
    {
        LengthUnit.Centimeter => 10m,
        LengthUnit.Meter => 1000m,
        _ => 1m,
    };

    /// <summary>
    /// value * factor. Callers range-check the result. Only when the decimal range itself would overflow
    /// (never for realistic input) the result is clamped to decimal.MaxValue / MinValue instead of throwing.
    /// </summary>
    public static decimal ToMillimeters(decimal value, LengthUnit unit)
    {
        try
        {
            return value * MillimetersPerUnit(unit);
        }
        catch (OverflowException)
        {
            return value < 0m ? decimal.MinValue : decimal.MaxValue;
        }
    }

    /// <summary>Short display name: "mm", "cm" or "m" (undefined values give "mm").</summary>
    public static string ShortName(LengthUnit unit) => unit switch
    {
        LengthUnit.Centimeter => "cm",
        LengthUnit.Meter => "m",
        _ => "mm",
    };

    /// <summary>
    /// Sums compound measures such as "1 santim 2 milim" (12 mm). Needs at least two parts, every part carries
    /// an explicit unit, units strictly descending in size (m, cm, mm) and no unit repeats; otherwise false.
    /// </summary>
    public static bool TryCombine(IReadOnlyList<(decimal Value, LengthUnit Unit)> parts, out decimal millimeters)
    {
        millimeters = 0m;
        try
        {
            if (parts is null || parts.Count < 2 || parts.Count > 3)
                return false;

            decimal sum = 0m;
            LengthUnit? previous = null;
            for (var i = 0; i < parts.Count; i++)
            {
                var (value, unit) = parts[i];
                if (!Enum.IsDefined(unit) || (previous is { } p && unit >= p))
                    return false;
                previous = unit;
                sum = checked(sum + checked(value * MillimetersPerUnit(unit)));
            }

            millimeters = sum;
            return true;
        }
        catch (Exception)
        {
            // Overflow or a misbehaving list implementation: never throw from the parser.
            millimeters = 0m;
            return false;
        }
    }

    private static Dictionary<string, (LengthUnit Unit, bool HasSuffix)> BuildWords()
    {
        var map = new Dictionary<string, (LengthUnit, bool)>(StringComparer.Ordinal)
        {
            ["mm"] = (LengthUnit.Millimeter, false),
            ["cm"] = (LengthUnit.Centimeter, false),
            ["m"] = (LengthUnit.Meter, false),
        };

        // Long stems only. Every stem has a front last vowel (i / e), so harmony gives e / ye / den / lik.
        // "dan" / "luk" / "a" / "ya" are NOT generated for them (back-vowel forms would be wrong Turkish).
        AddStem("milim", LengthUnit.Millimeter, endsWithVowel: false);
        AddStem("milimetre", LengthUnit.Millimeter, endsWithVowel: true);
        AddStem("santim", LengthUnit.Centimeter, endsWithVowel: false);
        AddStem("santimetre", LengthUnit.Centimeter, endsWithVowel: true);
        AddStem("metre", LengthUnit.Meter, endsWithVowel: true);
        return map;

        void AddStem(string stem, LengthUnit unit, bool endsWithVowel)
        {
            map[stem] = (unit, false);
            map[stem + "lik"] = (unit, true);
            map[stem + (endsWithVowel ? "ye" : "e")] = (unit, true);
            map[stem + "den"] = (unit, true);
        }
    }
}
