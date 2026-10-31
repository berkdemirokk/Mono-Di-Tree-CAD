using TekstilCAD.Core;
using Clipper2Lib;
namespace TekstilCAD.Patterns;

/// <summary>Millimetre pattern operations.</summary>
public static class PatternTools
{
    private const double OffsetGridScale = 1_000_000;

    /// <summary>Returns the grid-rounded allowance in millimetres used by Offset.</summary>
    public static double QuantizeAllowance(double mm) => AllowanceGridUnits(mm) / OffsetGridScale;

    private static double AllowanceGridUnits(double mm)
    {
        if (!double.IsFinite(mm) || mm < 0 || mm > 100_000) throw new ArgumentOutOfRangeException(nameof(mm));
        if (mm == 0) return 0;
        if (mm < 1 / OffsetGridScale) throw new ArgumentException("Dikiş payı en az 0,000001 mm olmalıdır.");
        return Math.Round(mm * OffsetGridScale, MidpointRounding.AwayFromZero);
    }
    /// <summary>Outward offset of a simple, closed straight-edge contour, on a 0.000001 mm grid.
    /// Rejects some unsafe corner and topology changes; experimental checks do not guarantee preservation of every feature.
    /// A source grainline is copied independently at the same coordinates and marked for review, including for a zero allowance.</summary>
    public static PatternPiece Offset(PatternPiece piece, double mm)
        => Offset(piece, mm, CancellationToken.None);

