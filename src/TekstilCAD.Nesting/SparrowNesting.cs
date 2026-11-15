using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Clipper2Lib;
using TekstilCAD.Core;

namespace TekstilCAD.Nesting;

/// <summary>Input for an external sparrow run. One <see cref="PatternPiece"/> is one cut copy.
/// X runs across the fabric width and Y along its length, as in <see cref="NestingEngine"/>.</summary>
public sealed record SparrowNestingRequest(
    IReadOnlyList<PatternPiece> Pieces,
    double FabricWidth,
    double Gap = 5,
    double EdgeMargin = 0,
    bool AllowRotate180 = false,
    int TimeLimitSeconds = 60,
    ulong? Seed = null);

/// <summary>One placed copy. Points are the placed contour in millimetres; rotation is 0 or 180 degrees.</summary>
public sealed record SparrowPlacement(Guid PieceId, double RotationDegrees, IReadOnlyList<Point2> Points);

/// <summary>Detached layout from sparrow after our own independent check. It is a heuristic result,
/// not a production approval; grainline, notch marks and grading are not carried to the result.</summary>
public sealed record SparrowNestingResult(IReadOnlyList<SparrowPlacement> Placements, double UsedLength, double UtilizationPercent);

/// <summary>sparrow output was missing, malformed or failed our independent check. The layout must not be used.</summary>
public sealed class SparrowResultRejectedException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>
/// Adapter for the external, MIT-licensed sparrow nesting heuristic
/// (https://github.com/JeroenGar/sparrow, built on the MPL-2.0 jagua-rs engine).
/// sparrow is not bundled; the caller supplies the path of a separately built executable.
///
/// Frame mapping. sparrow packs into a strip of fixed height (its Y) and variable width (its X).
/// Our fabric width is mapped to sparrow's strip height through a +90° rotation, never a mirror:
/// local item coordinates go in as (u, v) = (y, -x); a placed sparrow point (u, v) comes back as
/// (x, y) = (FabricWidth - EdgeMargin - v, u). The strip height is FabricWidth - 2 × EdgeMargin.
/// A rotation angle means the same thing in both frames, so 0° keeps the grainline direction.
///
/// sparrow's result is never trusted directly: every placement is re-checked here for allowed
/// rotation, no reflection, fabric bounds and pairwise gap with Clipper2 on a 0.001 mm grid.
/// sparrow uses 32-bit floats, so bounds and gap are checked with <see cref="Tolerance"/>.
/// </summary>
public static class SparrowNesting
{
    public const int MaximumPieces = 500;
    public const int MaximumVerticesPerPiece = 2_000;
    public const double MaximumDimension = 1_000_000;
    public const int MaximumTimeLimitSeconds = 24 * 60 * 60;
    /// <summary>Allowed numerical slack in millimetres for bounds and gap checks.</summary>
    public const double Tolerance = 0.01;
    public const string InstanceName = "tekstilcad";
    private const double Scale = 1000;
    private const double AngleTolerance = 1e-3;

