using System.Text.Json;
using TekstilCAD.Core;

namespace TekstilCAD.Grading;

/// <summary>Per-point millimetre increments for one size step. Point order is significant.</summary>
public sealed class GradeRule
{
    public string Name { get; set; } = "Serileme kuralı";
    public string BaseSize { get; set; } = "M";
    public List<Point2> Increments { get; set; } = [];

    public string ToJson()
    {
        Validate();
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }
    public static GradeRule FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > 4_000_000) throw new InvalidDataException("Serileme kuralı çok büyük.");
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Serileme kuralı bir nesne olmalıdır.");
            ValidateProperties(root, "Name", "BaseSize", "Increments");
            var name = RequiredProperty(root, "Name");
            var baseSize = RequiredProperty(root, "BaseSize");
            var points = RequiredProperty(root, "Increments");
            if (name.ValueKind != JsonValueKind.String || baseSize.ValueKind != JsonValueKind.String || points.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Serileme kuralı alanları geçersiz.");
            if (points.GetArrayLength() is < 2 or > GradingEngine.MaximumGradePoints)
                throw new InvalidDataException("Serileme kuralı nokta sayısı geçersiz.");
            foreach (var point in points.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Her serileme artımı X ve Y sayıları içermelidir.");
                ValidateProperties(point, "X", "Y", "IsFinite");
                ValidateJsonCoordinate(RequiredProperty(point, "X"));
                ValidateJsonCoordinate(RequiredProperty(point, "Y"));
                if (point.TryGetProperty("IsFinite", out var finite) && finite.ValueKind != JsonValueKind.True)
                    throw new InvalidDataException("Serileme artımının geçerlilik bilgisi tutarsız.");
            }
        }
        var rule = JsonSerializer.Deserialize<GradeRule>(json) ?? throw new InvalidDataException("Serileme kuralı boş.");
        rule.Validate();
        return rule;
    }
    private static void ValidateProperties(JsonElement obj, params string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new InvalidDataException($"Serileme kuralında bilinmeyen veya yinelenen alan: {property.Name}.");
    }
    private static void ValidateJsonCoordinate(JsonElement coordinate)
    {
        if (coordinate.ValueKind != JsonValueKind.Number || !coordinate.TryGetDouble(out var value) || !double.IsFinite(value))
            throw new InvalidDataException("Serileme artımı sonlu bir sayı olmalıdır.");
        if (value != 0) return;
        // A nonzero JSON mantissa that parses to zero lost the whole increment.
        foreach (var c in coordinate.GetRawText())
        {
            if (c is 'e' or 'E') break;
            if (c is >= '1' and <= '9')
                throw new InvalidDataException("Serileme artımı sıfıra yuvarlanacak kadar küçüktür.");
        }
    }
    private static JsonElement RequiredProperty(JsonElement obj, string name)
    {
        JsonElement value = default;
        var found = false;
        foreach (var property in obj.EnumerateObject())
        {
            if (property.Name != name) continue;
            if (found) throw new InvalidDataException($"Serileme kuralında yinelenen alan: {name}.");
            found = true;
            value = property.Value;
        }
        if (!found) throw new InvalidDataException($"Serileme kuralında eksik alan: {name}.");
        return value;
    }
    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 1024 || !GradeText.IsWellFormed(Name) || string.IsNullOrWhiteSpace(BaseSize) || BaseSize.Length > 128 || !GradeText.IsWellFormed(BaseSize) || Increments is null || Increments.Count is < 2 or > GradingEngine.MaximumGradePoints)
            throw new InvalidDataException("Serileme kuralı adı, temel beden veya nokta listesi geçersiz.");
        if (Increments.Any(p => !p.IsFinite || Math.Abs(p.X) > 1e8 || Math.Abs(p.Y) > 1e8))
            throw new InvalidDataException("Serileme artımları sonlu ve izin verilen aralıkta olmalıdır.");
    }
}

public sealed record GradeSize(string Name, double Steps);

internal static class GradeText
{
    internal static bool IsWellFormed(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i])) return false;
            }
            else if (char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }
}

