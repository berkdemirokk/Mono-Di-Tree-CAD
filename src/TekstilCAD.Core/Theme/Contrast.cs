using System.Globalization;

namespace TekstilCAD.Core.Theme;

/// <summary>WCAG 2 contrast arithmetic on "#RRGGBB" colours.</summary>
public static class Contrast
{
    /// <summary>Relative luminance of a "#RRGGBB" colour (WCAG 2).</summary>
    public static double Luminance(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != 7 || hex[0] != '#') throw new FormatException("Expected #RRGGBB: " + hex);
        static double Channel(string value)
        {
            var c = int.Parse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(hex.Substring(1, 2)) + 0.7152 * Channel(hex.Substring(3, 2)) + 0.0722 * Channel(hex.Substring(5, 2));
    }

    /// <summary>Contrast ratio of two colours, between 1 and 21; symmetric.</summary>
    public static double Ratio(string hexA, string hexB)
    {
        var a = Luminance(hexA);
        var b = Luminance(hexB);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
