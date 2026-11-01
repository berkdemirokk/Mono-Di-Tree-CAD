using TekstilCAD.Core;
using System.Numerics;

namespace TekstilCAD.Nesting;

public sealed record NestingResult(ProjectDocument Layout, double UsedLength, double UtilizationPercent, double WastePercent)
{
    public double FabricWidth { get; init; }
    public double Gap { get; init; }
    public double EdgeMargin { get; init; }
}

/// <summary>
/// Deterministic conservative shelf placement, in millimetres. Bounding boxes have the requested
/// clearance; grain orientation is unchanged. This is a feasible baseline, not optimal polygon nesting.
/// Edge margin is applied on both sides and at both ends. Waste is measured against fabricWidth * UsedLength, not purchased length.
/// </summary>
public static class NestingEngine
{
    private const string ClearancePrecisionMarker = "TekstilCAD.Nesting.ClearancePrecision";
    private static InvalidOperationException ClearancePrecision(string message)
    {
        var error = new InvalidOperationException(message);
        error.Data[ClearancePrecisionMarker] = true;
        return error;
    }
    internal static bool IsClearancePrecision(InvalidOperationException error) => error.Data[ClearancePrecisionMarker] is true;

    private const string LengthExceededMarker = "TekstilCAD.Nesting.LengthExceeded";
    private static InvalidOperationException LengthExceeded()
    {
        var error = new InvalidOperationException("Pastal boyu belirtilen uzunluk veya çalışma sınırını aşıyor.");
        error.Data[LengthExceededMarker] = true;
        return error;
    }
    internal static bool IsLengthExceeded(InvalidOperationException error) => error.Data[LengthExceededMarker] is true;
    // Avoid quadratic contour validation on unbounded imported data.
    public const int MaximumPieces = 10_000;
    public const int MaximumVerticesPerPiece = 2_000;
    public const double MaximumDimension = 100_000_000;
    public const int MaximumTotalVertices = 200_000;
    public const long MaximumValidationWork = 50_000_000;

