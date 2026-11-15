namespace TekstilCAD.Core.Theme;

/// <summary>
/// The foreground/background token pairs that must reach WCAG 2 contrast in the light palette: 4.5:1 for text and
/// 3:1 for control edges and graphics that carry information. One table feeds the unit tests and the headless verification,
/// so a colour cannot drift silently, and every token must appear in a rule or in <see cref="Exemptions"/> with a reason,
/// so a token cannot be added without a contrast row.
/// Control rules are listed per control kind (primary, secondary, danger, ghost) and per state (normal, hover, pressed,
/// focused, selected); the disabled state is exempt from WCAG and listed in <see cref="DisabledControls"/>.
/// </summary>
public static class ThemeContrastRules
{
    public const double TextMinimum = 4.5;
    public const double GraphicMinimum = 3.0;

    /// <summary>A pair that must reach <see cref="Minimum"/>. <see cref="Control"/> and <see cref="State"/> name the control state the pair belongs to, or are null for general pairs.</summary>
    public sealed record Rule(string Foreground, string Background, double Minimum, string? Control = null, string? State = null);

    public static IReadOnlyList<string> Controls { get; } = ["primary", "secondary", "danger", "ghost"];

    /// <summary>The states that carry rules; "disabled" is exempt (see <see cref="DisabledControls"/>).</summary>
    public static IReadOnlyList<string> States { get; } = ["normal", "hover", "pressed", "focused", "selected"];

    /// <summary>Disabled controls are exempt from WCAG contrast; the stylesheet dims them to this opacity instead.</summary>
    public const double DisabledOpacity = 0.45;

    /// <summary>The control kinds whose disabled state is covered by the exemption.</summary>
    public static IReadOnlyList<string> DisabledControls => Controls;

    /// <summary>Tokens that carry no information and therefore have no contrast rule, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> Exemptions { get; } = new Dictionary<string, string>
    {
        ["Border"] = "decorative divider and panel outline; control edges use BorderStrong",
        ["CanvasGrid"] = "decorative grid",
        ["CanvasAxis"] = "decorative axes",
        ["GridLine"] = "decorative table lines",
    };

    public static IReadOnlyList<Rule> General { get; } = BuildGeneral();

    public static IReadOnlyList<Rule> ControlStates { get; } = BuildControlStates();

    /// <summary>Every rule: the general pairs followed by the control-state pairs.</summary>
    public static IReadOnlyList<Rule> All { get; } = [.. General, .. ControlStates];

