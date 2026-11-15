using System.Globalization;

namespace TekstilCAD.Core.Commands;

public static partial class CommandParser
{
    private enum TokKind { Word, Number, Sep, Joiner, Junk }

    /// <summary>One lexical item. Offsets point into the normalised input; the folded text has the same length.</summary>
    private sealed class Tok
    {
        public TokKind Kind;
        /// <summary>Folded text of a word, the digits of a number as typed, or the suffix of a joiner.</summary>
        public string Text = string.Empty;
        public int Start;
        public int End;

        // Numbers only.
        public decimal Value;
        public bool Negative;
        public bool FromWords;
        public bool Bad;          // more than one separator, too many decimals, ...
        public bool Overflow;     // too many integer digits or a word number above 99999
        public bool Hazard;       // "1.500": one separator followed by exactly three digits
        public string HazardDecimal = string.Empty;
        public string HazardWhole = string.Empty;
        public LengthUnit? Unit;
        public int NumberEnd;     // end of the numeric part itself, before any unit word or suffix

        public bool IsWord(string text) => Kind == TokKind.Word && Text == text;
        public bool IsNumber => Kind == TokKind.Number;
    }

    private const int MaxIntegerDigits = 9;
    private const int MaxFractionDigits = 6;

    // Glued endings that may follow a number without an apostrophe ("100e 150").
    private static readonly HashSet<string> GluedNumberSuffixes = new(StringComparer.Ordinal)
    {
        "e", "a", "ye", "ya", "lik", "luk",
    };

    /// <summary>Splits the folded text into words, numbers and separators. Never throws.</summary>
    private static List<Tok> Lex(string norm, string folded)
    {
        var tokens = new List<Tok>();
        var n = folded.Length;
        var i = 0;
        while (i < n)
        {
            var c = folded[i];
            if (c == ' ') { i++; continue; }

            if (TurkishText.IsDigit(c)) { i = LexNumber(norm, folded, i, tokens, negative: false); continue; }

            if (char.IsLetter(c))
            {
                i = LexWord(norm, folded, i, tokens);
                continue;
            }

            switch (c)
            {
                case '×':
                case '*':
                    tokens.Add(new Tok { Kind = TokKind.Sep, Text = "x", Start = i, End = i + 1 });
                    i++;
                    break;
                case ',':
                    tokens.Add(new Tok { Kind = TokKind.Sep, Text = ",", Start = i, End = i + 1 });
                    i++;
                    break;
                case '-':
                case '−':
                case '–':
                    i = LexDash(norm, folded, i, tokens);
                    break;
                case '?':
                    if (n == 1) tokens.Add(new Tok { Kind = TokKind.Word, Text = "?", Start = i, End = i + 1 });
                    i++;
                    break;
                case '\'':
                case '.':
                case ';':
                case ':':
                case '(':
                case ')':
                case '"':
                case '“':
                case '”':
                case '„':
                case '!':
                case '+':
                case '«':
                case '»':
                    i++;
                    break;
                default:
                    tokens.Add(new Tok { Kind = TokKind.Junk, Text = c.ToString(), Start = i, End = i + 1 });
                    i++;
                    break;
            }
        }
        return tokens;
    }

    private static int LexDash(string norm, string folded, int i, List<Tok> tokens)
    {
        var n = folded.Length;
        var next = i + 1 < n ? folded[i + 1] : ' ';
        var prev = i > 0 ? folded[i - 1] : ' ';
        if (TurkishText.IsDigit(next))
        {
            if (TurkishText.IsDigit(prev))
            {
                // "120-80": a hyphen between two digits is neither a sign nor a separator we trust.
                tokens.Add(new Tok { Kind = TokKind.Junk, Text = "-", Start = i, End = i + 1 });
                return i + 1;
            }
            if (prev == ' ' || !char.IsLetter(prev))
                return LexNumber(norm, folded, i + 1, tokens, negative: true, signStart: i);
        }
        return i + 1; // a hyphen inside a word or before a letter is just white space
    }