    /// <summary>Validate the request and build sparrow's (jagua-rs SPP) input JSON.</summary>
    public static string BuildInstanceJson(SparrowNestingRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request, cancellationToken);
        var stripHeight = request.FabricWidth - 2 * request.EdgeMargin;
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            json.WriteStartObject();
            json.WriteString("name", InstanceName);
            json.WriteNumber("min_item_separation", request.Gap);
            json.WriteNumber("strip_height", stripHeight);
            json.WriteStartArray("items");
            for (var index = 0; index < request.Pieces.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var piece = request.Pieces[index];
                json.WriteStartObject();
                json.WriteNumber("id", index);
                json.WriteNumber("demand", 1);
                json.WriteStartObject("orientation");
                json.WriteStartObject("rotation");
                json.WriteString("mode", "discrete");
                json.WriteStartArray("angles");
                json.WriteNumberValue(0);
                if (request.AllowRotate180) json.WriteNumberValue(180);
                json.WriteEndArray();
                json.WriteEndObject();
                json.WriteEndObject();
                json.WriteStartObject("shape");
                json.WriteString("type", "simple_polygon");
                json.WriteStartArray("data");
                foreach (var p in ToSparrowLocal(piece.Points))
                {
                    json.WriteStartArray();
                    json.WriteNumberValue(p.X);
                    json.WriteNumberValue(p.Y);
                    json.WriteEndArray();
                }
                json.WriteEndArray();
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Read sparrow's final JSON (output/final_{name}.json), map it back to our frame and check it independently.</summary>
    public static SparrowNestingResult ReadSolution(string solutionJson, SparrowNestingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solutionJson);
        Validate(request, cancellationToken);
        var count = request.Pieces.Count;
        var transforms = new (double Rotation, double U, double V)?[count];
        try
        {
            using var document = JsonDocument.Parse(solutionJson);
            var items = document.RootElement.GetProperty("solution").GetProperty("layout").GetProperty("placed_items");
            foreach (var item in items.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = item.GetProperty("item_id").GetInt64();
                if (id < 0 || id >= count) throw new SparrowResultRejectedException("sparrow sonucunda bilinmeyen parça var; sonuç kullanılmadı.");
                if (transforms[id] is not null) throw new SparrowResultRejectedException("sparrow sonucunda aynı parça birden fazla yerleşmiş; sonuç kullanılmadı.");
                var transformation = item.GetProperty("transformation");
                if (transformation.TryGetProperty("reflected", out var reflected) && reflected.GetBoolean())
                    throw new SparrowResultRejectedException("sparrow bir parçayı aynalamış; sonuç kullanılmadı.");
                var translation = transformation.GetProperty("translation");
                transforms[id] = (transformation.GetProperty("rotation").GetDouble(), translation[0].GetDouble(), translation[1].GetDouble());
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException
                                       && error is not SparrowResultRejectedException)
        {
            throw new SparrowResultRejectedException("sparrow sonucu okunamadı; sonuç kullanılmadı.", error);
        }

        var placements = new SparrowPlacement[count];
        var contours = new Path64[count];
        double totalArea = 0, usedLength = 0, minY = double.PositiveInfinity;
        double xMin = request.EdgeMargin - Tolerance, xMax = request.FabricWidth - request.EdgeMargin + Tolerance;
        var right = request.FabricWidth - request.EdgeMargin;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transform = transforms[index] ?? throw new SparrowResultRejectedException("sparrow bazı parçaları yerleştirmemiş; sonuç kullanılmadı.");
            var rotation = NormalizeDegrees(transform.Rotation);
            double snapped;
            if (Math.Abs(rotation) <= AngleTolerance || Math.Abs(rotation - 360) <= AngleTolerance) snapped = 0;
            else if (request.AllowRotate180 && Math.Abs(rotation - 180) <= AngleTolerance) snapped = 180;
            else throw new SparrowResultRejectedException("sparrow izin verilmeyen bir açı kullanmış; sonuç kullanılmadı.");

            var piece = request.Pieces[index];
            var placed = new Point2[piece.Points.Count];
            for (var i = 0; i < placed.Length; i++)
            {
                // Rotate in sparrow's frame by the exact allowed angle, then translate, then map back.
                var local = new Point2(piece.Points[i].Y, -piece.Points[i].X);
                var (ru, rv) = snapped == 0 ? (local.X, local.Y) : (-local.X, -local.Y);
                double u = ru + transform.U, v = rv + transform.V;
                var point = new Point2(right - v, u);
                if (!point.IsFinite || point.X < xMin || point.X > xMax)
                    throw new SparrowResultRejectedException("sparrow yerleşimi kumaş sınırını aşıyor; sonuç kullanılmadı.");
                placed[i] = point;
                usedLength = Math.Max(usedLength, point.Y);
                minY = Math.Min(minY, point.Y);
            }
            contours[index] = ToPath(placed);
            totalArea += Math.Abs(Clipper.Area(contours[index])) / (Scale * Scale);
            placements[index] = new(piece.Id, snapped, placed);
        }
        if (minY < -Tolerance) throw new SparrowResultRejectedException("sparrow yerleşimi pastal başlangıcının gerisinde; sonuç kullanılmadı.");

        CheckPairwiseGap(contours, request.Gap, cancellationToken);
        return new(placements, usedLength, usedLength > 0 ? totalArea / (request.FabricWidth * usedLength) * 100 : 0);
    }

