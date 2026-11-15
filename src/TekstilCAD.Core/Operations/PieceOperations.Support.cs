using System.Globalization;

namespace TekstilCAD.Core;

/// <summary>
/// UI-free edit operations on the pieces of a <see cref="ProjectDocument"/>, for a command executor.
/// Every operation is pure: it reads the document it is given, never changes it, and returns a
/// <see cref="PieceOperationResult"/> holding either an edited copy or a Turkish refusal. Nothing here throws for a
/// user mistake. To commit, call <c>session.Execute(result.ApplyTo, ProjectStorage.Validate)</c>; that is one undo step.
/// Coordinates are millimetres with Y growing downward (screen convention). A piece's position is the top-left corner
/// of its bounding box, as in the inspector.
/// </summary>
public static partial class PieceOperations
{
    /// <summary>Gap used by the default placement of new pieces, to the right of the rightmost piece.</summary>
    public const double DefaultGapMm = 20;
    /// <summary>Offset used by <see cref="Duplicate"/> when none is given (right and down).</summary>
    public const double DefaultDuplicateOffsetMm = 20;
    /// <summary>A side of a new rectangle or a new line must be strictly longer than this.</summary>
    public const double MinimumSideMm = 0.001;
    /// <summary>Longest side (width, height, line length) the command box accepts, in millimetres.</summary>
    public const double MaximumSideMm = 20_000;
    /// <summary>Longest name accepted by <see cref="Rename"/>, in UTF-16 units after trimming.</summary>
    public const int MaximumNameLength = 128;

    // Mirrors of ProjectStorage's limits (FileFormats depends on Core, not the other way round);
    // a test keeps them equal to ProjectStorage.MaximumPieces and friends.
    public const int MaximumPieces = 10_000;
    public const int MaximumPointsPerPiece = 10_000;
    public const int MaximumTotalPoints = 200_000;
    private const int MaximumStoredNameLength = 1024;

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // R04 / R13 / R24 / R10 / R11 of the command design, verbatim.
    internal const string SideRangeText = "Ölçü 1 ile 20000 mm arasında olmalı. Örnek: 120 x 80 dikdörtgen";
    internal const string AngleText = "Şimdilik yalnız 90, 180 ve 270 derece döndürülebilir. Örnek: 90 sağa döndür";
    internal const string EmptyNameText = "Yeni ad boş olamaz. Örnek: adı ön beden olsun";
    internal const string NotRectangleText = "Bu parça dikdörtgen değil; ölçüsü yazıyla değiştirilemez. Yeni ölçüde dikdörtgen çizmek için yazın: 95 x 80 dikdörtgen";
    internal const string ExtrasText = "Bu parçada iplik yönü, köşe işareti, serileme kuralı veya eğri bilgisi var; ölçüsü yazıyla değiştirilemez. Tüm araçlar bölümünden düzenleyin.";

    internal const string OutOfRangeText = "Sonuç çalışma alanının dışına taşıyor (her eksende en çok ±100.000.000 mm). Daha küçük bir ölçü veya konum girin.";

    /// <summary>Same as <see cref="PieceNameSearch.FindPiecesByName"/>.</summary>
    public static IReadOnlyList<PieceMatch> FindPiecesByName(ProjectDocument document, string? text)
        => PieceNameSearch.FindPiecesByName(document, text);

    /// <summary>Same as <see cref="PieceNameSearch.FindPiecesByNameFolded"/>.</summary>
    public static IReadOnlyList<PieceMatch> FindPiecesByNameFolded(ProjectDocument document, string? text)
        => PieceNameSearch.FindPiecesByNameFolded(document, text);

    private static string Mm(double value)
    {
        if (!double.IsFinite(value)) return "geçersiz değer";
        var text = value.ToString("#,0.######", Tr);
        return (text == "-0" ? "0" : text) + " mm";
    }

    /// <summary>Name for a message: shortened, controls replaced, wrapped in typographic quotes.</summary>
    private static string Quote(string? name)
    {
        name ??= "";
        var length = Math.Min(name.Length, 40);
        if (length < name.Length && length > 0 && char.IsHighSurrogate(name[length - 1])) length--;
        var chars = name[..length].Select(c => char.IsControl(c) ? '·' : c).ToArray();
        return "“" + new string(chars) + (length < name.Length ? "…" : "") + "”";
    }

    private static PieceOperationResult Fail(PieceOperationError code, string message, string? detail = null)
        => PieceOperationResult.Failure(code, message, detail);

    private static PieceOperationResult NotFound() => Fail(PieceOperationError.PieceNotFound,
        "Parça bulunamadı; silinmiş olabilir. Parçayı yeniden seçip komutu tekrar verin.");

    // Wording R08 of the command design (FINAL_COMMANDS.md), kept verbatim so the box and the operations say the same thing.
    private static PieceOperationResult Locked(PatternPiece piece) => Fail(PieceOperationError.LayerLocked,
        "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın.");

