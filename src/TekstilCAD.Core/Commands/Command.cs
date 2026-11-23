namespace TekstilCAD.Core.Commands;

/// <summary>
/// One understood command. Immutable. Lengths are millimetres; directions are SCREEN directions
/// (right is +, down is +, so "up" is a negative <c>DownMm</c>), which the executor maps to document axes.
/// Commands carry what the user wrote; they never know about the document (see CommandPlanner in a later step).
/// </summary>
public abstract record Command;

/// <summary>"120 x 80 dikdörtgen" (C01) and "kare 100" (C02, <see cref="IsSquare"/>).</summary>
public sealed record RectangleCommand(double WidthMm, double HeightMm, bool IsSquare = false) : Command;

public enum LineOrientation { Horizontal, Vertical }

/// <summary>"150 çizgi", "80 dikey çizgi" (C03, C04).</summary>
public sealed record LineCommand(double LengthMm, LineOrientation Orientation) : Command;

/// <summary>"25 sağa taşı", "20 sağa 10 yukarı" (C05, C06): move the selected piece by a distance.</summary>
public sealed record MoveByCommand(double RightMm, double DownMm) : Command;

/// <summary>"x 100 y 50 konumuna taşı" (C07): move the selected piece's top-left corner to a point.</summary>
public sealed record MoveToCommand(double XMm, double YMm) : Command;

public enum RotationDirection { Clockwise, CounterClockwise }

/// <summary>"döndür", "90 sağa döndür", "yarım tur" (C08-C11). <see cref="Degrees"/> is 90, 180 or 270.</summary>
public sealed record RotateCommand(int Degrees, RotationDirection Direction) : Command;

public enum MirrorAxis
{
    /// <summary>Flip left-right across the vertical centre line ("aynala", "sağ sol aynala").</summary>
    Vertical,
    /// <summary>Flip top-bottom across the horizontal centre line ("alt üst aynala", "yatay aynala").</summary>
    Horizontal,
}

/// <summary>"aynala", "alt üst aynala" (C12, C13).</summary>
public sealed record MirrorCommand(MirrorAxis Axis) : Command;

/// <summary>"genişliği 95 yap", "95 x 60 yap" (C14-C16). A null side stays unchanged.</summary>
public sealed record ResizeCommand(double? WidthMm, double? HeightMm) : Command;

/// <summary>"adı ön beden olsun" (C17). The name keeps the letters as typed (1-128 characters).</summary>
public sealed record RenameProjectCommand(string Name) : Command;
public sealed record RenameCommand(string Name) : Command;

public enum PieceTarget { Selected, Named, Next, Previous, None }

/// <summary>"sil" (selected piece) or "kolu sil" (by name) (C19, C20). A name is the typed query, lower-cased, without its object suffix.</summary>
public sealed record DeleteCommand(PieceTarget Target, string? Name = null) : Command;

/// <summary>"ön bedeni seç", "sonraki parçayı seç", "seçimi bırak" (C21-C23).</summary>
public sealed record SelectCommand(PieceTarget Target, string? Name = null) : Command;

/// <summary>"geri al", "3 adım geri" (C24, C25). Steps is 1-10.</summary>
public sealed record UndoCommand(int Steps = 1) : Command;

/// <summary>"yinele" (C26).</summary>
public sealed record RedoCommand : Command;

public enum ViewAction
{
    FitAll, FitSelected, ZoomIn, ZoomOut,
    PanLeft, PanRight, PanUp, PanDown,
    DetailedView, AllTools, TextLarger, TextSmaller, ThemeDark, ThemeLight,
}

/// <summary>"tümünü göster", "yakınlaştır", "görünümü sola kaydır", "ayrıntılı görünüm", "koyu tema" (C27-C31, C63, C66). Never changes the project.</summary>
public sealed record ViewCommand(ViewAction Action) : Command;

public enum DrawingTool { Select, Line, Polyline, Rectangle, Bezier, Arc }

/// <summary>"dikdörtgen aracı", "yay çiz" (C32-C37): pick a mouse tool.</summary>
public sealed record ToolCommand(DrawingTool Tool) : Command;

public enum FinishDrawingAction { Close, FinishOpen, Cancel, RemoveLastPoint }

/// <summary>"çizimi kapat", "çizimi iptal et", "son noktayı sil" (C38-C41).</summary>
public sealed record FinishDrawingCommand(FinishDrawingAction Action) : Command;

public enum FileAction { Save, SaveAs, SaveCopy, Open, New }

/// <summary>"kaydet", "farklı kaydet", "yedeğini al", "aç", "yeni" (C43-C47).</summary>
public sealed record FileCommand(FileAction Action) : Command;

public enum HandoffTarget
{
    ImportDxf, Export, ExportSelectedDxf, ExportSelectedA4,
    TextileTools, MachineCenter, Grainline, PieceMetadata, MirrorCopy, GroupMove,
    Duplicate, Layers, CoordinateDrawing, About, DesignRequest, RecoveryScan,
}

/// <summary>
/// A request that an existing window or flow does the work (C18, C42, C48-C60, C62, C64, C65). <see cref="Tab"/> is the
/// tool-window tab (1 grading, 2 seam allowance, 3 marker) and <see cref="Value"/> a number to prefill: millimetres for
/// seam allowance and fabric width, a count for piece metadata ("adet 2").
/// </summary>
public sealed record HandoffCommand(HandoffTarget Target, int? Tab = null, double? Value = null) : Command;

/// <summary>"yardım", "?" (C61).</summary>
public sealed record HelpCommand : Command;

/// <summary>"vazgeç", "iptal": closes the shown preview only; never undoes anything.</summary>
public sealed record CancelPreviewCommand : Command;
