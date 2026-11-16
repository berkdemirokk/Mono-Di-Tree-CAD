using TekstilCAD.Core.Theme;

namespace TekstilCAD.Tests;

public class ThemePaletteContrastTests
{
    [Fact]
    public void LightPaletteMeetsEveryContrastRule()
    {
        Assert.True(ThemeContrastRules.All.Count >= 80);
        Assert.Empty(ThemeContrastRules.Failures(ThemePalette.Light));
    }

    [Fact]
    public void EveryRuleNamesTokensOfThePalette()
    {
        foreach (var rule in ThemeContrastRules.All)
        {
            Assert.Contains(rule.Foreground, ThemePalette.Tokens);
            Assert.Contains(rule.Background, ThemePalette.Tokens);
            Assert.True(rule.Minimum is ThemeContrastRules.TextMinimum or ThemeContrastRules.GraphicMinimum);
        }
    }

    [Theory]
    [InlineData("Text", "Surface", 16.12)]
    [InlineData("Muted", "Surface", 6.56)]
    [InlineData("Accent", "Surface", 5.47)]
    [InlineData("OnAccent", "Accent", 5.47)]
    [InlineData("Warning", "Surface", 7.23)]
    [InlineData("Danger", "Surface", 6.57)]
    [InlineData("BorderStrong", "Surface", 4.05)]
    [InlineData("Piece", "Canvas", 5.28)]
    [InlineData("PieceSelected", "Canvas", 4.85)]
    [InlineData("ScrollThumb", "Background", 3.40)]
    public void LightPaletteReproducesTheRatiosOfTheDesign(string foreground, string background, double expected)
    {
        Assert.Equal(expected, Contrast.Ratio(ThemePalette.Light[foreground], ThemePalette.Light[background]), 2);
    }

    [Fact]
    public void DecorativeLinesAreNotRulesAndStayFaint()
    {
        foreach (var token in new[] { "Border", "CanvasGrid", "CanvasAxis", "GridLine" })
            Assert.DoesNotContain(ThemeContrastRules.All, rule => rule.Foreground == token);
        Assert.True(Contrast.Ratio(ThemePalette.Light["Border"], ThemePalette.Light["Surface"]) < 3);
    }

    [Fact]
    public void ControlEdgesUseTheStrongBorderNotTheDivider()
    {
        Assert.Contains(ThemeContrastRules.All, rule => rule.Foreground == "BorderStrong" && rule.Background == "Surface");
        Assert.NotEqual(ThemePalette.Light["Border"], ThemePalette.Light["BorderStrong"]);
    }

    [Fact]
    public void AFailingPaletteIsReported()
    {
        var values = ThemePalette.Light.Hex.ToDictionary(pair => pair.Key, pair => pair.Value);
        values["Muted"] = "#C0C8D0";
        var failures = ThemeContrastRules.Failures(new ThemePalette("Weak", values));
        Assert.NotEmpty(failures);
        Assert.All(failures, line => Assert.Contains("Muted", line));
    }
}