    private static PieceOperationResult InvalidNumber(string label) => Fail(PieceOperationError.InvalidValue,
        $"{label} geçerli bir sayı olmalı (örneğin 120); girilen değer kullanılamadı.");

    private static PieceOperationResult CurvesUnsupported(PatternPiece piece, string operation) => Fail(PieceOperationError.Unsupported,
        $"{Quote(piece.Name)} yay veya eğri içeriyor; {operation} eğrilerin kontrol noktalarını bozacağı için uygulanmadı. Eğri içeren parçalarda yalnızca taşıma ve çoğaltma desteklenir.");

    private static PieceOperationResult PrecisionFailure(PatternPiece piece, Exception inner) => Fail(PieceOperationError.Precision,
        $"{Quote(piece.Name)} üzerinde işlem sayısal hassasiyetle güvenle yapılamadı; hiçbir şey değişmedi. Daha küçük bir değer girin veya parçayı orijine yaklaştırın.",
        inner.Message);

    /// <summary>Finds the piece and refuses when its layer is locked. Returns null when the piece may be edited.</summary>
    private static PieceOperationResult? Locate(ProjectDocument document, Guid id, bool requireUnlocked, out int index, out PatternPiece piece, bool requirePoints = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        index = document.Pieces.FindIndex(p => p.Id == id);
        if (index < 0) { piece = null!; return NotFound(); }
        piece = document.Pieces[index];
        if (requirePoints && (piece.Points is null || piece.Points.Count == 0))
            return Fail(PieceOperationError.InvalidShape, $"{Quote(piece.Name)} parçasının noktaları eksik; işlem uygulanmadı.");
        return requireUnlocked && LayerTools.IsPieceLocked(document, piece) ? Locked(piece) : null;
    }

    /// <summary>Runs an operation body and turns numeric exceptions of the geometry layer into Turkish refusals.</summary>
    private static PieceOperationResult Guard(PatternPiece piece, Func<PieceOperationResult> body)
    {
        try { return body(); }
        catch (ArgumentOutOfRangeException ex) { return Fail(PieceOperationError.OutOfRange, OutOfRangeText, ex.Message); }
        catch (ArgumentException ex) { return PrecisionFailure(piece, ex); }
        catch (ArithmeticException ex) { return PrecisionFailure(piece, ex); }
        catch (InvalidDataException ex) { return PrecisionFailure(piece, ex); }
    }

    private static PieceOperationResult Replace(ProjectDocument source, int index, PatternPiece updated,
        GradingRuleSet? newRule, string summary, List<string> warnings)
    {
        var edited = source.Clone();
        edited.Pieces[index] = updated;
        if (newRule is not null)
        {
            var ruleIndex = edited.GradingRules.FindIndex(r => r.SourcePieceId == updated.Id);
            edited.GradingRules[ruleIndex] = newRule;
        }
        return PieceOperationResult.Success(source, edited, updated.Id, summary, warnings);
    }

    private const string StaleRuleWarning = "Kayıtlı serileme kuralı bu parçanın eski konturuna ait kaldı; serilemeden önce kuralı gözden geçirin.";

    private static bool InRange(Point2 p) => p.IsFinite && Math.Abs(p.X) <= Geometry.MaxCoordinate && Math.Abs(p.Y) <= Geometry.MaxCoordinate;

    /// <summary>A computed span matches the requested one within the library's 1e-9 relative representation budget.</summary>
    private static bool SpanMatches(double requested, double actual) =>
        Math.Abs(actual - requested) <= requested * 1e-9;

    private static bool TryBounds(PatternPiece piece, out Bounds2 bounds)
    {
        bounds = default;
        if (piece.Points is null || piece.Points.Count == 0) return false;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var p in piece.Points)
        {
            if (!p.IsFinite) return false;
            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
        }
        bounds = new Bounds2(minX, minY, maxX, maxY);
        return true;
    }

    // An eight-ULP bound is a representation check around the requested double, as in the inspector's apply path.
    // The displacement is rounded on the grid of the larger of the old and the new coordinate, so the tolerance must use that
    // magnitude, not the (possibly much smaller) target alone.
    private static bool TargetMatches(double actual, double requested, double previous)
    {
        if (actual == requested) return true;
        var scale = Math.Max(Math.Abs(requested), Math.Abs(previous));
        var ulp = Math.Max(Math.Abs(Math.BitIncrement(scale) - scale), Math.Abs(scale - Math.BitDecrement(scale)));
        return Math.Abs(actual - requested) <= 8 * ulp;
    }

    private static long TotalPoints(ProjectDocument document)
    {
        long total = 0;
        foreach (var piece in document.Pieces) total += piece.Points?.Count ?? 0;
        return total;
    }

    /// <summary>"Parça N" where N is the first number from Count+1 up that no existing name uses.</summary>
    private static string NextPieceName(ProjectDocument document)
    {
        var used = new HashSet<string>(document.Pieces.Select(p => p.Name), StringComparer.Ordinal);
        for (var n = document.Pieces.Count + 1; ; n++)
        {
            var candidate = $"Parça {n.ToString(CultureInfo.InvariantCulture)}";
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
