namespace TekstilCAD.Core.Theme;

/// <summary>
/// A complete set of interface colours: one "#RRGGBB" value per token name. The names are the properties of
/// <c>StudioTheme</c> and the <c>Studio.*</c> resource keys of the desktop application. Pure data, so it can be tested without WPF.
/// </summary>
public sealed record ThemePalette(string Name, IReadOnlyDictionary<string, string> Hex)
{
    private static readonly (string Token, string Dark, string Light)[] Table =
    [
        ("Background", "#101923", "#F3F6F9"),
        ("Surface", "#16222F", "#FFFFFF"),
        ("Raised", "#1D2C3C", "#E9EEF3"),
        ("Border", "#304254", "#D3DBE3"),
        ("Accent", "#49D8BB", "#0F766E"),
        ("Text", "#EDF4FA", "#16222E"),
        ("Muted", "#9BADBE", "#4F5F70"),
        ("Warning", "#F5C377", "#85470A"),
        ("Danger", "#FF9999", "#B42318"),
        ("Selection", "#235C59", "#DDF3F0"),
        ("BorderStrong", "#304254", "#6F8093"),
        ("BorderHover", "#637C91", "#5B6C7F"),
        ("Input", "#101923", "#FFFFFF"),
        ("Placeholder", "#8A9DB0", "#5A6A7B"),
        ("OnAccent", "#101923", "#FFFFFF"),
        ("AccentHover", "#6FE6CF", "#0B5F58"),
        ("AccentPressed", "#2FBFA3", "#084C47"),
        ("AccentSoft", "#1B3B3A", "#DDF3F0"),
        ("PrimaryFill", "#176F65", "#0F766E"),
        ("PrimaryBorder", "#3CB29F", "#0B5F58"),
        ("Success", "#7FDDA5", "#166534"),
        ("SuccessSoft", "#15332A", "#E1F5E7"),
        ("WarningSoft", "#3A3022", "#FFF1CF"),
        ("DangerSoft", "#3C2327", "#FDE7E4"),
        ("SelectionBorder", "#41877F", "#2F8F85"),
        ("TextSelection", "#3E8D83", "#9AD6CE"),
        ("HoverFill", "#2A3D50", "#DDE5EC"),
        ("PressedFill", "#244D4A", "#CCD7E1"),
        ("RowAlt", "#192737", "#F8FAFC"),
        ("GridLine", "#253546", "#E1E7ED"),
        ("ScrollThumb", "#526779", "#76879A"),
        ("ScrollThumbHover", "#7B98AB", "#5B6C7F"),
        ("TooltipBg", "#1D2C3C", "#1F2D3A"),
        ("TooltipText", "#EDF4FA", "#FFFFFF"),
        ("Focus", "#49D8BB", "#0F766E"),
        ("Canvas", "#0D1827", "#FAFBFC"),
        ("CanvasGrid", "#203047", "#E3E8EE"),
        ("CanvasAxis", "#43617B", "#B4C0CC"),
        ("Piece", "#4DE1C1", "#0F766E"),
        ("PieceFill", "#163D40", "#DFF2EF"),
        ("PieceSelected", "#FFCB70", "#B45309"),
        ("PieceSelectedFill", "#304947", "#FFEECB"),
        ("PieceHover", "#7AF0D6", "#0B5F58"),
        ("PieceHoverFill", "#1D4A4D", "#CBE9E4"),
        ("PieceLabel", "#B3D2D5", "#24424A"),
        ("PieceLabelSelected", "#FFCB70", "#7A3A00"),
        ("Notch", "#FF8FAF", "#BE185D"),
        ("GrainOk", "#65CFFF", "#0369A1"),
        ("GrainReview", "#FFC468", "#B45309"),
        ("DrawingPreview", "#FFCB70", "#B45309"),
        ("Ghost", "#7FB2FF", "#1D4ED8"),
        ("GhostFill", "#16294A", "#E4EEFC"),
        ("OnPrimary", "#EDF4FA", "#FFFFFF"),
    ];

    /// <summary>Every token name, in table order.</summary>
    public static IReadOnlyList<string> Tokens { get; } = Table.Select(row => row.Token).ToArray();

    /// <summary>The default palette. It reproduces the colours the desktop had before themes existed.</summary>
    public static ThemePalette Dark { get; } = new("Dark", Table.ToDictionary(row => row.Token, row => row.Dark));

    /// <summary>The light palette.</summary>
    public static ThemePalette Light { get; } = new("Light", Table.ToDictionary(row => row.Token, row => row.Light));

    /// <summary>The palette with this name ("Dark" or "Light"), or null.</summary>
    public static ThemePalette? ByName(string? name) =>
        string.Equals(name, Dark.Name, StringComparison.Ordinal) ? Dark :
        string.Equals(name, Light.Name, StringComparison.Ordinal) ? Light : null;

    /// <summary>The value of a token.</summary>
    public string this[string token] => Hex[token];
}
