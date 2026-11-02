using System.Globalization;
using System.Numerics;
using System.Security;
using System.Text;
using TekstilCAD.Core;

namespace TekstilCAD.FileFormats;

// Marker semantics are not represented by these exporters. Never silently omit them.
internal static class NotchExportGuard
{
    internal static void EnsureSupported(ProjectDocument project)
    {
        PhysicalExportGuard.EnsureSupported(project);
        if (project.Pieces.Any(piece => piece.Notches is { Count: > 0 }))
            throw new InvalidDataException("Çentik işaretleri bu çıktı biçimine henüz aktarılamıyor; veri kaybını önlemek için dosya hazırlanmadı.");
    }
}

internal static class PhysicalExportGuard
{
    internal static void EnsureSupported(ProjectDocument project)
    {
        if(project.Pieces.Any(piece=>piece.PhysicalFeatures is not null))
            throw new InvalidDataException("Fiziksel kontur kimlikleri bu çıktı biçiminde korunamıyor; dosya hazırlanmadı.");
    }
}

// SVG supports grainline output; other formats reject silent omission.
internal static class GrainlineExportGuard
{
    internal static void EnsureSupported(ProjectDocument project)
    {
        if (project.Pieces.Any(piece => piece.Grainline is not null))
            throw new InvalidDataException("İplik yönü bilgisi bu çıktı biçimine henüz aktarılamıyor; veri kaybını önlemek için dosya hazırlanmadı. Projeyi TekstilCAD biçiminde saklayın.");
    }
}

public static class SvgFormat
{
    private static string F(double n) => n.ToString("G17", CultureInfo.InvariantCulture);
    public static void Export(ProjectDocument project, string path) => ExportCore(project, path, null);
    public static void Export(ProjectDocument project, string path, ProjectFileVersion expectedVersion)
        => ExportCore(project, path, expectedVersion ?? throw new ArgumentNullException(nameof(expectedVersion)));
    private static void ExportCore(ProjectDocument project, string path, ProjectFileVersion? expectedVersion)
    {
        ProjectStorage.Validate(project);
        NotchExportGuard.EnsureSupported(project);
        // The caller must not mutate the source while this detached snapshot is
        // captured. All bounds and XML below use exactly this captured content.
        project = project.Clone();
        ProjectStorage.Validate(project);
        NotchExportGuard.EnsureSupported(project);
        if (project.Pieces.Count == 0)
            throw new InvalidDataException("SVG dışa aktarımı için en az bir parça gerekir.");
        if (project.Pieces.Any(p => p.Points.Count < 2))
            throw new InvalidDataException("SVG dışa aktarımı için her parçada en az iki nokta gerekir.");
        foreach (var piece in project.Pieces)
        {
            try { System.Xml.XmlConvert.VerifyXmlChars(piece.Name); }
            catch (System.Xml.XmlException ex) { throw new InvalidDataException("Parça adı SVG için geçersiz karakter içeriyor.", ex); }
        }
        var all = project.Pieces.SelectMany(p => p.Points).ToList();
        foreach (var piece in project.Pieces)
            if (piece.Grainline is { } grain)
            {
                all.Add(grain.Start);
                all.Add(grain.End);
            }
        // The stroke is centered on the contour. Reserve half its physical width
        // on every edge so the viewport does not clip the outer half of the ink.
        const double strokeWidth = 0.25, margin = strokeWidth / 2;
        var minX = all.Min(p => p.X) - margin; var minY = all.Min(p => p.Y) - margin;
        var width = Math.Max(1, all.Max(p => p.X) - minX + margin);
        var height = Math.Max(1, all.Max(p => p.Y) - minY + margin);
        var b = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}mm\" height=\"{F(height)}mm\" viewBox=\"{F(minX)} {F(minY)} {F(width)} {F(height)}\">\n");
        foreach (var p in project.Pieces.Where(p => p.Points.Count > 0))
        {
            var data = "M " + string.Join(" L ", p.Points.Select(pt => F(pt.X) + " " + F(pt.Y))) + (p.Closed ? " Z" : "");
            b.Append($"<path d=\"{data}\" fill=\"none\" stroke=\"black\" stroke-width=\"0.25\" stroke-linejoin=\"round\" stroke-linecap=\"round\"><title>{SecurityElement.Escape(p.Name)}</title></path>\n");
        }
        if (project.Pieces.Any(p => p.Grainline is not null))
        {
            // The group and data attributes are TekstilCAD-specific information,
            // not a claim that third-party CAD or plotters understand grainlines.
            b.Append("<g id=\"tekstilcad-grainlines\" data-tekstilcad-role=\"grainline\" fill=\"none\" stroke=\"#147a94\" stroke-width=\"0.25\" stroke-linecap=\"round\" stroke-dasharray=\"2 1\">\n");
            foreach (var p in project.Pieces)
            {
                if (p.Grainline is not { } g) continue;
                var review = g.NeedsReview ? "true" : "false";
                b.Append($"<line data-piece-id=\"{p.Id:D}\" data-needs-review=\"{review}\" x1=\"{F(g.Start.X)}\" y1=\"{F(g.Start.Y)}\" x2=\"{F(g.End.X)}\" y2=\"{F(g.End.Y)}\"><title>İplik yönü · {SecurityElement.Escape(p.Name)}</title></line>\n");
            }
            b.Append("</g>\n");
        }
        b.Append("</svg>\n");
        if (expectedVersion is null) ProjectStorage.AtomicWrite(path, b.ToString());
        else ProjectStorage.AtomicWriteIfUnchanged(path, b.ToString(), expectedVersion);
    }
}