    // Keys are detached snapshots and are never modified during arrangement.
    private sealed class ContourComparer : IEqualityComparer<List<Point2>>
    {
        public bool Equals(List<Point2>? a, List<Point2>? b) =>
            ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b));
        public int GetHashCode(List<Point2> points)
        {
            var hash = new HashCode();
            foreach (var point in points) hash.Add(point);
            return hash.ToHashCode();
        }
    }

    // Limit relative changes to checked edge and anchor vectors and axis spans.
    // This arithmetic guard cannot prove preservation of every shallow local
    // feature, and is not a manufacturing tolerance or physical accuracy proof.
    private const long MaximumRelativeDistortionDenominator = 1_000_000_000;

    private readonly record struct BinaryNumber(BigInteger Significand, int Exponent);

    private static BinaryNumber ExactBinary(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7ff);
        long fraction = bits & 0x000f_ffff_ffff_ffffL;
        if (exponent == 0x7ff) throw new ArgumentOutOfRangeException(nameof(value));
        if (exponent == 0 && fraction == 0) return new(BigInteger.Zero, 0);
        var significand = new BigInteger(exponent == 0 ? fraction : fraction | (1L << 52));
        if (bits < 0) significand = -significand;
        return new(significand, exponent == 0 ? -1074 : exponent - 1075);
    }

    // Compare exact vector differences of the represented binary64 coordinates.
    // The largest source component sets the scale for both components: an edge
    // that is almost horizontal or vertical must not fail solely because its
    // near-zero component has a large *component-relative* rounding error.
    private static bool SameVectorWithinBudget(Point2 sourceStart, Point2 sourceEnd, Point2 placedStart, Point2 placedEnd)
    {
        var sx0 = ExactBinary(sourceStart.X); var sy0 = ExactBinary(sourceStart.Y);
        var sx1 = ExactBinary(sourceEnd.X); var sy1 = ExactBinary(sourceEnd.Y);
        var px0 = ExactBinary(placedStart.X); var py0 = ExactBinary(placedStart.Y);
        var px1 = ExactBinary(placedEnd.X); var py1 = ExactBinary(placedEnd.Y);
        int exponent = Math.Min(Math.Min(Math.Min(sx0.Exponent, sy0.Exponent), Math.Min(sx1.Exponent, sy1.Exponent)),
            Math.Min(Math.Min(px0.Exponent, py0.Exponent), Math.Min(px1.Exponent, py1.Exponent)));
        static BigInteger Aligned(BinaryNumber value, int exponent) => value.Significand << (value.Exponent - exponent);
        var dx = Aligned(sx1, exponent) - Aligned(sx0, exponent);
        var dy = Aligned(sy1, exponent) - Aligned(sy0, exponent);
        var placedDx = Aligned(px1, exponent) - Aligned(px0, exponent);
        var placedDy = Aligned(py1, exponent) - Aligned(py0, exponent);
        var magnitude = BigInteger.Max(BigInteger.Abs(dx), BigInteger.Abs(dy));
        var error = BigInteger.Max(BigInteger.Abs(placedDx - dx), BigInteger.Abs(placedDy - dy));
        return error * MaximumRelativeDistortionDenominator <= magnitude;
    }

    // A long, shallow contour can satisfy vector-relative checks while its
    // small overall height or width changes substantially. Check each axis
    // extent independently using exact differences of represented values.
    private static bool SameSpanWithinBudget(double sourceMin, double sourceMax, double placedMin, double placedMax)
    {
        var s0 = ExactBinary(sourceMin); var s1 = ExactBinary(sourceMax);
        var p0 = ExactBinary(placedMin); var p1 = ExactBinary(placedMax);
        int exponent = Math.Min(Math.Min(s0.Exponent, s1.Exponent), Math.Min(p0.Exponent, p1.Exponent));
        static BigInteger Aligned(BinaryNumber value, int exponent) => value.Significand << (value.Exponent - exponent);
        var sourceSpan = Aligned(s1, exponent) - Aligned(s0, exponent);
        var placedSpan = Aligned(p1, exponent) - Aligned(p0, exponent);
        return BigInteger.Abs(placedSpan - sourceSpan) * MaximumRelativeDistortionDenominator <= sourceSpan;
    }

    internal static void ValidateTranslation(PatternPiece source, IReadOnlyList<Point2> placed, CancellationToken cancellationToken)
    {
        var original = source.Points;
        if (original is null || placed is null || original.Count == 0 || original.Count != placed.Count)
            throw new ArgumentException("Kaynak ve yerleştirilen kontur noktaları eşleşmiyor.");
        double sourceMinX = original[0].X, sourceMaxX = original[0].X;
        double sourceMinY = original[0].Y, sourceMaxY = original[0].Y;
        double placedMinX = placed[0].X, placedMaxX = placed[0].X;
        double placedMinY = placed[0].Y, placedMaxY = placed[0].Y;
        for (int i = 0; i < original.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sourceMinX = Math.Min(sourceMinX, original[i].X); sourceMaxX = Math.Max(sourceMaxX, original[i].X);
            sourceMinY = Math.Min(sourceMinY, original[i].Y); sourceMaxY = Math.Max(sourceMaxY, original[i].Y);
            placedMinX = Math.Min(placedMinX, placed[i].X); placedMaxX = Math.Max(placedMaxX, placed[i].X);
            placedMinY = Math.Min(placedMinY, placed[i].Y); placedMaxY = Math.Max(placedMaxY, placed[i].Y);
            int previous = (i + original.Count - 1) % original.Count;
            if (!SameVectorWithinBudget(original[previous], original[i], placed[previous], placed[i]) ||
                !SameVectorWithinBudget(original[0], original[i], placed[0], placed[i]))
                throw NestingPieceError.Create(source.Id,
                    "Parçanın şekli yerleştirme sırasında sayısal hassasiyetle korunamıyor; daha küçük koordinatlar veya farklı bir iş grubu kullanın.", "pieces");
        }
        if (!SameSpanWithinBudget(sourceMinX, sourceMaxX, placedMinX, placedMaxX) ||
            !SameSpanWithinBudget(sourceMinY, sourceMaxY, placedMinY, placedMaxY))
            throw NestingPieceError.Create(source.Id,
                "Parçanın eni veya boyu yerleştirme sırasında sayısal hassasiyetle korunamıyor; daha küçük koordinatlar veya farklı bir iş grubu kullanın.", "pieces");
    }

    // Same relative arithmetic checks used when placing grainlines; no physical
    // tolerance or proof of all local shape details is implied.
    internal static bool GrainlinesEqualAfterTranslation(PatternPiece source, PatternPiece placed)
    {
        if (source.Grainline is null || placed.Grainline is null)
            return source.Grainline is null && placed.Grainline is null;
        var a = source.Grainline; var b = placed.Grainline;
        if (a.NeedsReview != b.NeedsReview || source.Points.Count == 0 || placed.Points.Count == 0 ||
            !a.Start.IsFinite || !a.End.IsFinite || !b.Start.IsFinite || !b.End.IsFinite ||
            a.Start == a.End || b.Start == b.End) return false;
        return SameVectorWithinBudget(a.Start, a.End, b.Start, b.End) &&
            SameVectorWithinBudget(source.Points[0], a.Start, placed.Points[0], b.Start) &&
            SameVectorWithinBudget(source.Points[0], a.End, placed.Points[0], b.End) &&
            SameSpanWithinBudget(Math.Min(a.Start.X, a.End.X), Math.Max(a.Start.X, a.End.X), Math.Min(b.Start.X, b.End.X), Math.Max(b.Start.X, b.End.X)) &&
            SameSpanWithinBudget(Math.Min(a.Start.Y, a.End.Y), Math.Max(a.Start.Y, a.End.Y), Math.Min(b.Start.Y, b.End.Y), Math.Max(b.Start.Y, b.End.Y));
    }

    internal static void ValidateMarkers(PatternPiece piece,CancellationToken token)
    {
        if(piece.Notches is not { } marks)return;
        if(piece.Points is null || marks.Count==0 || marks.Count>piece.Points.Count)throw new ArgumentException("Geçersiz köşe işareti listesi.");
        var indices=new HashSet<int>();
        foreach(var mark in marks)
        {
            token.ThrowIfCancellationRequested();
            if(mark is null || mark.VertexIndex<0 || mark.VertexIndex>=piece.Points.Count ||
                (piece.Closed && piece.Points.Count>1 && piece.Points[0]==piece.Points[^1] && mark.VertexIndex==piece.Points.Count-1) ||
                !indices.Add(mark.VertexIndex) || mark.Position!=piece.Points[mark.VertexIndex])
                throw new ArgumentException("Köşe işareti kaynak konturla eşleşmiyor.");
        }
    }
    internal static Point2 PlacedMarkerPosition(PatternPiece source,PatternPiece placed,NotchMark mark)
    {
        var oldPoint=source.Points[mark.VertexIndex];var point=placed.Points[mark.VertexIndex];
        return new(oldPoint.X==point.X ? mark.Position.X : point.X,oldPoint.Y==point.Y ? mark.Position.Y : point.Y);
    }
    internal static bool MarkersMatchPlacement(PatternPiece source,PatternPiece placed,CancellationToken token)
    {
        ValidateMarkers(source,token);ValidateMarkers(placed,token);
        if(source.Notches is null || placed.Notches is null)return source.Notches is null && placed.Notches is null;
        if(source.Notches.Count!=placed.Notches.Count || source.Points.Count!=placed.Points.Count)return false;
        for(var i=0;i<source.Notches.Count;i++)
        {
            token.ThrowIfCancellationRequested();var a=source.Notches[i];var b=placed.Notches[i];
            if(a.VertexIndex!=b.VertexIndex)return false;
            var expected=PlacedMarkerPosition(source,placed,a);
            if(BitConverter.DoubleToInt64Bits(expected.X)!=BitConverter.DoubleToInt64Bits(b.Position.X) ||
               BitConverter.DoubleToInt64Bits(expected.Y)!=BitConverter.DoubleToInt64Bits(b.Position.Y))return false;
        }
        return true;
    }

    // Round outward rather than silently shaving off a requested positive gap.
    private static double AddClearance(double value, double clearance)
    {
        var result = value + clearance;
        if (clearance > 0 && double.IsFinite(result) && !ShelfLayoutVerifier.DifferenceAtLeast(result, value, clearance))
            result = Math.BitIncrement(result);
        return result;
    }

    public static NestingResult Arrange(IReadOnlyList<PatternPiece> pieces, double fabricWidth, double gap = 5,
        double maxLength = double.PositiveInfinity, double edgeMargin = 0)
        => Arrange(pieces, fabricWidth, gap, maxLength, edgeMargin, CancellationToken.None);

    /// <summary>Arrange detached output while cooperatively observing cancellation.</summary>
    public static NestingResult Arrange(IReadOnlyList<PatternPiece> pieces, double fabricWidth, double gap,
        double maxLength, double edgeMargin, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(pieces);
        if (!double.IsFinite(fabricWidth) || fabricWidth <= 0 || fabricWidth > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(fabricWidth));
        if (!double.IsFinite(gap) || gap < 0 || gap > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(gap));
        if (!double.IsFinite(edgeMargin) || edgeMargin < 0 || edgeMargin > MaximumDimension || edgeMargin * 2 >= fabricWidth)
            throw new ArgumentOutOfRangeException(nameof(edgeMargin), "Kenar payı sonlu ve negatif olmayan bir değer olmalı; iki kenar payından sonra kumaşta kullanılabilir en kalmalıdır.");
        var usableWidth = fabricWidth - 2 * edgeMargin;
        if (double.IsNaN(maxLength) || maxLength <= 0 || double.IsNegativeInfinity(maxLength))
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        if (pieces.Count > MaximumPieces) throw new ArgumentException("Pastal parça sayısı sınırı aşıldı.", nameof(pieces));

        // Verify all identities first so a later piece cannot make an earlier
        // piece-specific error ambiguous by reusing its identifier.
        var ids=new HashSet<Guid>();
        foreach(var piece in pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(piece is null)throw new ArgumentException("Geçersiz parça.",nameof(pieces));
            if(piece.Id==Guid.Empty || !ids.Add(piece.Id))throw new ArgumentException("Her pastal parçasının kimliği benzersiz olmalıdır.",nameof(pieces));
        }
        // Bound copies before taking detached snapshots. The caller must not mutate
        // input while it is being copied; all later work uses only snapshots.
        long totalVertices = 0;
        foreach (var piece in pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (piece.Points is null) throw NestingPieceError.Create(piece.Id,"Geçersiz parça.",nameof(pieces));
            if (piece.Points.Count > MaximumVerticesPerPiece)
                throw NestingPieceError.Create(piece.Id,$"Kontur sınırı: {MaximumVerticesPerPiece} nokta.",nameof(pieces));
            try { ValidateMarkers(piece,cancellationToken); }
            catch(ArgumentException error) { throw NestingPieceError.Create(piece.Id,error.Message,nameof(pieces),error); }
            totalVertices += piece.Points.Count;
            if (totalVertices > MaximumTotalVertices)
                throw new ArgumentException("Pastal doğrulama kaynak sınırı aşıldı; daha küçük iş grupları kullanın.", nameof(pieces));
        }
        var snapshots = new List<PatternPiece>(pieces.Count);
        var uniqueContours = new HashSet<List<Point2>>(new ContourComparer());
        long validationWork = 0;
        foreach (var piece in pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = piece.Clone();
            snapshots.Add(snapshot);
            long cost = (long)snapshot.Points.Count * snapshot.Points.Count;
            // Every placed copy is checked. Only identical source snapshots share validation.
            validationWork += cost;
            if (uniqueContours.Add(snapshot.Points)) validationWork += cost;
            if (validationWork > MaximumValidationWork)
                throw new ArgumentException("Pastal doğrulama kaynak sınırı aşıldı; daha küçük iş grupları kullanın.", nameof(pieces));
        }
        var entries = new List<(PatternPiece Piece, Bounds2 Bounds, double Area, int Index)>();

        var validatedContours = new Dictionary<List<Point2>, (Bounds2 Bounds, double Area)>(new ContourComparer());
        for (int index = 0; index < snapshots.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var piece = snapshots[index];
            if (piece is null) throw new ArgumentException("Pastal için kapalı, en az üç noktalı konturlar gereklidir.", nameof(pieces));
            if (piece.Points is null || !piece.Closed || piece.Points.Count < 3)
                throw NestingPieceError.Create(piece.Id,"Pastal için kapalı, en az üç noktalı konturlar gereklidir.",nameof(pieces));
            if (piece.Points.Count > MaximumVerticesPerPiece)
                throw NestingPieceError.Create(piece.Id,$"Kontur sınırı: {MaximumVerticesPerPiece} nokta.",nameof(pieces));
            if (piece.Name is null || piece.Name.Length > 1024)
                throw NestingPieceError.Create(piece.Id, "Geçersiz parça adı.", nameof(pieces));

            if (piece.Points.Any(p => !p.IsFinite || Math.Abs(p.X) > MaximumDimension || Math.Abs(p.Y) > MaximumDimension))
                throw NestingPieceError.Create(piece.Id,"Koordinatlar çalışma sınırı dışında.",nameof(pieces));
            if(piece.Grainline is { } grain &&
               (!grain.Start.IsFinite || !grain.End.IsFinite ||
                Math.Abs(grain.Start.X)>MaximumDimension || Math.Abs(grain.Start.Y)>MaximumDimension ||
                Math.Abs(grain.End.X)>MaximumDimension || Math.Abs(grain.End.Y)>MaximumDimension || grain.Start==grain.End))
                throw NestingPieceError.Create(piece.Id,"İplik yönü uçları geçersiz veya çalışma sınırı dışında.",nameof(pieces));
            if (!validatedContours.TryGetValue(piece.Points, out var geometry))
            {
                var sourceBounds = Geometry.Bounds(piece.Points);
                var sourceArea = Math.Abs(Geometry.SignedArea(piece.Points));
                if (!double.IsFinite(sourceArea) || sourceArea <= Geometry.Epsilon || Geometry.HasSelfIntersections(piece.Points, true, cancellationToken))
                    throw NestingPieceError.Create(piece.Id,"Kontur kendisiyle kesişmemeli ve sıfırdan büyük alan taşımalıdır.",nameof(pieces));
                geometry = (sourceBounds, sourceArea);
                validatedContours.Add(piece.Points, geometry);
            }
            var (bounds, area) = geometry;
            if (bounds.Width > usableWidth)
                throw NestingPieceError.Create(piece.Id,"Parça kumaş enine sığmıyor; yönü değiştirilmedi.",nameof(fabricWidth));
            entries.Add((piece, bounds, area, index));
        }

        var layout = new ProjectDocument { Name = "Pastal · deterministik raf yerleşimi" };
        double x = 0, y = edgeMargin, rowHeight = 0, usedLength = 0, totalArea = 0;
        double? previousMaxX = null, previousRowMaxY = null;
        double currentRowMaxY = 0;
        foreach (var entry in entries.OrderByDescending(e => e.Bounds.Height).ThenBy(e => e.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (x > 0 && x + entry.Bounds.Width > usableWidth)
            {
                y = gap > 0 ? AddClearance(currentRowMaxY, gap) : y + rowHeight;
                x = 0;
                rowHeight = 0;
                previousMaxX = null;
                previousRowMaxY = currentRowMaxY;
                currentRowMaxY = 0;
            }
            var bottom = y + entry.Bounds.Height;
            var fabricEnd = AddClearance(bottom, edgeMargin);
            if (!double.IsFinite(fabricEnd) || fabricEnd > Math.Min(maxLength, MaximumDimension))
                throw LengthExceeded();
            var placed = entry.Piece.Clone();
            // Normalize locally before offsetting: the displacement may exceed the coordinate
            // domain even though both the source and placed coordinates are valid.
            placed.Points = new List<Point2>(entry.Piece.Points.Count);
            foreach (var p in entry.Piece.Points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var point = new Point2(p.X - entry.Bounds.MinX + x + edgeMargin, p.Y - entry.Bounds.MinY + y);
                Geometry.Validate(point);
                placed.Points.Add(point);
            }
            ValidateTranslation(entry.Piece, placed.Points, cancellationToken);
            if(entry.Piece.Notches is { } marks)
            {
                placed.Notches=new List<NotchMark>(marks.Count);
                foreach(var mark in marks) { cancellationToken.ThrowIfCancellationRequested();placed.Notches.Add(new NotchMark { VertexIndex=mark.VertexIndex,Position=PlacedMarkerPosition(entry.Piece,placed,mark) }); }
                if(!MarkersMatchPlacement(entry.Piece,placed,cancellationToken))throw NestingPieceError.Create(entry.Piece.Id,"Köşe işaretleri yerleşimde korunamadı.",nameof(pieces));
            }
            if(entry.Piece.Grainline is { } originalGrain)
            {
                // Apply the exact same operation order used for the contour.
                // The line is not assumed to lie inside the contour and this
                // check is not a manufacturing or cloth-direction certificate.
                Point2 PlaceGrainPoint(Point2 p) => new(p.X-entry.Bounds.MinX+x+edgeMargin,p.Y-entry.Bounds.MinY+y);
                var start=PlaceGrainPoint(originalGrain.Start);var end=PlaceGrainPoint(originalGrain.End);
                if(!start.IsFinite || !end.IsFinite || Math.Abs(start.X)>MaximumDimension || Math.Abs(start.Y)>MaximumDimension ||
                   Math.Abs(end.X)>MaximumDimension || Math.Abs(end.Y)>MaximumDimension || start==end ||
                   !SameVectorWithinBudget(originalGrain.Start,originalGrain.End,start,end) ||
                   !SameVectorWithinBudget(entry.Piece.Points[0],originalGrain.Start,placed.Points[0],start) ||
                   !SameVectorWithinBudget(entry.Piece.Points[0],originalGrain.End,placed.Points[0],end) ||
                   !SameSpanWithinBudget(Math.Min(originalGrain.Start.X,originalGrain.End.X),Math.Max(originalGrain.Start.X,originalGrain.End.X),Math.Min(start.X,end.X),Math.Max(start.X,end.X)) ||
                   !SameSpanWithinBudget(Math.Min(originalGrain.Start.Y,originalGrain.End.Y),Math.Max(originalGrain.Start.Y,originalGrain.End.Y),Math.Min(start.Y,end.Y),Math.Max(start.Y,end.Y)))
                    throw NestingPieceError.Create(entry.Piece.Id,"İplik yönü yerleştirme sırasında sayısal hassasiyetle korunamıyor; farklı koordinatlarla yeniden deneyin.",nameof(pieces));
                placed.Grainline=new Grainline { Start=start,End=end,NeedsReview=originalGrain.NeedsReview };
            }
            // Small rounding shifts can alter a near-touching contour even when
            // its individual coordinate differences pass the relative check.
            // Check the actual placed polygon before accepting it into the job.
            var placedArea = Math.Abs(Geometry.SignedArea(placed.Points));
            if (!double.IsFinite(placedArea) || placedArea <= Geometry.Epsilon ||
                Geometry.HasSelfIntersections(placed.Points, true, cancellationToken))
                throw NestingPieceError.Create(entry.Piece.Id,
                    "Parçanın yerleştirilen konturu hassasiyet nedeniyle geçersiz; yerleşim uygulanmadı.", "pieces");
            // At large coordinates floating-point rounding must not silently move a
            // piece beyond the requested fabric boundary. Reject an uncertain fit.
            var placedBounds = Geometry.Bounds(placed.Points);
            fabricEnd = AddClearance(placedBounds.MaxY, edgeMargin);
            if (!double.IsFinite(fabricEnd) || fabricEnd > Math.Min(maxLength, MaximumDimension))
                throw LengthExceeded();
            if (placedBounds.MinX < edgeMargin || !ShelfLayoutVerifier.DifferenceAtLeast(fabricWidth, placedBounds.MaxX, edgeMargin) ||
                placedBounds.MinY < edgeMargin || !ShelfLayoutVerifier.DifferenceAtLeast(fabricEnd, placedBounds.MaxY, edgeMargin))
                throw ClearancePrecision("Kenar payı sayısal hassasiyetle korunamıyor; yerleşim uygulanmadı.");
            if ((previousMaxX is { } prevX && !ShelfLayoutVerifier.DifferenceAtLeast(placedBounds.MinX, prevX, gap)) ||
                (previousRowMaxY is { } prevY && !ShelfLayoutVerifier.DifferenceAtLeast(placedBounds.MinY, prevY, gap)))
                throw ClearancePrecision("Parça boşluğu sayısal hassasiyetle korunamıyor; yerleşim uygulanmadı.");
            layout.Pieces.Add(placed);
            previousMaxX = placedBounds.MaxX;
            currentRowMaxY = Math.Max(currentRowMaxY, placedBounds.MaxY);
            if (gap > 0)
            {
                var nextMinX = AddClearance(placedBounds.MaxX, gap);
                x = nextMinX - edgeMargin;
                // The subtraction and later addition of edgeMargin can round
                // back inside the required clearance. Move the target outward
                // once before converting back to the local shelf coordinate.
                if (double.IsFinite(nextMinX) &&
                    !ShelfLayoutVerifier.DifferenceAtLeast(x + edgeMargin, placedBounds.MaxX, gap))
                    x = Math.BitIncrement(nextMinX) - edgeMargin;
            }
            else x += entry.Bounds.Width;
            rowHeight = Math.Max(rowHeight, entry.Bounds.Height);
            usedLength = Math.Max(usedLength, fabricEnd);
            totalArea += entry.Area;
        }
        cancellationToken.ThrowIfCancellationRequested();
        // An empty job consumes no fabric; no waste is incurred.
        var utilization = usedLength == 0 ? 0 : Math.Clamp(100 * totalArea / (fabricWidth * usedLength), 0, 100);
        return new NestingResult(layout, usedLength, utilization, usedLength == 0 ? 0 : 100 - utilization)
        { FabricWidth = fabricWidth, Gap = gap, EdgeMargin = edgeMargin };
    }
}

