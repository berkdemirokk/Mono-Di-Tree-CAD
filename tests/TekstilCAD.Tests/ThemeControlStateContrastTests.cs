using TekstilCAD.Core.Theme;

namespace TekstilCAD.Tests;

public class ThemeControlStateContrastTests
{
    [Fact]
    public void EveryControlKindHasRulesForEveryState()
    {
        foreach (var control in ThemeContrastRules.Controls)
            foreach (var state in ThemeContrastRules.States)
                Assert.Contains(ThemeContrastRules.ControlStates, rule => rule.Control == control && rule.State == state);
    }

    [Fact]
    public void EveryControlStateHasATextOrEdgeRuleThatTheLightPaletteMeets()
    {
        Assert.Empty(ThemeContrastRules.Failures(ThemePalette.Light));
        foreach (var rule in ThemeContrastRules.ControlStates)
        {
            Assert.Contains(rule.Control, ThemeContrastRules.Controls);
            Assert.Contains(rule.State, ThemeContrastRules.States);
            Assert.True(Contrast.Ratio(ThemePalette.Light[rule.Foreground], ThemePalette.Light[rule.Background]) >= rule.Minimum, $"{rule.Control} {rule.State} {rule.Foreground} on {rule.Background}");
        }
    }

    [Fact]
    public void TheDestructiveButtonHasItsOwnHoverAndPressedFills()
    {
        Assert.Equal("#9B1C13", ThemePalette.Light["DangerHover"]);
        Assert.Equal("#7F1710", ThemePalette.Light["DangerPressed"]);
        Assert.Equal(8.17, Contrast.Ratio("#FFFFFF", ThemePalette.Light["DangerHover"]), 2);
        Assert.Equal(10.33, Contrast.Ratio("#FFFFFF", ThemePalette.Light["DangerPressed"]), 2);
        Assert.Contains(ThemeContrastRules.ControlStates, rule => rule.Control == "danger" && rule.State == "hover" && rule.Background == "DangerHover" && rule.Foreground == "OnAccent");
        Assert.Contains(ThemeContrastRules.ControlStates, rule => rule.Control == "danger" && rule.State == "pressed" && rule.Background == "DangerPressed" && rule.Foreground == "OnAccent");
    }

    [Fact]
    public void MutedTextOnThePressedFillReachesTheTextMinimum()
    {
        Assert.Contains(ThemeContrastRules.General, rule => rule.Foreground == "Muted" && rule.Background == "PressedFill" && rule.Minimum == ThemeContrastRules.TextMinimum);
        Assert.True(Contrast.Ratio(ThemePalette.Light["Muted"], ThemePalette.Light["PressedFill"]) >= 4.5);
    }

    [Fact]
    public void EveryTokenHasAContrastRuleOrANamedExemption()
    {
        Assert.Empty(ThemeContrastRules.UncoveredTokens(ThemePalette.Tokens));
        foreach (var (token, reason) in ThemeContrastRules.Exemptions)
        {
            Assert.Contains(token, ThemePalette.Tokens);
            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.DoesNotContain(ThemeContrastRules.All, rule => rule.Foreground == token || rule.Background == token);
        }
    }

    [Fact]
    public void ANewTokenWithoutARuleIsReported()
    {
        var uncovered = ThemeContrastRules.UncoveredTokens(ThemePalette.Tokens.Append("BrandNewToken"));
        Assert.Equal(["BrandNewToken"], uncovered);
    }

    [Fact]
    public void DisabledControlsAreExemptAndDimmed()
    {
        Assert.Equal(ThemeContrastRules.Controls, ThemeContrastRules.DisabledControls);
        Assert.Equal(0.45, ThemeContrastRules.DisabledOpacity);
        Assert.DoesNotContain("disabled", ThemeContrastRules.States);
    }
}