/// <summary>A captured source coordinate in output millimetres and its actual integer HPGL coordinate.</summary>
public readonly record struct HpglPreviewPoint(double SourceX,double SourceY,int PlotX,int PlotY);

/// <summary>Immutable bounded geometry for one piece; source coordinates already include any simulator scale.</summary>
public sealed class HpglPieceGeometryPreview
{
    internal HpglPieceGeometryPreview(Guid id,bool closed,double units,HpglPreviewPoint[] points)
    { PieceId=id;Closed=closed;UnitsPerMillimeter=units;Points=Array.AsReadOnly(points); }
    public Guid PieceId { get; }
    public bool Closed { get; }
    public double UnitsPerMillimeter { get; }
    public IReadOnlyList<HpglPreviewPoint> Points { get; }
}

/// <summary>An immutable file-only HPGL result and informational reviews for the same captured source.</summary>
public sealed class HpglPreparedOutput
{
    // Compact immutable indexes derive from the private captured geometry;
    // neither the dictionary nor its arrays are exposed to callers.
    private readonly Dictionary<Guid,HpglTurnReview.Index>? reviewIndexes;
    private readonly Dictionary<Guid,HpglPieceGeometryPreview>? geometryPreviews;
    internal HpglPreparedOutput(string text,double units,IReadOnlyList<HpglTurnReview.Report> reviews,
        Dictionary<Guid,HpglTurnReview.Index>? reviewIndexes,Dictionary<Guid,HpglPieceGeometryPreview>? geometryPreviews)
    { Text=text;UnitsPerMillimeter=units;TurnReviews=reviews;this.reviewIndexes=reviewIndexes;this.geometryPreviews=geometryPreviews; }
    public string Text { get; }
    public double UnitsPerMillimeter { get; }
    /// <summary>Informational only; not a guarantee of geometry or machine compatibility.</summary>
    public IReadOnlyList<HpglTurnReview.Report> TurnReviews { get; }

    /// <summary>Returns one piece's immutable captured source and encoded integer coordinates.
    /// The list has at most 10,000 points and does not certify machine compatibility.</summary>
    public HpglPieceGeometryPreview GetPieceGeometry(Guid pieceId,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(geometryPreviews is null || !geometryPreviews.TryGetValue(pieceId,out var geometry))
            throw new KeyNotFoundException("Geometrisi istenen parça hazırlanan HPGL kaynağında bulunamadı.");
        cancellationToken.ThrowIfCancellationRequested();
        return geometry;
    }

