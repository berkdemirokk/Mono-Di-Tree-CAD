using System.Globalization;

namespace TekstilCAD.Core.Commands;

/// <summary>Number and name formatting for preview sentences. Turkish decimal comma, no thousands separator, never culture dependent.</summary>
internal static class CommandPlannerText
{
    private static readonly NumberFormatInfo Format = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "", NegativeSign = "-" };

    /// <summary>A number with up to six decimals and a comma: 2,5; 120; 0,125.</summary>
    internal static string N(double value)
    {
        if (!double.IsFinite(value)) return "?";
        var text = Math.Round(value, 6).ToString("0.######", Format);
        return text == "-0" ? "0" : text;
    }

    /// <summary>Round-trip text of a number, independent of the culture (for seeds and digests).</summary>
    internal static string Inv(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>"25 mm".</summary>
    internal static string Mm(double mm) => N(mm) + " mm";

    /// <summary>"25 mm (2,5 cm)".</summary>
    internal static string MmCm(double mm) => $"{N(mm)} mm ({N(mm / 10)} cm)";

    /// <summary>"120 × 80 mm (12 × 8 cm)".</summary>
    internal static string Size(double widthMm, double heightMm) =>
        $"{N(widthMm)} × {N(heightMm)} mm ({N(widthMm / 10)} × {N(heightMm / 10)} cm)";

    /// <summary>"120 × 80 mm" (millimetres only, for deletion and "already" sentences).</summary>
    internal static string SizeMm(double widthMm, double heightMm) => $"{N(widthMm)} × {N(heightMm)} mm";

    /// <summary>"soldan 20 mm, yukarıdan 20 mm".</summary>
    internal static string Position(double xMm, double yMm) => $"soldan {Mm(xMm)}, yukarıdan {Mm(yMm)}";

    /// <summary>A piece name in the quotes of the previews: ‘Parça 1’. Long names are shortened and control characters replaced.</summary>
    internal static string Quote(string? name)
    {
        name ??= "";
        var length = Math.Min(name.Length, 40);
        if (length < name.Length && length > 0 && char.IsHighSurrogate(name[length - 1])) length--;
        var chars = name[..length].Select(c => char.IsControl(c) ? '·' : c).ToArray();
        return "‘" + new string(chars) + (length < name.Length ? "…" : "") + "’";
    }

    /// <summary>A name in quotes without shortening (a new name must be shown in full before it is accepted); control characters are replaced.</summary>
    internal static string QuoteFull(string? name) =>
        "‘" + new string((name ?? "").Select(c => char.IsControl(c) ? '·' : c).ToArray()) + "’";

    /// <summary>The text with its first letter in lower case (tr-TR), for a label placed after a colon.</summary>
    internal static string LowerFirst(string text) =>
        text.Length == 0 || (text.Length > 1 && char.IsUpper(text[1])) ? text : char.ToLower(text[0], CultureInfo.GetCultureInfo("tr-TR")) + text[1..];

    /// <summary>Text for a one-line sentence: control characters and line breaks become spaces, runs of spaces collapse, long text is shortened.</summary>
    internal static string OneLine(string? text, int max = 60)
    {
        var clean = string.Join(" ", new string((text ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= max ? clean : clean[..max].TrimEnd() + "…";
    }

    /// <summary>The sentence appended to a preview whose number had no unit.</summary>
    internal const string UnitAssumedSentence = "Birim yazmadınız; milimetre sayıldı.";
}
