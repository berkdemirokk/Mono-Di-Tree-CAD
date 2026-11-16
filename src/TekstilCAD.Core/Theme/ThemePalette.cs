namespace TekstilCAD.Core.Theme;

/// <summary>
/// A complete set of interface colours: one "#RRGGBB" value per token name. The names are the properties of
/// <c>StudioTheme</c> and the <c>Studio.*</c> resource keys of the desktop application. Pure data, so it can be tested without WPF.
/// </summary>
public sealed record ThemePalette(string Name, IReadOnlyDictionary<string, string> Hex)
{
    private static readonly (string Token, string Dark, string Light)[] Table =
    [
        ("Background", "#101923", "#101923"),
        ("Surface", "#16222F", "#16222F"),
        ("Raised", "#1D2C3C", "#1D2C3C"),
        ("Border", "#304254", "#304254"),
        ("Accent", "#49D8BB", "#49D8BB"),
        ("Text", "#EDF4FA", "#EDF4FA"),
        ("Muted", "#9BADBE", "#9BADBE"),
        ("Warning", "#F5C377", "#F5C377"),
        ("Danger", "#FF9999", "#FF9999"),
        ("Selection", "#235C59", "#235C59"),
        ("BorderStrong", "#304254", "#304254"),
        ("BorderHover", "#637C91", "#637C91"),
        ("Input", "#101923", "#101923"),
        ("Placeholder", "#8A9DB0", "#8A9DB0"),
        ("OnAccent", "#101923", "#101923"),
        ("AccentHover", "#6FE6CF", "#6FE6CF"),
        ("AccentPressed", "#2FBFA3", "#2FBFA3"),
        ("AccentSoft", "#1B3B3A", "#1B3B3A"),
        ("PrimaryFill", "#176F65", "#176F65"),
        ("PrimaryBorder", "#3CB29F", "#3CB29F"),
        ("Success", "#7FDDA5", "#7FDDA5"),
        ("SuccessSoft", "#15332A", "#15332A"),
        ("WarningSoft", "#3A3022", "#3A3022"),
        ("DangerSoft", "#3C2327", "#3C2327"),
        ("SelectionBorder", "#41877F", "#41877F"),
        ("TextSelection", "#3E8D83", "#3E8D83"),
        ("HoverFill", "#2A3D50", "#2A3D50"),
        ("PressedFill", "#244D4A", "#244D4A"),
        ("RowAlt", "#192737", "#192737"),
        ("GridLine", "#253546", "#253546"),
        ("ScrollThumb", "#526779", "#526779"),
        ("ScrollThumbHover", "#7B98AB", "#7B98AB"),
        ("TooltipBg", "#1D2C3C", "#1D2C3C"),
        ("TooltipText", "#EDF4FA", "#EDF4FA"),
        ("Focus", "#49D8BB", "#49D8BB"),
        ("Canvas", "#0D1827", "#0D1827"),
        ("CanvasGrid", "#203047", "#203047"),
        ("CanvasAxis", "#43617B", "#43617B"),
        ("Piece", "#4DE1C1", "#4DE1C1"),
        ("PieceFill", "#163D40", "#163D40"),
        ("PieceSelected", "#FFCB70", "#FFCB70"),
        ("PieceSelectedFill", "#304947", "#304947"),
        ("PieceHover", "#7AF0D6", "#7AF0D6"),
        ("PieceHoverFill", "#1D4A4D", "#1D4A4D"),
        ("PieceLabel", "#B3D2D5", "#B3D2D5"),
        ("PieceLabelSelected", "#FFCB70", "#FFCB70"),
        ("Notch", "#FF8FAF", "#FF8FAF"),
        ("GrainOk", "#65CFFF", "#65CFFF"),
        ("GrainReview", "#FFC468", "#FFC468"),
        ("DrawingPreview", "#FFCB70", "#FFCB70"),
        ("Ghost", "#7FB2FF", "#7FB2FF"),
        ("GhostFill", "#16294A", "#16294A"),
        ("OnPrimary", "#EDF4FA", "#EDF4FA"),
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