    /// <summary>Cooperatively cancels between processing steps. Individual geometry validation and
    /// Clipper calls are synchronous and cannot be interrupted while running.</summary>
    public static PatternPiece Offset(PatternPiece piece, double mm, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSource(piece);
        ValidateDerivedGrainline(piece);
        if (!double.IsFinite(mm) || mm < 0 || mm > 100_000) throw new ArgumentOutOfRangeException(nameof(mm));
        if (!piece.Closed) throw new ArgumentException("Dikiş payı için kapalı kontur gereklidir.");
        cancellationToken.ThrowIfCancellationRequested();
        Geometry.ValidateContour(piece.Points, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var points = piece.Points.ToList();
        if (mm == 0) { cancellationToken.ThrowIfCancellationRequested(); return NewPiece(piece, points, " · dikiş payı 0 mm", preserveGrainline:true, markGrainlineForReview:true); }
        if (points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        const double scale = OffsetGridScale;
        var delta = AllowanceGridUnits(mm);
        var path = new Path64();
        var distinct = new HashSet<(long, long)>();
        var quantized = new List<Point2>();
        foreach (var point in points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var x = checked((long)Math.Round(point.X * scale, MidpointRounding.AwayFromZero));
            var y = checked((long)Math.Round(point.Y * scale, MidpointRounding.AwayFromZero));
            if (!distinct.Add((x, y))) throw new ArgumentException("Noktalar 0,000001 mm hassasiyette birleşiyor; konturu düzeltin.");
            path.Add(new Point64(x, y)); quantized.Add(new Point2(x / scale, y / scale));
        }
        cancellationToken.ThrowIfCancellationRequested();
        Geometry.ValidateContour(quantized, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var orientation = Math.Sign(Geometry.SignedArea(quantized));
        for (var i = 0; i < quantized.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = quantized[(i + quantized.Count - 1) % quantized.Count];
            var b = quantized[i]; var c = quantized[(i + 1) % quantized.Count];
            var u = b - a; var v = c - b;
            var dot = (u.X * v.X + u.Y * v.Y) / (Geometry.Distance(a, b) * Geometry.Distance(b, c));
            // The later inverse-offset comparison allows eight grid units of error.
            // A sharp concave feature with an incident edge that short can disappear
            // entirely inside that allowance. Refuse this uncertain case rather than
            // treating the proximity check as evidence that the feature survived.
            // Integer coordinate differences avoid losing the short edge at large origins.
            var ga = path[(i + path.Count - 1) % path.Count];
            var gb = path[i]; var gc = path[(i + 1) % path.Count];
            double ux = gb.X - ga.X, uy = gb.Y - ga.Y;
            double vx = gc.X - gb.X, vy = gc.Y - gb.Y;
            var ul2 = ux * ux + uy * uy; var vl2 = vx * vx + vy * vy;
            if ((ul2 <= 64 || vl2 <= 64) && (ux * vy - uy * vx) * orientation < 0
                && ux * vx + uy * vy < 0.5 * Math.Sqrt(ul2 * vl2))
                throw new ArgumentException("Çok küçük keskin girinti dikiş payında kaybolabilir; işlem uygulanmadı.");
            if (Cross(u, v) * orientation > 0 && dot < -0.5 + 1e-12)
                throw new ArgumentException("Dar köşe dikiş payı sınırında kesileceği için işlem uygulanmadı.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = Clipper.InflatePaths(new Paths64 { path }, delta, JoinType.Miter, EndType.Polygon, 2.0);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Count != 1) throw new ArgumentException("Dikiş payı tek bir dış kontur oluşturmadı; sonuç uygulanmadı.");
        // Inverting a safe offset should recover the source within grid-rounding error.
        // Larger symmetric differences expose collapsed recesses or altered topology.
        cancellationToken.ThrowIfCancellationRequested();
        var recovered = Clipper.InflatePaths(result, -delta, JoinType.Miter, EndType.Polygon, 2.0);
        cancellationToken.ThrowIfCancellationRequested();
        if (recovered.Count != 1) throw new ArgumentException("Dikiş payı kontur yapısını değiştiriyor; işlem uygulanmadı.");
        // A global area budget can hide a small lost notch on a large perimeter.
        // Check local vertices and edge midpoints in both directions, in grid units.
        // This is a conservative sampling check, not a proof of Hausdorff distance.
        if (!LocallyMatchesWithCancellation(path, recovered[0], 8.0, cancellationToken) || !LocallyMatchesWithCancellation(recovered[0], path, 8.0, cancellationToken))
            throw new OffsetLocalDetailUnverifiedException();
        cancellationToken.ThrowIfCancellationRequested();
        var difference = Clipper.Xor(new Paths64 { path }, recovered, FillRule.NonZero);
        cancellationToken.ThrowIfCancellationRequested();
        var differenceArea = difference.Sum(p => Math.Abs(Clipper.Area(p)));
        double perimeter = 0;
        for (int i = 0; i < quantized.Count; i++) { cancellationToken.ThrowIfCancellationRequested(); perimeter += Geometry.Distance(quantized[i], quantized[(i + 1) % quantized.Count]) * scale; }
        if (differenceArea > perimeter * 8 + 64)
            throw new ArgumentException("Dikiş payı girintiyi kapatıyor veya kontur şeklini değiştiriyor; işlem uygulanmadı.");
        var outline = result[0].Select(p => new Point2(p.X / scale, p.Y / scale)).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        Geometry.ValidateContour(outline, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return NewPiece(piece, outline, $" · dikiş payı istenen {mm:G} mm, hesaplanan {delta / scale:G} mm", preserveGrainline:true, markGrainlineForReview:true);
    }

    /// <summary>Outward miter offset. Negative allowance, concave/collinear vertices and extreme miters are rejected.</summary>
    public static PatternPiece OffsetConvex(PatternPiece piece, double mm)
    {
        ValidateSource(piece);
        ValidateDerivedGrainline(piece);
        if (!double.IsFinite(mm) || mm < 0 || mm > 100_000) throw new ArgumentOutOfRangeException(nameof(mm));
        if (!piece.Closed) throw new ArgumentException("Dikiş payı için kapalı kontur gereklidir.");
        Geometry.ValidateContour(piece.Points);
        var points = piece.Points.ToList();
        if (points[0] == points[^1]) points.RemoveAt(points.Count - 1);
        var orientation = Math.Sign(Geometry.SignedArea(points));
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[(i + points.Count - 1) % points.Count];
            var b = points[i]; var c = points[(i + 1) % points.Count];
            if (Cross(b - a, c - b) * orientation <= Geometry.Epsilon)
                throw new ArgumentException("Bu sürüm yalnızca kesin dışbükey konturları destekler; içbükey veya doğrusal köşe bulundu.");
        }
        if (mm == 0) return NewPiece(piece, points, " · dikiş payı 0 mm", preserveGrainline:true, markGrainlineForReview:true);
        var result = new List<Point2>();
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[(i + points.Count - 1) % points.Count]; var b = points[i]; var c = points[(i + 1) % points.Count];
            var u = b - a; var v = c - b;
            var ul = Geometry.Distance(a, b); var vl = Geometry.Distance(b, c);
            var n1 = new Point2(orientation * u.Y / ul, -orientation * u.X / ul);
            var n2 = new Point2(orientation * v.Y / vl, -orientation * v.X / vl);
            var q = b + n1 * mm; var r = b + n2 * mm;
            var determinant = Cross(u, v);
            var intersection = q + u * (Cross(r - q, v) / determinant);
            Geometry.Validate(intersection);
            if (Geometry.Distance(intersection, b) > mm * 1000)
                throw new ArgumentException("Çok dar köşe güvenli dikiş payı sınırını aşıyor.");
            result.Add(intersection);
        }
        Geometry.ValidateContour(result);
        return NewPiece(piece, result, $" · dikiş payı {mm:g} mm", preserveGrainline:true, markGrainlineForReview:true);
    }

    // Subtract integer coordinates before conversion to retain precision at large origins.
    private static bool LocallyMatches(Path64 source, Path64 target, double tolerance)
        => LocallyMatchesWithCancellation(source, target, tolerance, CancellationToken.None);

    private static bool LocallyMatchesWithCancellation(Path64 source, Path64 target, double tolerance, CancellationToken cancellationToken)
    {
        if (source.Count > Geometry.MaxValidatedContourVertices || target.Count > Geometry.MaxValidatedContourVertices)
            throw new ArgumentException("Dikiş payı doğrulama nokta sınırını aşıyor.");
        bool Near(double x, double y, Point64 origin)
        {
            for(var j=0;j<target.Count;j++)
            {
                if ((j & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                var a=target[j]; var b=target[(j+1)%target.Count];
                double ax=a.X-origin.X, ay=a.Y-origin.Y, bx=b.X-origin.X, by=b.Y-origin.Y;
                var dx=bx-ax; var dy=by-ay; var length=dx*dx+dy*dy;
                var t=length==0 ? 0 : Math.Clamp(((x-ax)*dx+(y-ay)*dy)/length,0,1);
                var ex=x-(ax+t*dx); var ey=y-(ay+t*dy);
                if(ex*ex+ey*ey<=tolerance*tolerance)return true;
            }
            return false;
        }
        for(var i=0;i<source.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a=source[i];var b=source[(i+1)%source.Count];
            if(!Near(0,0,a) || !Near((b.X-a.X)/2.0,(b.Y-a.Y)/2.0,a))return false;
        }
        return true;
    }

    /// <summary>V notch with half-width=depth, apex on the left of directed a→b. Caller selects inward edge direction.</summary>
    public static List<Point2> NotchPoints(Point2 a, Point2 b, double alongFraction, double depth)
    {
        Geometry.Validate(a); Geometry.Validate(b);
        if (!double.IsFinite(alongFraction) || alongFraction <= 0 || alongFraction >= 1) throw new ArgumentOutOfRangeException(nameof(alongFraction));
        if (!double.IsFinite(depth) || depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));
        var length = Geometry.Distance(a, b);
        if (length <= Geometry.Epsilon || depth >= Math.Min(alongFraction, 1 - alongFraction) * length)
            throw new ArgumentException("Çentik kenarın dışına taşıyor veya kenar çok kısa.");
        var unit = (b - a) * (1 / length); var center = a + (b - a) * alongFraction;
        List<Point2> result = [center - unit * depth, center + new Point2(-unit.Y, unit.X) * depth, center + unit * depth];
        foreach (var point in result) Geometry.Validate(point);
        return result;
    }

    public static PatternPiece InsertPoint(PatternPiece piece, int afterIndex, Point2 point)
        => InsertPoint(piece, afterIndex, point, CancellationToken.None);

    /// <summary>Reverse an open path without changing any represented coordinates.
    /// The input and its point collection remain unchanged.</summary>
    public static PatternPiece ReverseOpenPath(PatternPiece piece,CancellationToken cancellationToken=default)
    {
        var points=SnapshotPointEdit(piece,cancellationToken,allowNotches:true);
        var marks=SnapshotNotchIndices(piece,points,cancellationToken);
        if(piece.Closed)throw new ArgumentException("Yalnız açık konturun yönü ters çevrilebilir.");
        ValidatePointEdit(false,points,cancellationToken);
        points.Reverse();
        if(marks is not null)for(var i=0;i<marks.Length;i++)marks[i]=points.Count-1-marks[i];
        cancellationToken.ThrowIfCancellationRequested();
        return NewPiece(piece,points,"",preserveGrainline:true,notchIndices:marks);
    }

    /// <summary>Splits the edge after the selected vertex at its exact binary64 midpoint.
    /// Rejects a midpoint that cannot be represented exactly, preserving the original edge.</summary>
    public static PatternPiece InsertEdgeMidpoint(PatternPiece piece,int afterIndex,CancellationToken cancellationToken = default)
    {
        var points=SnapshotPointEdit(piece,cancellationToken,allowNotches:true);
        _=SnapshotNotchIndices(piece,points,cancellationToken);
        if(afterIndex<0 || afterIndex>=points.Count)throw new ArgumentOutOfRangeException(nameof(afterIndex));
        if(piece.Closed && points.Count>1 && points[0]==points[^1] && afterIndex==points.Count-1)afterIndex=0;
        if(!piece.Closed && afterIndex==points.Count-1)throw new ArgumentException("Açık konturun son noktasından sonra kenar yok.");
        var next=afterIndex+1<points.Count ? afterIndex+1 : 0;
        var a=points[afterIndex];var b=points[next];
        // Valid CAD coordinates are bounded, so their sums cannot overflow.
        // Sum before halving: separate divisions can lose subnormal bits even
        // when the exact midpoint is representable. The exact check below
        // still rejects any rounded result.
        var midpoint=new Point2((a.X+b.X)/2,(a.Y+b.Y)/2);
        static System.Numerics.BigInteger Units(double value)
        {
            var bits=BitConverter.DoubleToInt64Bits(value);var exponent=(int)((bits>>52)&0x7ff);
            var fraction=bits&0x000f_ffff_ffff_ffffL;
            if(exponent==0x7ff)throw new ArgumentOutOfRangeException(nameof(value));
            var mantissa=exponent==0 ? fraction : fraction|(1L<<52);
            var result=new System.Numerics.BigInteger(mantissa) << (exponent==0 ? 0 : exponent-1);
            return bits<0 ? -result : result;
        }
        if(2*Units(midpoint.X)!=Units(a.X)+Units(b.X) || 2*Units(midpoint.Y)!=Units(a.Y)+Units(b.Y) || midpoint==a || midpoint==b)
            throw new ArgumentException("Kenarın orta noktası sayısal hassasiyetle tam temsil edilemiyor; geometri değiştirilmedi.");
        // InsertPoint snapshots again and applies the normal contour validation and limits.
        var detached=piece.Clone();detached.Points=points;
        return InsertPoint(detached,afterIndex,midpoint,cancellationToken);
    }

    /// <summary>Inserts into a detached contour. If the final stored point explicitly
    /// repeats the first point, its index identifies that same first vertex.
    /// Large contours must be processed off the UI thread; cancellation is cooperative.</summary>
    public static PatternPiece InsertPoint(PatternPiece piece, int afterIndex, Point2 point, CancellationToken cancellationToken)
    {
        var points = SnapshotPointEdit(piece, cancellationToken,allowNotches:true);
        var marks = SnapshotNotchIndices(piece,points,cancellationToken);
        Geometry.Validate(point);
        if (afterIndex < 0 || afterIndex >= points.Count) throw new ArgumentOutOfRangeException(nameof(afterIndex));
        if (points.Count >= LargeContourValidator.MaximumStoredPoints)
            throw new ArgumentException("Kontur 10.000 saklı nokta sınırını aşamaz.", nameof(piece));
        if (piece.Closed && points.Count > 1 && points[0] == points[^1] && afterIndex == points.Count - 1)
            afterIndex = 0;
        if(marks is not null)for(var i=0;i<marks.Length;i++)if(marks[i]>afterIndex)marks[i]++;
        points.Insert(afterIndex + 1, point);
        ValidatePointEdit(piece.Closed, points, cancellationToken);
        return NewPiece(piece, points, "", preserveGrainline:true,notchIndices:marks);
    }
    public static PatternPiece DeletePoint(PatternPiece piece, int index)
        => DeletePoint(piece, index, CancellationToken.None);

    /// <summary>Deletes a vertex from a detached contour. The two stored copies
    /// of an explicitly closed first vertex are removed together and the new
    /// first vertex is used to close the contour.</summary>
    public static PatternPiece DeletePoint(PatternPiece piece, int index, CancellationToken cancellationToken)
    {
        var points = SnapshotPointEdit(piece, cancellationToken,allowNotches:true);
        var marks = SnapshotNotchIndices(piece,points,cancellationToken);
        if (index < 0 || index >= points.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var repeatedClosure = piece.Closed && points.Count > 1 && points[0] == points[^1];
        var effectiveIndex=repeatedClosure && index==points.Count-1 ? 0 : index;
        if(marks is not null && marks.Contains(effectiveIndex))throw new ArgumentException("İşaretli nokta silinemez; önce işareti kaldırın.");
        if(marks is not null)for(var i=0;i<marks.Length;i++)if(marks[i]>effectiveIndex)marks[i]--;
        if (repeatedClosure && (index == 0 || index == points.Count - 1))
        {
            points.RemoveAt(points.Count - 1);
            points.RemoveAt(0);
            if (points.Count > 0) points.Add(points[0]);
        }
        else points.RemoveAt(index);
        ValidatePointEdit(piece.Closed, points, cancellationToken);
        return NewPiece(piece, points, "", preserveGrainline:true,notchIndices:marks);
    }

    // The caller must not mutate the source during snapshot creation. The edit
    // itself never mutates or retains the caller's point collection.
    private static List<Point2> SnapshotPointEdit(PatternPiece piece, CancellationToken token,bool allowNotches=false)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(!allowNotches)NotchSafety.RequireNoNotches(piece);
        token.ThrowIfCancellationRequested();
        if (piece.Points is null) throw new ArgumentException("Kaynak kontur noktaları gerekli.", nameof(piece));
        if (piece.Name is null || piece.Name.Length > 1024) throw new ArgumentException("Geçersiz kaynak parça adı.", nameof(piece));
        if (piece.Points.Count > LargeContourValidator.MaximumStoredPoints)
            throw new ArgumentException("Kaynak kontur 10.000 nokta sınırını aşıyor.", nameof(piece));
        var points = new List<Point2>(piece.Points.Count);
        for (var i = 0; i < piece.Points.Count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            points.Add(piece.Points[i]);
        }
        token.ThrowIfCancellationRequested();
        return points;
    }

    private static int[]? SnapshotNotchIndices(PatternPiece piece,IReadOnlyList<Point2> points,CancellationToken token)
    {
        if(piece.Notches is not { } notches)return null;
        if(notches.Count<1 || notches.Count>points.Count || notches.Count>LargeContourValidator.MaximumStoredPoints)
            throw new ArgumentException("İşaret listesi veya kaynak kontur geçersiz.");
        var result=new int[notches.Count];var seen=new HashSet<int>();
        for(var i=0;i<notches.Count;i++)
        {
            token.ThrowIfCancellationRequested();var mark=notches[i];
            if(mark is null || mark.VertexIndex<0 || mark.VertexIndex>=points.Count ||
               (piece.Closed && points.Count>1 && points[0]==points[^1] && mark.VertexIndex==points.Count-1) ||
               !seen.Add(mark.VertexIndex) || mark.Position!=points[mark.VertexIndex])
                throw new ArgumentException("İşaret indeksi veya kaynak köşe konumu geçersiz; işlem uygulanmadı.");
            result[i]=mark.VertexIndex;
        }
        return result;
    }

    private static void ValidatePointEdit(bool closed, List<Point2> points, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (points.Count > Geometry.MaxValidatedContourVertices)
        {
            if (closed) LargeContourValidator.Validate(points, token);
            else LargeOpenPathValidator.Validate(points, token);
        }
        else if (closed) Geometry.ValidateContour(points, token);
        else
        {
            if (points.Count < 2) throw new ArgumentException("Açık çizgide en az iki nokta gerekir.");
            foreach (var point in points) { token.ThrowIfCancellationRequested(); Geometry.Validate(point); }
            if (Geometry.HasSelfIntersections(points, false, token))
                throw new ArgumentException("Düzenleme sıfır uzunluklu, üst üste binen veya kesişen bir kenar oluşturuyor.");
        }
        token.ThrowIfCancellationRequested();
    }
    /// <summary>
    /// Moves one stored vertex in a detached copy and validates the resulting path.
    /// Explicitly repeated endpoints of a closed contour are moved together.
    /// More than 4096 stored points require the caller to run this synchronous,
    /// cancellable operation off the UI thread. The input must not be mutated while
    /// it is being copied. The caller retains responsibility for source identity,
    /// metadata, and checking whether its original document has changed.
    /// </summary>
    public static PatternPiece MovePoint(PatternPiece piece, int index, Point2 position, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(piece);
        cancellationToken.ThrowIfCancellationRequested();
        if (piece.Points is null) throw new ArgumentException("Kaynak kontur noktaları gerekli.", nameof(piece));
        if (piece.Name is null || piece.Name.Length > 1024) throw new ArgumentException("Geçersiz kaynak parça adı.", nameof(piece));
        if (piece.Points.Count > LargeContourValidator.MaximumStoredPoints)
            throw new ArgumentException("Kaynak kontur 10.000 nokta sınırını aşıyor.", nameof(piece));
        if (index < 0 || index >= piece.Points.Count) throw new ArgumentOutOfRangeException(nameof(index));
        Geometry.Validate(position);
        var points = new List<Point2>(piece.Points.Count);
        for (var i = 0; i < piece.Points.Count; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            points.Add(piece.Points[i]);
        }
        var marks=SnapshotNotchIndices(piece,points,cancellationToken);
        var repeatedClosure = piece.Closed && points.Count > 1 && points[0] == points[^1];
        points[index] = position;
        if (repeatedClosure && (index == 0 || index == points.Count - 1))
        { points[0] = position; points[^1] = position; }
        cancellationToken.ThrowIfCancellationRequested();
        if (points.Count > Geometry.MaxValidatedContourVertices)
        {
            if (piece.Closed) LargeContourValidator.Validate(points, cancellationToken);
            else LargeOpenPathValidator.Validate(points, cancellationToken);
        }
        else if (piece.Closed) Geometry.ValidateContour(points, cancellationToken);
        else
        {
            if (points.Count < 2) throw new ArgumentException("Açık çizgide en az iki nokta gerekir.");
            foreach (var point in points) { cancellationToken.ThrowIfCancellationRequested(); Geometry.Validate(point); }
            if (Geometry.HasSelfIntersections(points, false, cancellationToken))
                throw new ArgumentException("Nokta taşınamadı: açık çizgi sıfır uzunluklu, üst üste binen veya kesişen bir kenar oluşturuyor.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return NewPiece(piece, points, "", preserveGrainline:true,notchIndices:marks);
    }
    /// <summary>Samples a circular arc; endpoints are included. The returned contour is an open polyline approximation.</summary>
    public static List<Point2> SampleArc(Point2 center, double radius, double startDegrees, double sweepDegrees, int segments = 64)
    {
        Geometry.Validate(center);
        if (!double.IsFinite(radius) || radius <= 0 || !double.IsFinite(startDegrees) || !double.IsFinite(sweepDegrees) || Math.Abs(sweepDegrees) > 360 || sweepDegrees == 0 || segments < 1 || segments > 4096)
            throw new ArgumentOutOfRangeException(nameof(radius), "Geçersiz yay parametreleri.");
        var result = new List<Point2>();
        for (int i = 0; i <= segments; i++)
        {
            var angle = ((startDegrees % 360) + sweepDegrees * i / segments) * Math.PI / 180;
            var p = center + new Point2(Math.Cos(angle), Math.Sin(angle)) * radius;
            Geometry.Validate(p); result.Add(p);
        }
        return result;
    }
    private static void ValidateSource(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        NotchSafety.RequireNoNotches(piece);
        if (piece.Points is null) throw new ArgumentException("Kaynak kontur noktaları gerekli.", nameof(piece));
        if (piece.Name is null || piece.Name.Length > 1024) throw new ArgumentException("Geçersiz kaynak parça adı.", nameof(piece));
        // Editing must not copy or quadratically inspect unbounded input.
        if (piece.Points.Count > Geometry.MaxValidatedContourVertices + (piece.Closed ? 1 : 0))
            throw new ArgumentException("Kaynak kontur doğrulama nokta sınırını aşıyor.", nameof(piece));
    }
    private static void ValidateDerivedGrainline(PatternPiece source)
    {
        if(source.Grainline is not { } line)return;
        Geometry.Validate(line.Start);Geometry.Validate(line.End);
        if(line.Start==line.End)
            throw new ArgumentException("İplik yönünün başlangıç ve bitiş noktaları farklı olmalıdır.",nameof(source));
    }
    private static PatternPiece NewPiece(PatternPiece source, List<Point2> points, string suffix, bool preserveGrainline=false, bool markGrainlineForReview=false,int[]? notchIndices=null)
    {
        var name = source.Name + suffix;
        if (name.Length > 1024) throw new ArgumentException("Üretilen parça adı 1024 karakteri aşıyor; kaynak adını kısaltın.");
        var line=preserveGrainline ? source.Grainline?.Clone() : null;
        if(line is not null && (markGrainlineForReview || !source.Points.SequenceEqual(points)))line.NeedsReview=true;
        var marks=notchIndices?.Select(index=>new NotchMark { VertexIndex=index,Position=points[index] }).ToList();
        return new() { Name = name, Points = points, Closed = source.Closed, Grainline=line,Notches=marks };
    }
    private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
}