    /// <summary>
    /// Run a separately built sparrow executable in a private temporary folder and return the checked result.
    /// The executable is started with "-i input.json -t seconds [-s seed]" and writes output/final_tekstilcad.json
    /// under its working directory. Not exercised by the automated tests; verify on the target PC first.
    /// </summary>
    public static async Task<SparrowNestingResult> RunAsync(string executablePath, SparrowNestingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!File.Exists(executablePath)) throw new FileNotFoundException("sparrow çalıştırılabilir dosyası bulunamadı.", executablePath);
        var input = BuildInstanceJson(request, cancellationToken);
        var folder = Path.Combine(Path.GetTempPath(), "TekstilCAD-sparrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "input.json"), input, cancellationToken).ConfigureAwait(false);
            var start = new ProcessStartInfo(executablePath)
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("-i"); start.ArgumentList.Add("input.json");
            start.ArgumentList.Add("-t"); start.ArgumentList.Add(request.TimeLimitSeconds.ToString(CultureInfo.InvariantCulture));
            if (request.Seed is { } seed) { start.ArgumentList.Add("-s"); start.ArgumentList.Add(seed.ToString(CultureInfo.InvariantCulture)); }
            using var process = Process.Start(start) ?? throw new SparrowResultRejectedException("sparrow başlatılamadı.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new SparrowResultRejectedException($"sparrow {process.ExitCode} koduyla bitti; sonuç kullanılmadı.");
            var output = Path.Combine(folder, "output", $"final_{InstanceName}.json");
            if (!File.Exists(output)) throw new SparrowResultRejectedException("sparrow sonuç dosyası üretmedi; sonuç kullanılmadı.");
            var solution = await File.ReadAllTextAsync(output, cancellationToken).ConfigureAwait(false);
            return ReadSolution(solution, request, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void Validate(SparrowNestingRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Pieces);
        if (!double.IsFinite(request.FabricWidth) || request.FabricWidth <= 0 || request.FabricWidth > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(request), "Kumaş eni geçersiz.");
        if (!double.IsFinite(request.Gap) || request.Gap < 0 || request.Gap > MaximumDimension) throw new ArgumentOutOfRangeException(nameof(request), "Parça aralığı geçersiz.");
        if (!double.IsFinite(request.EdgeMargin) || request.EdgeMargin < 0 || request.EdgeMargin * 2 >= request.FabricWidth) throw new ArgumentOutOfRangeException(nameof(request), "Kenar payı geçersiz.");
        if (request.TimeLimitSeconds < 1 || request.TimeLimitSeconds > MaximumTimeLimitSeconds) throw new ArgumentOutOfRangeException(nameof(request), "Süre sınırı 1 saniye ile 24 saat arasında olmalıdır.");
        if (request.Pieces.Count == 0 || request.Pieces.Count > MaximumPieces) throw new ArgumentException("Pastal parça sayısı 1 ile 500 arasında olmalıdır.", nameof(request));
        var usable = request.FabricWidth - 2 * request.EdgeMargin;
        var ids = new HashSet<Guid>();
        foreach (var piece in request.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (piece is null) throw new ArgumentException("Geçersiz parça.", nameof(request));
            if (piece.Id == Guid.Empty || !ids.Add(piece.Id)) throw new ArgumentException("Her pastal parçasının kimliği benzersiz olmalıdır.", nameof(request));
            if (piece.Points is null || !piece.Closed || piece.Points.Count < 3 || piece.Points.Count > MaximumVerticesPerPiece)
                throw NestingPieceError.Create(piece.Id, "Pastal için kapalı, 3–2000 noktalı konturlar gereklidir.", nameof(request));
            if (piece.Points.Any(p => !p.IsFinite || Math.Abs(p.X) > MaximumDimension || Math.Abs(p.Y) > MaximumDimension))
                throw NestingPieceError.Create(piece.Id, "Koordinatlar çalışma sınırı dışında.", nameof(request));
            if (Geometry.HasSelfIntersections(piece.Points, true, cancellationToken))
                throw NestingPieceError.Create(piece.Id, "Kontur kendisiyle kesişmemelidir.", nameof(request));
            if (!(Math.Abs(Clipper.Area(ToPath(piece.Points))) > 0))
                throw NestingPieceError.Create(piece.Id, "Kontur sıfırdan büyük alan taşımalıdır.", nameof(request));
            var width = piece.Points.Max(p => p.X) - piece.Points.Min(p => p.X);
            if (width > usable) throw NestingPieceError.Create(piece.Id, "Parça kumaş enine sığmıyor.", nameof(request));
        }
    }

    /// <summary>(u, v) = (y, -x), returned counter-clockwise in sparrow's frame.</summary>
    internal static List<Point2> ToSparrowLocal(IReadOnlyList<Point2> points)
    {
        var mapped = points.Select(p => new Point2(p.Y, -p.X)).ToList();
        if (Clipper.Area(ToPath(mapped)) < 0) mapped.Reverse();
        return mapped;
    }

    private static void CheckPairwiseGap(Path64[] contours, double gap, CancellationToken cancellationToken)
    {
        // Two contours at least (gap - tolerance) apart cannot overlap after each grows by half of that.
        var half = Math.Max(0, gap - Tolerance) / 2 * Scale;
        var grown = new Path64[contours.Length];
        var boxes = new Rect64[contours.Length];
        for (var i = 0; i < contours.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (half > 0)
            {
                var result = Clipper.InflatePaths(new Paths64 { contours[i] }, half, JoinType.Round, EndType.Polygon);
                if (result.Count != 1) throw new SparrowResultRejectedException("Parça aralığı tek bir dış kontur oluşturmadı; sonuç kullanılmadı.");
                grown[i] = result[0];
            }
            else grown[i] = contours[i];
            boxes[i] = Clipper.GetBounds(grown[i]);
        }
        for (var i = 0; i < grown.Length; i++)
            for (var j = i + 1; j < grown.Length; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!boxes[i].Intersects(boxes[j])) continue;
                foreach (var path in Clipper.Intersect(new Paths64 { grown[i] }, new Paths64 { grown[j] }, FillRule.NonZero))
                    if (Math.Abs(Clipper.Area(path)) > 1)
                        throw new SparrowResultRejectedException("sparrow yerleşiminde çakışma ya da yetersiz parça aralığı bulundu; sonuç kullanılmadı.");
            }
    }

    private static double NormalizeDegrees(double degrees)
    {
        if (!double.IsFinite(degrees)) throw new SparrowResultRejectedException("sparrow sonucunda geçersiz açı var; sonuç kullanılmadı.");
        var result = degrees % 360;
        return result < 0 ? result + 360 : result;
    }

    private static Path64 ToPath(IReadOnlyList<Point2> points)
    {
        var path = new Path64(points.Count);
        foreach (var p in points) path.Add(new Point64((long)Math.Round(p.X * Scale), (long)Math.Round(p.Y * Scale)));
        return path;
    }
}
