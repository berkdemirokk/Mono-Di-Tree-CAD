namespace TekstilCAD.Core.Theme;

/// <summary>
/// The foreground/background token pairs that must reach WCAG 2 contrast in the light palette: 4.5:1 for text and
/// 3:1 for control edges and graphics that carry information. One list feeds the unit tests and the headless verification,
/// so a colour cannot drift silently. Decorative lines (dividers, grid, axes) are deliberately absent.
/// </summary>
public static class ThemeContrastRules
{
    public const double TextMinimum = 4.5;
    public const double GraphicMinimum = 3.0;

    public sealed record Rule(string Foreground, string Background, double Minimum);

    public static IReadOnlyList<Rule> All { get; } = Build();

    private static IReadOnlyList<Rule> Build()
    {
        var rules = new List<Rule>();
        void Text(string foreground, params string[] backgrounds) { foreach (var background in backgrounds) rules.Add(new Rule(foreground, background, TextMinimum)); }
        void Graphic(string foreground, params string[] backgrounds) { foreach (var background in backgrounds) rules.Add(new Rule(foreground, background, GraphicMinimum)); }

        Text("Text", "Surface", "Background", "Raised", "HoverFill", "PressedFill", "AccentSoft", "Selection", "TextSelection", "SuccessSoft", "WarningSoft", "DangerSoft", "RowAlt", "Input");
        Text("Muted", "Surface", "Background", "Raised", "Canvas", "AccentSoft", "HoverFill", "RowAlt");
        Text("Placeholder", "Input");
        Text("Accent", "Surface", "Background", "Raised", "AccentSoft", "Selection", "Canvas");
        Text("OnAccent", "Accent", "AccentHover", "AccentPressed", "Danger", "PrimaryFill");
        Text("OnPrimary", "PrimaryFill");
        Text("Background", "Accent");
        Text("Success", "Surface", "Background", "SuccessSoft");
        Text("Warning", "Surface", "Background", "WarningSoft", "Raised", "Canvas");
        Text("Danger", "Surface", "Background", "DangerSoft", "Raised", "Canvas");
        Text("TooltipText", "TooltipBg");
        Text("PieceLabel", "Canvas", "PieceFill", "PieceHoverFill");
        Text("PieceLabelSelected", "Canvas", "PieceSelectedFill");
        Text("Ghost", "Canvas", "GhostFill", "Surface");

        Graphic("BorderStrong", "Surface", "Background", "Raised", "Canvas", "HoverFill");
        Graphic("BorderHover", "Surface", "Background", "PressedFill");
        Graphic("Focus", "Surface", "Background", "Canvas", "Raised", "AccentSoft", "HoverFill");
        Graphic("SelectionBorder", "Surface", "Selection");
        Graphic("Piece", "Canvas", "PieceFill");
        Graphic("PieceSelected", "Canvas", "PieceSelectedFill");
        Graphic("PieceHover", "Canvas", "PieceHoverFill");
        Graphic("Notch", "Canvas");
        Graphic("GrainOk", "Canvas");
        Graphic("GrainReview", "Canvas");
        Graphic("DrawingPreview", "Canvas");
        Graphic("Ghost", "Canvas", "PieceFill", "PieceSelectedFill");
        Graphic("Danger", "Canvas");
        Graphic("ScrollThumb", "Background", "Surface");
        Graphic("ScrollThumbHover", "Background");
        return rules;
    }

    /// <summary>The rules a palette fails, as readable lines; empty when it meets all of them.</summary>
    public static IReadOnlyList<string> Failures(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return All
            .Select(rule => (rule, ratio: Contrast.Ratio(palette[rule.Foreground], palette[rule.Background])))
            .Where(item => item.ratio < item.rule.Minimum)
            .Select(item => $"{item.rule.Foreground} {palette[item.rule.Foreground]} on {item.rule.Background} {palette[item.rule.Background]} = {item.ratio:0.00} < {item.rule.Minimum:0.0}")
            .ToArray();
    }
}
