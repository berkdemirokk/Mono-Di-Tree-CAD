using System.Globalization;

namespace TekstilCAD.Core;

/// <summary>
/// Culture-independent millimetre input. A single comma or dot is a decimal separator,
/// never a grouping separator. Leading/trailing whitespace is ignored; internal whitespace
/// and mixed/grouped separators are rejected. ASCII scientific notation is supported.
/// </summary>
public static class MeasurementInput
{
    private const NumberStyles Styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    public static bool TryParseMillimeters(string? text, out double value)
    {
        value = 0;
        if (text is null) return false;
        var token = text.Trim();
        if (token.Length == 0) return false;

        var index = 0;
        if (token[index] is '+' or '-') index++;
        var digits = 0;
        var nonZeroMantissa = false;
        ReadDigits(token, ref index, ref digits, ref nonZeroMantissa);
        if (index < token.Length && token[index] is '.' or ',')
        {
            index++;
            ReadDigits(token, ref index, ref digits, ref nonZeroMantissa);
        }
        if (digits == 0) return false;
        if (index < token.Length && token[index] is 'e' or 'E')
        {
            index++;
            if (index < token.Length && token[index] is '+' or '-') index++;
            var exponentDigits = 0;
            var ignored = false;
            ReadDigits(token, ref index, ref exponentDigits, ref ignored);
            if (exponentDigits == 0) return false;
        }
        if (index != token.Length) return false;
        if (!double.TryParse(token.Replace(',', '.'), Styles, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed) || (parsed == 0 && nonZeroMantissa)) return false;
        value = parsed;
        return true;
    }

    /// <summary>Rejects values outside the caller's domain without clamping or changing their scale.</summary>
    public static bool TryParseMillimeters(string? text, double maximumAbsoluteValue, out double value)
    {
        if (!double.IsFinite(maximumAbsoluteValue) || maximumAbsoluteValue < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumAbsoluteValue));
        if (TryParseMillimeters(text, out var parsed) && Math.Abs(parsed) <= maximumAbsoluteValue)
        {
            value = parsed;
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>Uses a dot and round-trip precision regardless of the current UI culture.</summary>
    public static string Format(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void ReadDigits(string token, ref int index, ref int count, ref bool nonZero)
    {
        while (index < token.Length && token[index] is >= '0' and <= '9')
        {
            nonZero |= token[index] != '0';
            count++;
            index++;
        }
    }
}
