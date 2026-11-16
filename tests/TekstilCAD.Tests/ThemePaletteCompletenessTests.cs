using System.Text.RegularExpressions;
using TekstilCAD.Core.Theme;

namespace TekstilCAD.Tests;

public class ThemePaletteCompletenessTests
{
    // The colours the desktop had before themes existed: the nine StudioTheme brushes, Studio.Selection, the 14 literals of
    // App.xaml and the 11 distinct literals of CadCanvas. The default palette must reproduce every one of them exactly.
    private static readonly (string Token, string Hex)[] LegacyDark =
    [
        ("Background", "#101923"), ("Surface", "#16222F"), ("Raised", "#1D2C3C"), ("Border", "#304254"), ("Accent", "#49D8BB"),
        ("Text", "#EDF4FA"), ("Muted", "#9BADBE"), ("Warning", "#F5C377"), ("Danger", "#FF9999"), ("Selection", "#235C59"),
        ("HoverFill", "#2A3D50"), ("PressedFill", "#244D4A"), ("PrimaryFill", "#176F65"), ("PrimaryBorder", "#3CB29F"),
        ("BorderHover", "#637C91"), ("SelectionBorder", "#41877F"), ("TextSelection", "#3E8D83"), ("RowAlt", "#192737"),
        ("GridLine", "#253546"), ("ScrollThumb", "#526779"), ("ScrollThumbHover", "#7B98AB"),
        ("Canvas", "#0D1827"), ("CanvasGrid", "#203047"), ("CanvasAxis", "#43617B"), ("Piece", "#4DE1C1"), ("PieceFill", "#163D40"),
        ("PieceSelected", "#FFCB70"), ("PieceSelectedFill", "#304947"), ("PieceLabel", "#B3D2D5"), ("Notch", "#FF8FAF"),
        ("GrainOk", "#65CFFF"), ("GrainReview", "#FFC468"), ("DrawingPreview", "#FFCB70"),
        // Controls whose borders, fills and text used existing tokens: the dark value equals the token they replace.
        ("BorderStrong", "#304254"), ("Input", "#101923"), ("OnAccent", "#101923"), ("TooltipBg", "#1D2C3C"), ("TooltipText", "#EDF4FA"),
        ("Focus", "#49D8BB"), ("OnPrimary", "#EDF4FA"),
    ];

    [Fact]
    public void BothPalettesHaveTheSameTokens()
    {
        Assert.Equal(ThemePalette.Dark.Hex.Keys.OrderBy(k => k, StringComparer.Ordinal), ThemePalette.Light.Hex.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(ThemePalette.Tokens.OrderBy(k => k, StringComparer.Ordinal), ThemePalette.Dark.Hex.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(ThemePalette.Tokens.Count, ThemePalette.Tokens.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryValueIsASixDigitHexColour()
    {
        var pattern = new Regex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant);
        foreach (var palette in new[] { ThemePalette.Dark, ThemePalette.Light })
            foreach (var (token, hex) in palette.Hex)
                Assert.True(pattern.IsMatch(hex), palette.Name + " " + token + " " + hex);
    }

    [Fact]
    public void DarkPaletteReproducesTheColoursBeforeThemes()
    {
        foreach (var (token, hex) in LegacyDark)
            Assert.Equal(hex, ThemePalette.Dark[token]);
    }

    [Fact]
    public void DefaultTokenSetCoversTheSpecifiedGroups()
    {
        // 10 existing + 25 interface + 17 canvas and preview tokens from the design, plus OnPrimary (text on the primary fill)
        // and DangerHover and DangerPressed (the destructive button's own states).
        Assert.Equal(55, ThemePalette.Tokens.Count);
        foreach (var token in new[] { "Background", "Selection", "Input", "Focus", "BorderStrong", "Canvas", "PieceHover", "Ghost", "GhostFill", "OnPrimary", "DangerHover", "DangerPressed" })
            Assert.Contains(token, ThemePalette.Tokens);
    }

    [Fact]
    public void ByNameFindsOnlyTheTwoPalettes()
    {
        Assert.Same(ThemePalette.Dark, ThemePalette.ByName("Dark"));
        Assert.Same(ThemePalette.Light, ThemePalette.ByName("Light"));
        Assert.Null(ThemePalette.ByName("dark"));
        Assert.Null(ThemePalette.ByName(null));
        Assert.Null(ThemePalette.ByName("Neon"));
    }

    [Fact]
    public void ContrastRatioMatchesTheWcagDefinition()
    {
        Assert.Equal(21.0, Contrast.Ratio("#000000", "#FFFFFF"), 6);
        Assert.Equal(1.0, Contrast.Ratio("#336699", "#336699"), 6);
        Assert.Equal(Contrast.Ratio("#16222E", "#FFFFFF"), Contrast.Ratio("#FFFFFF", "#16222E"), 9);
        Assert.Equal(16.12, Contrast.Ratio("#16222E", "#FFFFFF"), 2);
        Assert.Equal(5.47, Contrast.Ratio("#0F766E", "#FFFFFF"), 2);
        Assert.Throws<FormatException>(() => Contrast.Ratio("336699", "#FFFFFF"));
    }
}