    /// <summary>Reexamines one captured piece with bounded examples; no files are read or written.
    /// This informational report does not establish feature preservation or machine compatibility.</summary>
    public HpglTurnReview.Report ReviewPiece(Guid pieceId,int sampleLimit=20,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(reviewIndexes is null || !reviewIndexes.TryGetValue(pieceId,out var index))
            throw new KeyNotFoundException("İncelenecek parça hazırlanan HPGL kaynağında bulunamadı.");
        return index.Review(0,sampleLimit,cancellationToken);
    }

    /// <summary>Reexamines one captured piece and returns a bounded page of changed turns.</summary>
    public HpglTurnReview.Page ReviewPiecePage(Guid pieceId,int offset=0,int pageSize=20,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(reviewIndexes is null || !reviewIndexes.TryGetValue(pieceId,out var index))
            throw new KeyNotFoundException("İncelenecek parça hazırlanan HPGL kaynağında bulunamadı.");
        return index.ReviewPage(offset,pageSize,cancellationToken);
    }
}

public static class HpglFormat
{
    // Includes the PD prefix and terminating semicolon. This bounds individual
    // commands; a target may still impose a smaller limit or other restrictions.
    public const int MaximumPenDownCommandBytes = 128;
    internal readonly record struct PlotPoint(int X, int Y)
    {
        public string Coordinates => X.ToString(CultureInfo.InvariantCulture) + "," + Y.ToString(CultureInfo.InvariantCulture);
    }