    private static int LexWord(string norm, string folded, int i, List<Tok> tokens)
    {
        var n = folded.Length;
        var start = i;
        while (i < n && char.IsLetter(folded[i])) i++;
        var text = folded.Substring(start, i - start);

        // "a4" / "a3" paper sizes are one word.
        if ((text == "a") && i < n && TurkishText.IsDigit(folded[i]))
        {
            var j = i;
            while (j < n && TurkishText.IsDigit(folded[j])) j++;
            text = folded.Substring(start, j - start);
            i = j;
        }

        var previousIsNumber = tokens.Count > 0 && tokens[^1].Kind == TokKind.Number;
        tokens.Add(new Tok { Kind = TokKind.Word, Text = text, Start = start, End = i });

        // An apostrophe suffix ("100'e", "mm'lik", "bedeni'ni") is dropped; after a number or a unit word it
        // still marks the join between two measures, so it is kept as a joiner token.
        if (i + 1 < n && folded[i] == '\'' && char.IsLetter(folded[i + 1]))
        {
            var j = i + 1;
            while (j < n && char.IsLetter(folded[j])) j++;
            var suffix = folded.Substring(i + 1, j - i - 1);
            if (previousIsNumber || UnitParser.TryParseUnitWord(text, out _, out _))
                tokens.Add(new Tok { Kind = TokKind.Joiner, Text = suffix, Start = i, End = j });
            i = j;
        }
        return i;
    }

    private static int LexNumber(string norm, string folded, int i, List<Tok> tokens, bool negative, int signStart = -1)
    {
        var n = folded.Length;
        var start = i;
        var integerStart = i;
        while (i < n && TurkishText.IsDigit(folded[i])) i++;
        var integerDigits = i - integerStart;
        var separators = 0;
        var fractionDigits = 0;
        var lastFractionLength = 0;
        string? firstFraction = null;
        while (i + 1 < n && (folded[i] == ',' || folded[i] == '.') && TurkishText.IsDigit(folded[i + 1]))
        {
            separators++;
            var j = i + 1;
            while (j < n && TurkishText.IsDigit(folded[j])) j++;
            lastFractionLength = j - i - 1;
            firstFraction ??= folded.Substring(i + 1, lastFractionLength);
            fractionDigits += lastFractionLength;
            i = j;
        }

        var tok = new Tok
        {
            Kind = TokKind.Number,
            Start = signStart >= 0 ? signStart : start,
            End = i,
            NumberEnd = i,
            Negative = negative,
            Text = folded.Substring(start, i - start),
        };

        if (separators >= 2 || fractionDigits > MaxFractionDigits)
        {
            tok.Bad = true;
        }
        else if (integerDigits > MaxIntegerDigits)
        {
            tok.Overflow = true;
        }
        else
        {
            var wholeText = folded.Substring(integerStart, integerDigits);
            var normalized = separators == 0
                ? wholeText
                : wholeText + "." + firstFraction;
            if (decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                tok.Value = value;
                if (separators == 1 && lastFractionLength == 3 && !IsZeroDigits(wholeText))
                {
                    tok.Hazard = true;
                    var frac = firstFraction!.TrimEnd('0');
                    tok.HazardDecimal = wholeText + (frac.Length == 0 ? string.Empty : "," + frac);
                    tok.HazardWhole = wholeText + firstFraction;
                }
            }
            else
            {
                tok.Bad = true;
            }
        }
        tokens.Add(tok);

        // Scientific notation ("15e-1") is not supported: refuse it instead of reading a unit suffix.
        if (i < n && folded[i] == 'e')
        {
            var k = i + 1;
            if (k < n && (folded[k] == '-' || folded[k] == '+')) k++;
            if (k < n && TurkishText.IsDigit(folded[k]))
            {
                tokens.Add(new Tok { Kind = TokKind.Junk, Text = "e", Start = i, End = i + 1 });
                return i + 1;
            }
        }

        // Apostrophe suffix: "100'e", "100'lük".
        if (i + 1 < n && folded[i] == '\'' && char.IsLetter(folded[i + 1]))
        {
            var j = i + 1;
            while (j < n && char.IsLetter(folded[j])) j++;
            tokens.Add(new Tok { Kind = TokKind.Joiner, Text = folded.Substring(i + 1, j - i - 1), Start = i, End = j });
            return j;
        }

        // Letters glued to the number: "120x80", "12cm", "100e".
        if (i < n && char.IsLetter(folded[i]))
        {
            var j = i;
            while (j < n && char.IsLetter(folded[j])) j++;
            var run = folded.Substring(i, j - i);
            if (GluedNumberSuffixes.Contains(run))
            {
                tokens.Add(new Tok { Kind = TokKind.Joiner, Text = run, Start = i, End = j });
                return j;
            }
            // Leave the letters for the word lexer so "12cm" and "120x80" read as number + word.
            return i;
        }
        return i;
    }

    private static bool IsZeroDigits(string digits)
    {
        foreach (var d in digits) if (d != '0') return false;
        return true;
    }
}