    private static List<Rule> BuildGeneral()
    {
        var rules = new List<Rule>();
        void Text(string foreground, params string[] backgrounds) { foreach (var background in backgrounds) rules.Add(new Rule(foreground, background, TextMinimum)); }
        void Graphic(string foreground, params string[] backgrounds) { foreach (var background in backgrounds) rules.Add(new Rule(foreground, background, GraphicMinimum)); }

        Text("Text", "Surface", "Background", "Raised", "HoverFill", "PressedFill", "AccentSoft", "Selection", "TextSelection", "SuccessSoft", "WarningSoft", "DangerSoft", "RowAlt", "Input");
        Text("Muted", "Surface", "Background", "Raised", "Canvas", "AccentSoft", "HoverFill", "PressedFill", "RowAlt");
        Text("Placeholder", "Input");
        Text("Accent", "Surface", "Background", "Raised", "AccentSoft", "Selection", "Canvas");
        Text("OnAccent", "Accent", "AccentHover", "AccentPressed", "Danger", "DangerHover", "DangerPressed", "PrimaryFill");
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

    private static List<Rule> BuildControlStates()
    {
        var rules = new List<Rule>();
        void Text(string control, string state, string foreground, string background) => rules.Add(new Rule(foreground, background, TextMinimum, control, state));
        void Edge(string control, string state, string foreground, string background) => rules.Add(new Rule(foreground, background, GraphicMinimum, control, state));

        // Primary: accent-filled button (Kaydet, Uygula, the selected tool).
        Text("primary", "normal", "OnAccent", "Accent"); Text("primary", "normal", "OnPrimary", "PrimaryFill"); Edge("primary", "normal", "PrimaryBorder", "Surface");
        Text("primary", "hover", "OnAccent", "AccentHover"); Edge("primary", "hover", "AccentHover", "Surface");
        Text("primary", "pressed", "OnAccent", "AccentPressed"); Edge("primary", "pressed", "AccentPressed", "Surface");
        Edge("primary", "focused", "Focus", "Surface"); Edge("primary", "focused", "Focus", "Background");
        Text("primary", "selected", "OnAccent", "Accent");

        // Secondary: the default button (raised fill, strong edge).
        Text("secondary", "normal", "Text", "Raised"); Edge("secondary", "normal", "BorderStrong", "Surface"); Edge("secondary", "normal", "BorderStrong", "Background");
        Text("secondary", "hover", "Text", "HoverFill"); Text("secondary", "hover", "Muted", "HoverFill"); Edge("secondary", "hover", "BorderHover", "Surface");
        Text("secondary", "pressed", "Text", "PressedFill"); Text("secondary", "pressed", "Muted", "PressedFill"); Edge("secondary", "pressed", "BorderHover", "PressedFill");
        Text("secondary", "focused", "Text", "Raised"); Edge("secondary", "focused", "Focus", "Raised"); Edge("secondary", "focused", "Focus", "Surface");
        Text("secondary", "selected", "Text", "Selection"); Edge("secondary", "selected", "SelectionBorder", "Selection");

        // Danger: destructive confirmation (Evet, parçayı sil); has its own hover and pressed fills.
        Text("danger", "normal", "OnAccent", "Danger"); Edge("danger", "normal", "Danger", "Surface");
        Text("danger", "hover", "OnAccent", "DangerHover"); Edge("danger", "hover", "DangerHover", "Surface");
        Text("danger", "pressed", "OnAccent", "DangerPressed"); Edge("danger", "pressed", "DangerPressed", "Surface");
        Edge("danger", "focused", "Focus", "Surface"); Edge("danger", "focused", "Focus", "Background");
        Text("danger", "selected", "OnAccent", "Danger");

        // Ghost: quiet, transparent button on the panel or window surface.
        Text("ghost", "normal", "Text", "Surface"); Text("ghost", "normal", "Text", "Background"); Text("ghost", "normal", "Muted", "Surface");
        Text("ghost", "hover", "Text", "HoverFill"); Text("ghost", "hover", "Muted", "HoverFill");
        Text("ghost", "pressed", "Text", "PressedFill"); Text("ghost", "pressed", "Muted", "PressedFill");
        Edge("ghost", "focused", "Focus", "Surface"); Edge("ghost", "focused", "Focus", "Background");
        Text("ghost", "selected", "Text", "Selection"); Edge("ghost", "selected", "SelectionBorder", "Selection");
        return rules;
    }

    /// <summary>The tokens that appear in no rule and no exemption.</summary>
    public static IReadOnlyList<string> UncoveredTokens(IEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var covered = new HashSet<string>(All.SelectMany(rule => new[] { rule.Foreground, rule.Background }), StringComparer.Ordinal);
        covered.UnionWith(Exemptions.Keys);
        return tokens.Where(token => !covered.Contains(token)).ToArray();
    }

    /// <summary>The rules a palette fails, as readable lines; empty when it meets all of them.</summary>
    public static IReadOnlyList<string> Failures(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return All
            .Select(rule => (rule, ratio: Contrast.Ratio(palette[rule.Foreground], palette[rule.Background])))
            .Where(item => item.ratio < item.rule.Minimum)
            .Select(item => $"{item.rule.Control}{(item.rule.Control is null ? "" : " " + item.rule.State + ": ")}{item.rule.Foreground} {palette[item.rule.Foreground]} on {item.rule.Background} {palette[item.rule.Background]} = {item.ratio:0.00} < {item.rule.Minimum:0.0}")
            .ToArray();
    }
}