    // Shared with the file-free review so it observes the encoder's exact rounding.
    internal static PlotPoint Quantize(Point2 p,double unitsPerMillimeter)
    {
        var x=p.X*unitsPerMillimeter;var y=p.Y*unitsPerMillimeter;
        if(!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x)>int.MaxValue || Math.Abs(y)>int.MaxValue)
            throw new InvalidDataException("HPGL koordinat sınırı aşıldı.");
        return new PlotPoint((int)Math.Round(x),(int)Math.Round(y));
    }

    /// <summary>File-only pen plot export. Does not send to physical equipment.</summary>
    public static void Export(ProjectDocument project, string path, double unitsPerMillimeter = 40)
        => ExportCore(project, path, unitsPerMillimeter, null, CancellationToken.None);

    /// <summary>Cancellation is observed before the atomic file replacement; it cannot roll back a completed replacement.</summary>
    public static void Export(ProjectDocument project, string path, double unitsPerMillimeter, CancellationToken cancellationToken)
        => ExportCore(project, path, unitsPerMillimeter, null, cancellationToken);

    public static void Export(ProjectDocument project, string path, double unitsPerMillimeter, ProjectFileVersion expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(expectedVersion);
        ExportCore(project, path, unitsPerMillimeter, expectedVersion, CancellationToken.None);
    }

    public static void Export(ProjectDocument project, string path, double unitsPerMillimeter, ProjectFileVersion expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedVersion);
        ExportCore(project, path, unitsPerMillimeter, expectedVersion, cancellationToken);
    }

    /// <summary>
    /// Captures and prepares a file-only output and bounded turn reviews without writing.
    /// Caller must prevent source mutation until capture finishes. Reviews describe
    /// the captured source and units only; they are not a production certificate.
    /// Validation observes cancellation; source cloning remains non-interruptible.
    /// </summary>
    public static HpglPreparedOutput Prepare(ProjectDocument project,double unitsPerMillimeter=40,CancellationToken cancellationToken=default)
        => PrepareCore(project,unitsPerMillimeter,true,cancellationToken);

    /// <summary>Writes exactly the prepared content if the observed destination version remains unchanged.</summary>
    public static void WritePrepared(HpglPreparedOutput prepared,string path,ProjectFileVersion expectedVersion,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(prepared);ArgumentNullException.ThrowIfNull(expectedVersion);
        ProjectStorage.AtomicWriteIfUnchanged(path,prepared.Text,expectedVersion,cancellationToken);
    }

    private static void ExportCore(ProjectDocument project, string path, double unitsPerMillimeter, ProjectFileVersion? expectedVersion, CancellationToken cancellationToken)
    {
        var prepared=PrepareCore(project,unitsPerMillimeter,false,cancellationToken);
        if(expectedVersion is null)ProjectStorage.AtomicWrite(path,prepared.Text,cancellationToken);
        else ProjectStorage.AtomicWriteIfUnchanged(path,prepared.Text,expectedVersion,cancellationToken);
    }
    internal static HpglPreparedOutput PrepareForVerification(ProjectDocument project,double unitsPerMillimeter,CancellationToken cancellationToken,Action<int,ProjectDocument> beforeValidation)
        => PrepareCore(project,unitsPerMillimeter,true,cancellationToken,beforeValidation);
    private static HpglPreparedOutput PrepareCore(ProjectDocument project,double unitsPerMillimeter,bool includeReviews,CancellationToken cancellationToken,Action<int,ProjectDocument>? beforeValidationForVerification=null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        beforeValidationForVerification?.Invoke(1,project);
        ProjectStorage.Validate(project,cancellationToken);
        GrainlineExportGuard.EnsureSupported(project);
        NotchExportGuard.EnsureSupported(project);
        cancellationToken.ThrowIfCancellationRequested();
        // Validate before cloning to bound resource use, then work only on a
        // detached copy. Caller must keep the source stable through capture.
        project=project.Clone();
        cancellationToken.ThrowIfCancellationRequested();
        beforeValidationForVerification?.Invoke(2,project);
        ProjectStorage.Validate(project,cancellationToken);
        GrainlineExportGuard.EnsureSupported(project);
        NotchExportGuard.EnsureSupported(project);
        cancellationToken.ThrowIfCancellationRequested();
        if (project.Pieces.Count == 0)
            throw new InvalidDataException("HPGL dışa aktarımı için en az bir parça gerekir.");
        if (project.Pieces.Any(p => p.Points.Count < 2))
            throw new InvalidDataException("HPGL dışa aktarımı için her parçada en az iki nokta gerekir.");
        if (!double.IsFinite(unitsPerMillimeter) || unitsPerMillimeter <= 0) throw new ArgumentOutOfRangeException(nameof(unitsPerMillimeter));
        // Bound the total quadratic intersection work across the entire job, not
        // merely each contour. Raw edge counts conservatively include duplicates.
        const long maxJobComparisons = 50_000_000;
        long comparisons = 0;
        foreach (var piece in project.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long edges = piece.Closed ? piece.Points.Count : piece.Points.Count - 1;
            comparisons += edges * (edges - 1) / 2;
            if (comparisons > maxJobComparisons)
                throw new InvalidDataException("HPGL işi toplam kontur doğrulama sınırını aşıyor; daha küçük iş grupları kullanın.");
        }
        var b = new StringBuilder("IN;SP1;PA;\n");
        var reviews=new List<HpglTurnReview.Report>();var remainingSamples=100;
        var reviewIndexes=includeReviews ? new Dictionary<Guid,HpglTurnReview.Index>(project.Pieces.Count) : null;
        var geometryPreviews=includeReviews ? new Dictionary<Guid,HpglPieceGeometryPreview>(project.Pieces.Count) : null;
        foreach (var p in project.Pieces.Where(p => p.Points.Count >= 2))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var points = new PlotPoint[p.Points.Count];
            for (var i = 0; i < points.Length; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                points[i] = Quantize(p.Points[i],unitsPerMillimeter);
            }
            ValidateQuantizedContour(p, points, cancellationToken);
            if(includeReviews)
            {
                var (review,index)=HpglTurnReview.AnalyzeWithIndex(p,unitsPerMillimeter,Math.Min(10,remainingSamples),cancellationToken);
                remainingSamples-=review.Samples.Count;reviews.Add(review);reviewIndexes!.Add(p.Id,index);
                var previewPoints=new HpglPreviewPoint[points.Length];
                for(var i=0;i<points.Length;i++)
                {
                    if((i&255)==0)cancellationToken.ThrowIfCancellationRequested();
                    previewPoints[i]=new HpglPreviewPoint(p.Points[i].X,p.Points[i].Y,points[i].X,points[i].Y);
                }
                geometryPreviews!.Add(p.Id,new HpglPieceGeometryPreview(p.Id,p.Closed,unitsPerMillimeter,previewPoints));
            }
            b.Append("PU").Append(points[0].Coordinates).Append(";PD");
            var commandLength = 2;
            var pairsInCommand = 0;
            void AppendPenDown(PlotPoint point)
            {
                var coordinates = point.Coordinates;
                var separatorLength = pairsInCommand == 0 ? 0 : 1;
                if (commandLength + separatorLength + coordinates.Length + 1 > MaximumPenDownCommandBytes)
                {
                    b.Append(";PD");
                    commandLength = 2;
                    pairsInCommand = 0;
                    separatorLength = 0;
                }
                if (separatorLength != 0) b.Append(',');
                b.Append(coordinates);
                commandLength += separatorLength + coordinates.Length;
                pairsInCommand++;
            }
            for (var i = 1; i < points.Length; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                AppendPenDown(points[i]);
            }
            if (p.Closed) AppendPenDown(points[0]);
            b.Append(";PU;\n");
        }
        b.Append("SP0;\n");
        cancellationToken.ThrowIfCancellationRequested();
        var text=b.ToString();cancellationToken.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        return new HpglPreparedOutput(text,unitsPerMillimeter,reviews.AsReadOnly(),reviewIndexes,geometryPreviews);
    }

    private static void ValidateQuantizedContour(PatternPiece piece, PlotPoint[] points, CancellationToken cancellationToken)
    {
        // Keep existing duplicate vertices (including an explicit closure), but do not
        // silently erase a genuine edge when converting millimetres to plotter units.
        var edgeCount = piece.Closed ? points.Length : points.Length - 1;
        for (var i = 0; i < edgeCount; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var next = (i + 1) % points.Length;
            if (piece.Points[i] != piece.Points[next] && points[i] == points[next])
                throw new InvalidDataException($"“{piece.Name}” parçasının {i + 1}. kenarı HPGL çözünürlüğünde kayboluyor. Çıktı oluşturulmadı; kalıp ayrıntısını ve hedef cihazın birim ayarını kontrol edin.");
        }
        var originalByOutput=new Dictionary<PlotPoint,Point2>();
        for(var i=0;i<points.Length;i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            if(originalByOutput.TryGetValue(points[i],out var original) && original!=piece.Points[i])
                throw new InvalidDataException($"“{piece.Name}” parçasındaki farklı noktalar HPGL çözünürlüğünde birleşiyor. Çıktı oluşturulmadı; ayrıntıları ve hedef birim ayarını kontrol edin.");
            originalByOutput[points[i]]=piece.Points[i];
        }
        if (!piece.Closed)
        {
            if (points.All(point => point == points[0]))
                throw new InvalidDataException($"“{piece.Name}” parçası HPGL çözünürlüğünde çizilebilir bir kenar içermiyor. Çıktı oluşturulmadı.");
            ValidatePlotEdges(piece, points, cancellationToken);
            return;
        }

        // Exact integer area avoids cancellation near the coordinate limit and
        // overflow when a contour contains thousands of large plotter coordinates.
        Int128 twiceArea = 0;
        for (var i = 0; i < points.Length; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var next = points[(i + 1) % points.Length];
            twiceArea += (Int128)points[i].X * next.Y - (Int128)next.X * points[i].Y;
        }
        if (twiceArea == 0)
            throw new InvalidDataException($"“{piece.Name}” kapalı konturu HPGL çözünürlüğünde alan oluşturmuyor. Çıktı oluşturulmadı; kalıp ayrıntısını ve hedef cihazın birim ayarını kontrol edin.");
        var exactSource=ExactCoordinates(piece.Points,cancellationToken);
        var sourceOrientation = ExactSourceOrientation(exactSource, cancellationToken);
        if (sourceOrientation == 0 || sourceOrientation != (twiceArea > 0 ? 1 : -1))
            throw new InvalidDataException($"“{piece.Name}” kapalı konturunun yönü HPGL çözünürlüğünde korunmuyor veya kaynak alanı sıfır. Çıktı oluşturulmadı; kalıp ayrıntısını ve hedef cihazın birim ayarını kontrol edin.");
        ValidatePlotEdges(piece, points, cancellationToken);
    }

    // Evaluate the signed area of the stored IEEE-754 coordinates exactly as
    // dyadic rationals. A floating-point shoelace sum can give a wrong sign for
    // small contours at large origins or nearly cancelling contours.
    private static int ExactSourceOrientation((BigInteger X,BigInteger Y)[] coordinates, CancellationToken cancellationToken)
    {
        var originX=coordinates[0].X;var originY=coordinates[0].Y;
        BigInteger area=BigInteger.Zero;
        for(var i=1;i<coordinates.Length-1;i++)
        {
            if((i & 255)==0)cancellationToken.ThrowIfCancellationRequested();
            var ax=coordinates[i].X-originX;var ay=coordinates[i].Y-originY;
            var bx=coordinates[i+1].X-originX;var by=coordinates[i+1].Y-originY;
            area+=ax*by-ay*bx;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return area.Sign;
    }
    internal static (BigInteger X,BigInteger Y)[] ExactCoordinates(IReadOnlyList<Point2> points,CancellationToken cancellationToken)
    {
        static (BigInteger Mantissa, int Exponent) Dyadic(double value)
        {
            var bits=BitConverter.DoubleToInt64Bits(value);
            var exponent=(int)((bits >> 52) & 0x7ff);
            var fraction=(ulong)bits & 0x000fffffffffffffUL;
            var mantissa=new BigInteger(exponent==0 ? fraction : fraction | (1UL << 52));
            if(bits<0)mantissa=-mantissa;
            return (mantissa,exponent==0 ? -1074 : exponent-1075);
        }
        var coordinates=new ((BigInteger Mantissa,int Exponent) X,(BigInteger Mantissa,int Exponent) Y)[points.Count];
        var minimumExponent=int.MaxValue;
        for(var i=0;i<points.Count;i++)
        {
            if((i & 255)==0)cancellationToken.ThrowIfCancellationRequested();
            var x=Dyadic(points[i].X);var y=Dyadic(points[i].Y);
            coordinates[i]=(x,y);
            if(!x.Mantissa.IsZero)minimumExponent=Math.Min(minimumExponent,x.Exponent);
            if(!y.Mantissa.IsZero)minimumExponent=Math.Min(minimumExponent,y.Exponent);
        }
        if(minimumExponent==int.MaxValue)return new (BigInteger X,BigInteger Y)[points.Count];
        static BigInteger Integer((BigInteger Mantissa,int Exponent) value,int exponent)
            => value.Mantissa.IsZero ? BigInteger.Zero : value.Mantissa << (value.Exponent-exponent);
        var result=new (BigInteger X,BigInteger Y)[points.Count];
        for(var i=0;i<points.Count;i++)
        {
            if((i & 255)==0)cancellationToken.ThrowIfCancellationRequested();
            result[i]=(Integer(coordinates[i].X,minimumExponent),Integer(coordinates[i].Y,minimumExponent));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    // Integer predicates avoid double rounding or overflow near the HPGL coordinate limit.
    // Consecutive repeated vertices carry no edge; explicit closure remains supported.
    private static void ValidatePlotEdges(PatternPiece piece, PlotPoint[] points, CancellationToken cancellationToken)
    {
        var quantized = new HpglIntersectionDiagnostic.Point[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            quantized[i] = new(points[i].X, points[i].Y);
        }
        try { HpglIntersectionDiagnostic.Validate(quantized, piece.Closed, cancellationToken: cancellationToken); }
        catch (InvalidDataException ex)
        { throw new InvalidDataException($"“{piece.Name}” parçası HPGL için doğrulanamadı: {ex.Message} Çıktı oluşturulmadı.", ex); }
    }
}