public static class GradingEngine
{
    // Two quadratic contour validations are bounded by the same 50 million work budget.
    public const int MaximumGradePoints = 7071;
    public const long MaximumBatchValidationWork = 50_000_000;
    /// <summary>Create an independent rule for a generated piece, preserving every coordinate bit.</summary>
    public static GradingRuleSet CreateDerivedRule(PatternPiece generated, IReadOnlyList<Point2> increments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(increments);
        if (generated.Id == Guid.Empty || generated.Points is null || generated.Points.Count != increments.Count ||
            increments.Count < (generated.Closed ? 3 : 2) || increments.Count > MaximumGradePoints)
            throw new ArgumentException("Yeni beden ve artışların nokta sayıları uyuşmuyor.");
        static bool SameBits(Point2 a, Point2 b) => BitConverter.DoubleToInt64Bits(a.X) == BitConverter.DoubleToInt64Bits(b.X) &&
            BitConverter.DoubleToInt64Bits(a.Y) == BitConverter.DoubleToInt64Bits(b.Y);
        if (generated.Closed && generated.Points[0] == generated.Points[^1] &&
            (!SameBits(generated.Points[0], generated.Points[^1]) || !SameBits(increments[0], increments[^1])))
            throw new ArgumentException("Yinelenen kapanış noktasının koordinatları ve artışları bit düzeyinde eşleşmelidir.");
        var rule = new GradingRuleSet { SourcePieceId = generated.Id, SourceClosed = generated.Closed };
        for (var i = 0; i < increments.Count; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            ValidatePoint(generated.Points[i]);
            ValidatePoint(increments[i]);
            rule.SourcePoints.Add(generated.Points[i]);
            rule.Deltas.Add(increments[i]);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return rule;
    }

    /// <summary>Generate a new size without mutating the base contour. A rule is matched by point index;
    /// visual vertex marks retain that index and follow its graded point.</summary>
    public static PatternPiece Generate(PatternPiece basePiece, IReadOnlyList<Point2> increments, double steps, string sizeName)
        => Generate(basePiece, increments, steps, sizeName, CancellationToken.None);

    /// <summary>Generate a size while cooperatively observing cancellation, including contour validation.</summary>
    public static PatternPiece Generate(PatternPiece basePiece, IReadOnlyList<Point2> increments, double steps, string sizeName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(basePiece);
        var grainline = CopyGrainlineForReview(basePiece);
        ArgumentNullException.ThrowIfNull(increments);
        if (string.IsNullOrWhiteSpace(sizeName) || sizeName.Trim().Length > 128) throw new ArgumentException("Beden adı gerekli ve en fazla 128 karakter olmalıdır.", nameof(sizeName));
        if (!GradeText.IsWellFormed(sizeName)) throw new ArgumentException("Beden adı eksik Unicode karakteri içeremez.", nameof(sizeName));
        if (basePiece.Name is null || basePiece.Name.Length + 3 + sizeName.Trim().Length > 1024)
            throw new ArgumentException("Oluşturulan parça adı 1024 karakter sınırını aşamaz.", nameof(basePiece));
        if (!GradeText.IsWellFormed(basePiece.Name)) throw new ArgumentException("Kaynak parça adı eksik Unicode karakteri içeremez.", nameof(basePiece));
        if (!double.IsFinite(steps)) throw new ArgumentOutOfRangeException(nameof(steps));
        if (basePiece.Points is null || basePiece.Points.Count != increments.Count)
            throw new ArgumentException("Her temel kalıp noktası için tam bir X/Y kuralı gereklidir.", nameof(increments));
        ValidateContour(basePiece.Points, basePiece.Closed, cancellationToken);
        // Validate and snapshot all visual marks before generating any output.
        // This rejects stale, duplicate, empty, or repeated-closure aliases.
        var sourceMarks = basePiece.Notches is null ? null : MarkedPieceTranslation.Translate(basePiece, 0, 0, cancellationToken).Notches;
        // An explicit closing vertex represents the first vertex, not another
        // independent point. Different rules would silently split that vertex.
        if (basePiece.Closed && basePiece.Points[0] == basePiece.Points[^1] && increments[0] != increments[^1])
            throw new ArgumentException("Yinelenen kapanış noktası için ilk ve son artış aynı olmalıdır.", nameof(increments));
        var result = new List<Point2>(increments.Count);
        for (var i = 0; i < increments.Count; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var increment = increments[i];
            ValidatePoint(increment);
            var source = basePiece.Points[i];
            var point = new Point2(GradeCoordinate(source.X, increment.X, steps, i, "X"),
                                   GradeCoordinate(source.Y, increment.Y, steps, i, "Y"));
            ValidatePoint(point);
            result.Add(point);
        }
        // A newly coincident endpoint is a collapsed edge, not an explicit
        // closing vertex from the source contour.
        if (basePiece.Closed && basePiece.Points[0] != basePiece.Points[^1] && result[0] == result[^1])
            throw new InvalidDataException("Serileme kapanış kenarını sıfıra indirdi; kuralları kontrol edin.");
        // A uniform rule is a translation. Checking each absolute displacement
        // alone can miss substantial distortion of a much shorter edge.
        var uniform=true;
        for(var i=1;i<increments.Count;i++) { if((i & 255)==0)cancellationToken.ThrowIfCancellationRequested();if(increments[i]!=increments[0]) { uniform=false;break; } }
        if (uniform)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var translated = Geometry.Translate(basePiece.Points, increments[0].X * steps, increments[0].Y * steps,cancellationToken);
                for(var i=0;i<translated.Count;i++)
                {
                    if((i & 255)==0)cancellationToken.ThrowIfCancellationRequested();
                    if(translated[i]!=result[i])throw new InvalidDataException("Serileme konturu güvenle taşıyamadı.");
                }
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException("Serileme sırasında kontur şekli sayısal hassasiyetle korunamıyor.", error);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (grainline is not null && uniform)
        {
            var delta = increments[0];
            Point2 Move(Point2 p) => new(GradeCoordinate(p.X, delta.X, steps, 0, "X"),
                                        GradeCoordinate(p.Y, delta.Y, steps, 0, "Y"));
            // Check each absolute displacement as well as the line and its
            // vectors to the contour anchor. A translated short mark can
            // otherwise change direction or length at large coordinates.
            var start = Move(grainline.Start);
            var end = Move(grainline.End);
            var moved = Geometry.Translate([basePiece.Points[0], grainline.Start, grainline.End],
                delta.X * steps, delta.Y * steps,cancellationToken);
            if (moved[0] != result[0] || moved[1] != start || moved[2] != end)
                throw new InvalidDataException("İplik yönü ve kontur aynı konuma güvenle taşınamadı.");
            grainline.Start = start;
            grainline.End = end;
        }
        ValidateContour(result, basePiece.Closed, cancellationToken);
        if (basePiece.Closed && Math.Sign(Geometry.SignedArea(basePiece.Points,cancellationToken)) != Math.Sign(Geometry.SignedArea(result,cancellationToken)))
            throw new InvalidDataException("Serileme kontur yönünü tersine çevirdi; kuralları kontrol edin.");
        List<NotchMark>? resultMarks = null;
        if (sourceMarks is not null)
        {
            resultMarks = new List<NotchMark>(sourceMarks.Count);
            foreach (var mark in sourceMarks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int i = mark.VertexIndex;
                var position = new Point2(GradeCoordinate(mark.Position.X, increments[i].X, steps, i, "X"),
                    GradeCoordinate(mark.Position.Y, increments[i].Y, steps, i, "Y"));
                if (position != result[i])
                    throw new InvalidDataException("Serilemede köşe işareti yeni noktayla eşleşmedi; işlem uygulanmadı.");
                resultMarks.Add(new NotchMark { VertexIndex = i, Position = position });
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PatternPiece { Id = Guid.NewGuid(), Name = $"{basePiece.Name} · {sizeName.Trim()}", Points = result, Closed = basePiece.Closed, Grainline = grainline, Notches = resultMarks, ExperimentalALine=basePiece.ExperimentalALine };
    }

    private static double GradeCoordinate(double source, double increment, double steps, int index, string axis)
    {
        var displacement = increment * steps;
        // Adding positive zero would rewrite a source -0 to +0 even though
        // this axis did not move. Keep the original binary64 representation.
        var result = displacement == 0 ? source : source + displacement;
        if (increment != 0 && steps != 0 && (displacement == 0 || result == source))
            throw new InvalidDataException($"Nokta {index}, {axis}: serileme artışı sayısal hassasiyet nedeniyle kayboluyor.");
        // Match the core translation guard's relative arithmetic tolerance.
        // This is not a manufacturing tolerance or a local-shape certificate.
        if (displacement != 0 && double.IsFinite(displacement) && double.IsFinite(result) &&
            Math.Abs((result - source) / displacement - 1) > 1e-9)
            throw new InvalidDataException($"Nokta {index}, {axis}: serileme artışı sayısal hassasiyetle korunamıyor.");
        return result;
    }

    public static IReadOnlyList<PatternPiece> GenerateSizes(PatternPiece basePiece, IReadOnlyList<Point2> increments, IReadOnlyList<GradeSize> sizes)
        => GenerateSizes(basePiece, increments, sizes, CancellationToken.None);

    public static IReadOnlyList<PatternPiece> GenerateSizes(PatternPiece basePiece, IReadOnlyList<Point2> increments, IReadOnlyList<GradeSize> sizes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(basePiece);
        _ = CopyGrainlineForReview(basePiece);
        ArgumentNullException.ThrowIfNull(increments);
        ArgumentNullException.ThrowIfNull(sizes);
        if (basePiece.Points is null || basePiece.Points.Count != increments.Count)
            throw new ArgumentException("Her nokta için bir kural gereklidir.", nameof(increments));
        // Bound operands before multiplication, then total quadratic work including both contour validations.
        if(increments.Count>MaximumGradePoints)throw new ArgumentException("Serileme için en fazla 7071 nokta desteklenir.",nameof(increments));
        if (sizes.Count > 1000) throw new ArgumentException("En fazla 1000 beden oluşturulabilir.", nameof(sizes));
        if ((long)increments.Count * sizes.Count > 200_000 || (long)increments.Count * increments.Count * sizes.Count > MaximumBatchValidationWork)
            throw new ArgumentException("Serileme grubu çok büyük; daha küçük gruplar kullanın.", nameof(sizes));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var size in sizes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (size is null || string.IsNullOrWhiteSpace(size.Name) || !names.Add(size.Name.Trim()))
                throw new ArgumentException("Beden adları boş veya yinelenen olamaz.", nameof(sizes));
        }
        var generated = new List<PatternPiece>(sizes.Count);
        foreach (var size in sizes)
            generated.Add(Generate(basePiece, increments, size.Steps, size.Name, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        return generated;
    }

    // There is no grading rule for the grainline itself. Keep its original world
    // coordinates as a reviewable reference, never as an approved production mark.
    private static Grainline? CopyGrainlineForReview(PatternPiece piece)
    {
        if (piece.Grainline is not { } source) return null;
        var start = source.Start;
        var end = source.End;
        ValidatePoint(start);
        ValidatePoint(end);
        if (start == end)
            throw new ArgumentException("İplik yönünün iki ucu farklı olmalıdır.", nameof(piece));
        return new Grainline { Start = start, End = end, NeedsReview = true };
    }

    private static void ValidatePoint(Point2 point)
    {
        if (!point.IsFinite || Math.Abs(point.X) > 1e8 || Math.Abs(point.Y) > 1e8)
            throw new ArgumentOutOfRangeException(nameof(point), "Koordinat sonlu ve ±100 milyon mm içinde olmalıdır.");
    }
    private static void ValidateContour(IReadOnlyList<Point2> points, bool closed, CancellationToken cancellationToken)
    {
        // Quadratic intersection validation is intentionally bounded for this first implementation.
        if (points.Count < (closed ? 3 : 2) || points.Count > GradingEngine.MaximumGradePoints)
            throw new ArgumentException("Serileme için geçersiz nokta sayısı (en fazla 7071).");
        for (var i = 0; i < points.Count; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            ValidatePoint(points[i]);
        }
        // Geometry's adjacent-edge tolerance can miss short, exactly collinear
        // reversals. Reject these before accepting either source or result.
        var count = points.Count;
        if (closed && count > 1 && points[0] == points[^1]) count--;
        var first = closed ? 0 : 1;
        var end = closed ? count : count - 1;
        for (var i = first; i < end; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            var a = points[(i + count - 1) % count];
            var b = points[i];
            var c = points[(i + 1) % count];
            var ux = a.X - b.X; var uy = a.Y - b.Y;
            var vx = c.X - b.X; var vy = c.Y - b.Y;
            if (ux * vy - uy * vx == 0 && ux * vx + uy * vy > 0)
                throw new InvalidDataException("Kontur komşu kenarlarda geri dönüyor veya üst üste biniyor.");
        }
        if(points.Count>Geometry.MaxValidatedContourVertices)
        {
            if(closed)LargeContourValidator.Validate(points,cancellationToken);else LargeOpenPathValidator.Validate(points,cancellationToken);
        }
        else if (Geometry.HasSelfIntersections(points, closed, cancellationToken)) throw new InvalidDataException("Kontur kendiyle kesişiyor veya yinelenen kenar içeriyor.");
        if (closed && Math.Abs(Geometry.SignedArea(points,cancellationToken)) <= Geometry.Epsilon)
            throw new InvalidDataException("Kapalı konturun alanı sıfır olamaz.");
    }
}

