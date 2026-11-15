using System.Globalization;
using System.Text;

namespace TekstilCAD.Core.Commands;

/// <summary>
/// Culture-independent Turkish text helpers for the command box. Nothing here reads the current
/// culture: the dotted/dotless I rules (I→ı, İ→i) and the diacritic folding are written out explicitly.
/// Every mapping is one UTF-16 unit to one UTF-16 unit, so a folded string has the same length as the
/// string it was folded from and token offsets can be reused on the original text.
/// </summary>
public static class TurkishText
{
    /// <summary>Longest accepted input, in UTF-16 units, after leading and trailing white space is removed.</summary>
    public const int MaxInputLength = 200;

    /// <summary>Lower-cases with Turkish rules: I→ı and İ→i; every other letter uses invariant lower-casing.</summary>
    public static string ToLower(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++) chars[i] = LowerChar(text[i]);
        return new string(chars);
    }

    /// <summary>Upper-cases with Turkish rules: i→İ and ı→I; every other letter uses invariant upper-casing.</summary>
    public static string ToUpper(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            chars[i] = c switch
            {
                'i' => 'İ',
                'ı' => 'I',
                _ => char.ToUpperInvariant(c),
            };
        }
        return new string(chars);
    }

    /// <summary>
    /// Lower-cases (Turkish rules) and folds Turkish diacritics to ASCII so that "dikdörtgen", "dikdortgen" and
    /// "DİKDÖRTGEN" are equal: ç→c ğ→g ı→i ö→o ş→s ü→u â→a î→i û→u. The result has the same length as the input.
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++) chars[i] = FoldChar(text[i]);
        return new string(chars);
    }

    /// <summary>Lower-cases and folds one character (see <see cref="Fold"/>).</summary>
    public static char FoldChar(char c)
    {
        var l = LowerChar(c);
        return l switch
        {
            'ç' => 'c',
            'ğ' => 'g',
            'ı' => 'i',
            'ö' => 'o',
            'ş' => 's',
            'ü' => 'u',
            'â' or 'á' or 'à' or 'ä' => 'a',
            'î' or 'í' or 'ì' or 'ï' => 'i',
            'û' or 'ú' or 'ù' => 'u',
            'é' or 'è' or 'ê' or 'ë' => 'e',
            'ó' or 'ò' or 'ô' => 'o',
            _ => l,
        };
    }

    /// <summary>
    /// Cleans raw user input without changing letters: composes decomposed Turkish letters, turns typographic
    /// apostrophes into ', turns control characters and every kind of white space into one space, collapses runs
    /// of spaces, trims, and drops trailing '.', '!' and '?' (a lone "?" is kept; it asks for help).
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var composed = Compose(text);
        var sb = new StringBuilder(composed.Length);
        var pendingSpace = false;
        foreach (var raw in composed)
        {
            var c = raw;
            if (c is '’' or '‘' or '´' or '`' or 'ʼ' or '′') c = '\'';
            if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '​' || c == '﻿' || c == '­')
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
        }
        var result = sb.ToString();
        var end = result.Length;
        while (end > 1 && result[end - 1] is '.' or '!' or '?') end--;
        while (end > 1 && result[end - 1] == ' ') end--;
        return end == result.Length ? result : result.Substring(0, end);
    }

    /// <summary>
    /// Formats a number the way messages show it: Turkish decimal comma, no thousands separator and no
    /// trailing zeros (12.5 → "12,5", 120 → "120"). Independent of the current culture.
    /// </summary>
    public static string FormatNumber(decimal value)
    {
        var rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("0.######", CultureInfo.InvariantCulture);
        return text.Replace('.', ',');
    }

    /// <summary>Same as <see cref="FormatNumber(decimal)"/> for a double; non-finite values give "0".</summary>
    public static string FormatNumber(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1e15) return "0";
        return FormatNumber((decimal)value);
    }

    /// <summary>True for the ASCII digits 0-9 only (never other Unicode digits).</summary>
    public static bool IsDigit(char c) => c >= '0' && c <= '9';

    /// <summary>True when the folded text contains one of the words the product never uses in its messages.</summary>
    public static bool ContainsForbiddenWord(string? text)
    {
        var folded = Fold(text);
        return folded.Contains("yapay zeka", StringComparison.Ordinal)
            || folded.Contains("asistan", StringComparison.Ordinal)
            || folded.Contains("model", StringComparison.Ordinal);
    }

    private static char LowerChar(char c) => c switch
    {
        'I' => 'ı',
        'İ' => 'i',
        _ => char.ToLowerInvariant(c),
    };

    // Composes the few decomposed sequences a dictation or paste can produce (letter + combining mark).
    private static string Compose(string text)
    {
        var any = false;
        foreach (var c in text)
        {
            if (c is '̇' or '̈' or '̧' or '̆' or '̂') { any = true; break; }
        }
        if (!any) return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i + 1 < text.Length)
            {
                var mark = text[i + 1];
                var composed = Combine(c, mark);
                if (composed != '\0') { sb.Append(composed); i++; continue; }
            }
            if (c is '̇' or '̈' or '̧' or '̆' or '̂') continue; // stray combining mark
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static char Combine(char letter, char mark) => (letter, mark) switch
    {
        ('I', '̇') => 'İ',
        ('i', '̇') => 'i',
        ('o', '̈') => 'ö',
        ('O', '̈') => 'Ö',
        ('u', '̈') => 'ü',
        ('U', '̈') => 'Ü',
        ('c', '̧') => 'ç',
        ('C', '̧') => 'Ç',
        ('s', '̧') => 'ş',
        ('S', '̧') => 'Ş',
        ('g', '̆') => 'ğ',
        ('G', '̆') => 'Ğ',
        ('a', '̂') => 'â',
        ('A', '̂') => 'Â',
        ('i', '̂') => 'î',
        ('u', '̂') => 'û',
        _ => '\0',
    };
}
