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
        if (!OffsetSourceGridTurnGuard.PreservesTurns(points, path.Select(p => (p.X,p.Y)).ToArray(), cancellationToken))
            throw new ArgumentException("Izgara yuvarlaması kaynak konturun bir köşesini değiştiriyor; işlem uygulanmadı.");
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
        // Preview consumers also offer geometry-based measurement. Never expose
        // a boundary outside the coordinate range supported by those operations,
        // even when the later local-detail check would otherwise reject it.
        const long maxGridCoordinate = (long)(Geometry.MaxCoordinate * OffsetGridScale);
        foreach (var p in result[0])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (p.X < -maxGridCoordinate || p.X > maxGridCoordinate || p.Y < -maxGridCoordinate || p.Y > maxGridCoordinate)
                throw new ArgumentException("Dikiş payı desteklenen koordinat sınırını aşıyor; işlem uygulanmadı.");
        }
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
        {
            // The local check may return false just as cancellation is requested.
            // Do not replace that cancellation with a validation failure or copy
            // an entire potentially large boundary without checking the token.
            var preview = new List<Point2>(result[0].Count);
            foreach (var p in result[0])
            {
                cancellationToken.ThrowIfCancellationRequested();
                preview.Add(new Point2(p.X / scale, p.Y / scale));
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new OffsetLocalDetailUnverifiedException(preview);
        }
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
            // Positive allowance must move every convex miter. At large origins,
            // a tiny normal displacement may round back to the original binary64
            // coordinate; accepting it would silently return an unoffset corner.
            var displacement = intersection - b;
            // A nonzero displacement alone is insufficient: one of its normal
            // components can round away when coordinate magnitudes differ.
            // A convex outward miter must lie strictly outside both incident
            // source edge lines; an on-edge or inward result is unsafe.
            if (Cross(u, displacement) * orientation >= 0 || Cross(v, displacement) * orientation >= 0)
                throw new ArgumentException("Dikiş payı bu koordinatlarda sayısal olarak temsil edilemiyor; işlem uygulanmadı.");
            // Match the relative dimensional precision already required for
            // generated notch dimensions; positive movement by itself can be a
            // badly rounded fraction or multiple of the requested allowance.
            var firstOffset = -orientation * Cross(u, displacement) / ul;
            var secondOffset = -orientation * Cross(v, displacement) / vl;
            if (Math.Abs(firstOffset - mm) > mm * 1e-9 || Math.Abs(secondOffset - mm) > mm * 1e-9)
                throw new ArgumentException("Dikiş payı bu koordinatlarda sayısal olarak temsil edilemiyor; işlem uygulanmadı.");
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
        => NotchPoints(a, b, alongFraction, depth * 2, depth);

    public static List<Point2> NotchPoints(Point2 a, Point2 b, double alongFraction, double width, double depth)
    {
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Geometry.Validate(a); Geometry.Validate(b);
        if (!double.IsFinite(alongFraction) || alongFraction <= 0 || alongFraction >= 1) throw new ArgumentOutOfRangeException(nameof(alongFraction));
        if (!double.IsFinite(depth) || depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));
        var length = Geometry.Distance(a, b);
        if (length <= Geometry.Epsilon || width / 2 >= Math.Min(alongFraction, 1 - alongFraction) * length)
            throw new ArgumentException("Çentik kenarın dışına taşıyor veya kenar çok kısa.");
        var unit = (b - a) * (1 / length); var center = a + (b - a) * alongFraction;
        List<Point2> result = [center - unit * (width / 2), center + new Point2(-unit.Y, unit.X) * depth, center + unit * (width / 2)];
        foreach (var point in result) Geometry.Validate(point);
        var apexVector = result[1] - center;
        var firstMouth = result[0] - center; var secondMouth = result[2] - center;
        var halfWidth = width / 2;
        if (result[0] == center || result[2] == center || result[1] == center ||
            Math.Abs(Geometry.Distance(result[0], result[2]) - width) > width * 1e-9 ||
            Math.Abs(Geometry.Distance(center, result[1]) - depth) > depth * 1e-9 ||
            Math.Abs(Cross(unit, firstMouth)) > halfWidth * 1e-9 ||
            Math.Abs(Cross(unit, secondMouth)) > halfWidth * 1e-9 ||
            Math.Abs(unit.X * firstMouth.X + unit.Y * firstMouth.Y + halfWidth) > halfWidth * 1e-9 ||
            Math.Abs(unit.X * secondMouth.X + unit.Y * secondMouth.Y - halfWidth) > halfWidth * 1e-9 ||
            Math.Abs(unit.X * apexVector.X + unit.Y * apexVector.Y) > depth * 1e-9 ||
            Cross(unit, apexVector) <= 0)
            throw new ArgumentException("Çentik ölçüleri sayısal hassasiyetle temsil edilemiyor.");
        return result;
    }

    /// <summary>Cut an inward V notch into a closed contour edge. Half-width equals depth.</summary>
    public static PatternPiece InsertNotch(PatternPiece piece, int afterIndex, double fraction, double depth, CancellationToken cancellationToken = default)
        => InsertNotch(piece, afterIndex, fraction, depth * 2, depth, cancellationToken);

    public static PatternPiece InsertNotch(PatternPiece piece, int afterIndex, double fraction, double width, double depth, CancellationToken cancellationToken = default)
    {
        var points = SnapshotPointEdit(piece, cancellationToken);
        if (!piece.Closed) throw new ArgumentException("Çentik için kapalı kontur gereklidir.");
        if (afterIndex < 0 || afterIndex >= points.Count) throw new ArgumentOutOfRangeException(nameof(afterIndex));
        if (points.Count + 3 > LargeContourValidator.MaximumStoredPoints) throw new ArgumentException("Kontur nokta sınırı aşılıyor.");
        ValidatePointEdit(true, points, cancellationToken);
        if (points.Count > 1 && points[0] == points[^1] && afterIndex == points.Count - 1) afterIndex = 0;
        var next = afterIndex + 1 < points.Count ? afterIndex + 1 : 0;
        var sourceArea = Geometry.SignedArea(points);
        var notch = NotchPoints(points[afterIndex], points[next], fraction, width, depth);
        if (sourceArea < 0)
        {
            var center = points[afterIndex] + (points[next] - points[afterIndex]) * fraction;
            notch[1] = center - (notch[1] - center);
        }
        var edge = points[next] - points[afterIndex];
        var edgeLength = Geometry.Distance(points[afterIndex], points[next]);
        var expectedCenter = points[afterIndex] + edge * fraction;
        var actualCenter = notch[0] + (notch[2] - notch[0]) * 0.5;
        var apexVector = notch[1] - actualCenter;
        if (Geometry.Distance(actualCenter, expectedCenter) > Math.Min(width, depth) * 1e-9 ||
            Math.Abs(Geometry.Distance(notch[0], notch[2]) - width) > width * 1e-9 ||
            Math.Abs(Geometry.Distance(actualCenter, notch[1]) - depth) > depth * 1e-9 ||
            Math.Abs((edge.X / edgeLength) * apexVector.X + (edge.Y / edgeLength) * apexVector.Y) > depth * 1e-9 ||
            Cross(edge, apexVector) * Math.Sign(sourceArea) <= 0 ||
            Math.Abs(Geometry.Distance(points[afterIndex], actualCenter) / edgeLength - fraction) > 1e-9)
            throw new ArgumentException("Çentik konumu veya ölçüleri sayısal hassasiyetle temsil edilemiyor.");
        points.InsertRange(afterIndex + 1, notch);
        ValidatePointEdit(true, points, cancellationToken);
        var resultArea = Geometry.SignedArea(points);
        if (Math.Sign(resultArea) != Math.Sign(sourceArea) || Math.Abs(resultArea) >= Math.Abs(sourceArea))
            throw new ArgumentException("Çentik kontur alanını güvenle azaltmadı; işlem uygulanmadı.");
        return NewPiece(piece, points, "", preserveGrainline:true);
    }
    public static PatternPiece InsertPoint(PatternPiece piece, int afterIndex, Point2 point)
        => InsertPoint(piece, afterIndex, point, CancellationToken.None);

    /// <summary>Reverse an open path without changing any represented coordinates.
    /// The input and its point collection remain unchanged.</summary>
    public static PatternPiece ReverseOpenPath(PatternPiece piece,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(piece.Notches is not null)return ReverseMarkedPath(piece,false,cancellationToken);
        var points=SnapshotPointEdit(piece,cancellationToken);
        if(piece.Closed)throw new ArgumentException("Yalnız açık konturun yönü ters çevrilebilir.");
        ValidatePointEdit(false,points,cancellationToken);
        points.Reverse();cancellationToken.ThrowIfCancellationRequested();
        return NewPiece(piece,points,"",preserveGrainline:true);
    }

    /// <summary>Reverses a closed contour while retaining its first vertex and closure representation.</summary>
    public static PatternPiece ReverseClosedContour(PatternPiece piece, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(piece.Notches is not null)return ReverseMarkedPath(piece,true,cancellationToken);
        var points = SnapshotPointEdit(piece, cancellationToken);
        if (!piece.Closed) throw new ArgumentException("Yalnız kapalı konturun yönü ters çevrilebilir.");
        ValidatePointEdit(true, points, cancellationToken);
        var repeated = points.Count > 1 && points[0] == points[^1];
        points.Reverse(1, points.Count - 1 - (repeated ? 1 : 0));
        cancellationToken.ThrowIfCancellationRequested();
        return NewPiece(piece, points, "", preserveGrainline:true);
    }

    private static PatternPiece ReverseMarkedPath(PatternPiece source,bool closed,CancellationToken token)
    {
        _=MarkedPieceTranslation.Translate(source,0,0,token);
        var detached=source.Clone();detached.Notches=null;
        var result=closed ? ReverseClosedContour(detached,token) : ReverseOpenPath(detached,token);
        var count=source.Points.Count;
        var logicalCount=count-(closed && count>1 && source.Points[0]==source.Points[^1] ? 1 : 0);
        var marks=new List<NotchMark>(source.Notches!.Count);
        foreach(var mark in source.Notches)
        {
            token.ThrowIfCancellationRequested();
            var index=closed ? (mark.VertexIndex==0 ? 0 : logicalCount-mark.VertexIndex) : count-1-mark.VertexIndex;
            if(index<0 || index>=logicalCount || result.Points[index]!=mark.Position)
                throw new ArgumentException("Köşe işareti ters çevrilen konturla eşleşmedi; işlem uygulanmadı.");
            marks.Add(new NotchMark { VertexIndex=index,Position=mark.Position });
        }
        result.Notches=marks;token.ThrowIfCancellationRequested();return result;
    }

    /// <summary>Splits the edge after the selected vertex at its exact binary64 midpoint.
    /// Rejects a midpoint that cannot be represented exactly, preserving the original edge.</summary>
    public static PatternPiece InsertEdgeMidpoint(PatternPiece piece,int afterIndex,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(piece.Notches is not null)
        {
            _=MarkedPieceTranslation.Translate(piece,0,0,cancellationToken);
            var unmarkedSource=piece.Clone();unmarkedSource.Notches=null;
            var inserted=InsertEdgeMidpoint(unmarkedSource,afterIndex,cancellationToken);
            return PreserveMarksAfterInsertion(piece,inserted,afterIndex,cancellationToken);
        }
        var points=SnapshotPointEdit(piece,cancellationToken);
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

    /// <summary>Splits the next edge at a percentage of its length, using the same
    /// geometric precision and validation guarantees as distance insertion.</summary>
    public static PatternPiece InsertPointAtPercentage(PatternPiece piece, int afterIndex, double percentage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(piece.Notches is not null)
        {
            _=MarkedPieceTranslation.Translate(piece,0,0,cancellationToken);
            var unmarkedSource=piece.Clone();unmarkedSource.Notches=null;
            var inserted=InsertPointAtPercentage(unmarkedSource,afterIndex,percentage,cancellationToken);
            return PreserveMarksAfterInsertion(piece,inserted,afterIndex,cancellationToken);
        }
        var points = SnapshotPointEdit(piece, cancellationToken);
        if (!double.IsFinite(percentage) || percentage <= 0 || percentage >= 100)
            throw new ArgumentException("Yüzde sıfırdan büyük ve 100'den küçük olmalıdır.", nameof(percentage));
        if (afterIndex < 0 || afterIndex >= points.Count) throw new ArgumentOutOfRangeException(nameof(afterIndex));
        if (piece.Closed && points.Count > 1 && points[0] == points[^1] && afterIndex == points.Count - 1) afterIndex = 0;
        if (!piece.Closed && afterIndex == points.Count - 1) throw new ArgumentException("Açık konturun son noktasından sonra kenar yok.");
        var distance = Geometry.Distance(points[afterIndex], points[(afterIndex + 1) % points.Count]) * (percentage / 100);
        var detached = piece.Clone(); detached.Points = points;
        return InsertPointAtDistance(detached, afterIndex, distance, cancellationToken);
    }

    /// <summary>Splits the next edge at a distance from the selected vertex. The new
    /// point is at most 0.000001 mm from the original line and requested distance.</summary>
    public static PatternPiece InsertPointAtDistance(PatternPiece piece, int afterIndex, double distance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(piece.Notches is not null)
        {
            _=MarkedPieceTranslation.Translate(piece,0,0,cancellationToken);
            var unmarkedSource=piece.Clone();unmarkedSource.Notches=null;
            var inserted=InsertPointAtDistance(unmarkedSource,afterIndex,distance,cancellationToken);
            return PreserveMarksAfterInsertion(piece,inserted,afterIndex,cancellationToken);
        }
        var points = SnapshotPointEdit(piece, cancellationToken);
        if (afterIndex < 0 || afterIndex >= points.Count) throw new ArgumentOutOfRangeException(nameof(afterIndex));
        if (piece.Closed && points.Count > 1 && points[0] == points[^1] && afterIndex == points.Count - 1) afterIndex = 0;
        if (!piece.Closed && afterIndex == points.Count - 1) throw new ArgumentException("Açık konturun son noktasından sonra kenar yok.");
        var a = points[afterIndex]; var b = points[(afterIndex + 1) % points.Count];
        var length = Geometry.Distance(a, b);
        if (!double.IsFinite(distance) || distance <= 0 || distance >= length)
            throw new ArgumentException("Mesafe sıfırdan büyük ve kenar uzunluğundan küçük olmalıdır.");
        var fraction = distance / length;
        var point = new Point2(a.X + (b.X - a.X) * fraction, a.Y + (b.Y - a.Y) * fraction);
        Geometry.Validate(point);
        static System.Numerics.BigInteger Units(double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value); var exponent = (int)((bits >> 52) & 0x7ff);
            var mantissa = bits & 0x000f_ffff_ffff_ffffL;
            if (exponent != 0) mantissa |= 1L << 52;
            var units = new System.Numerics.BigInteger(mantissa) << (exponent == 0 ? 0 : exponent - 1);
            return bits < 0 ? -units : units;
        }
        var dx = Units(b.X) - Units(a.X); var dy = Units(b.Y) - Units(a.Y);
        var px = Units(point.X) - Units(a.X); var py = Units(point.Y) - Units(a.Y);
        // Compare exact binary64 coordinates as integer multiples of 2^-1074.
        // This avoids cancellation in the cross product, including at large origins.
        var tolerance = Units(0.000001); var requested = Units(distance);
        var edgeSquared = dx * dx + dy * dy; var pointSquared = px * px + py * py;
        var cross = dx * py - dy * px; var projection = dx * px + dy * py;
        var lower = System.Numerics.BigInteger.Max(System.Numerics.BigInteger.Zero, requested - tolerance);
        var upper = requested + tolerance;
        if (projection <= 0 || projection >= edgeSquared ||
            cross * cross > tolerance * tolerance * edgeSquared ||
            pointSquared < lower * lower || pointSquared > upper * upper)
            throw new ArgumentException("Bu mesafe kenar üzerinde sayısal hassasiyetle temsil edilemiyor; geometri değiştirilmedi.");
        var detached = piece.Clone(); detached.Points = points;
        return InsertPoint(detached, afterIndex, point, cancellationToken);
    }

    private static PatternPiece PreserveMarksAfterInsertion(PatternPiece source,PatternPiece inserted,int afterIndex,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(afterIndex<0 || afterIndex>=source.Points.Count)throw new ArgumentOutOfRangeException(nameof(afterIndex));
        var normalized=source.Closed && source.Points.Count>1 && source.Points[0]==source.Points[^1] && afterIndex==source.Points.Count-1 ? 0 : afterIndex;
        if(inserted.Points.Count!=source.Points.Count+1)throw new ArgumentException("Nokta ekleme eşlemesi geçersiz.");
        for(var i=0;i<source.Points.Count;i++)
        {
            token.ThrowIfCancellationRequested();
            if(inserted.Points[i>normalized ? i+1 : i]!=source.Points[i])
                throw new ArgumentException("Kaynak köşeler korunamadı; nokta eklenmedi.");
        }
        var marks=source.Notches!.Select(mark=>new NotchMark { VertexIndex=mark.VertexIndex>normalized ? mark.VertexIndex+1 : mark.VertexIndex,Position=mark.Position }).ToList();
        foreach(var mark in marks)
            if(mark.VertexIndex>=inserted.Points.Count || inserted.Points[mark.VertexIndex]!=mark.Position)
                throw new ArgumentException("Çentik işareti korunamadı; nokta eklenmedi.");
        inserted.Notches=marks;token.ThrowIfCancellationRequested();return inserted;
    }
    /// <summary>Inserts into a detached contour. If the final stored point explicitly
    /// repeats the first point, its index identifies that same first vertex.
    /// Large contours must be processed off the UI thread; cancellation is cooperative.</summary>
    public static PatternPiece InsertPoint(PatternPiece piece, int afterIndex, Point2 point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(piece.Notches is not null)
        {
            _=MarkedPieceTranslation.Translate(piece,0,0,cancellationToken);
            if(afterIndex<0 || afterIndex>=piece.Points.Count)throw new ArgumentOutOfRangeException(nameof(afterIndex));
            var normalized=piece.Closed && piece.Points.Count>1 && piece.Points[0]==piece.Points[^1] && afterIndex==piece.Points.Count-1 ? 0 : afterIndex;
            var detached=piece.Clone();detached.Notches=null;
            var inserted=InsertPoint(detached,afterIndex,point,cancellationToken);
            var marks=piece.Notches.Select(mark=>new NotchMark { VertexIndex=mark.VertexIndex>normalized ? mark.VertexIndex+1 : mark.VertexIndex,Position=mark.Position }).ToList();
            foreach(var mark in marks)
                if(mark.VertexIndex>=inserted.Points.Count || inserted.Points[mark.VertexIndex]!=mark.Position)
                    throw new ArgumentException("Çentik işareti korunamadı; nokta eklenmedi.",nameof(piece));
            inserted.Notches=marks;cancellationToken.ThrowIfCancellationRequested();return inserted;
        }
        var points = SnapshotPointEdit(piece, cancellationToken);
        Geometry.Validate(point);
        if (afterIndex < 0 || afterIndex >= points.Count) throw new ArgumentOutOfRangeException(nameof(afterIndex));
        if (points.Count >= LargeContourValidator.MaximumStoredPoints)
            throw new ArgumentException("Kontur 10.000 saklı nokta sınırını aşamaz.", nameof(piece));
        if (piece.Closed && points.Count > 1 && points[0] == points[^1] && afterIndex == points.Count - 1)
            afterIndex = 0;
        // An implicit closing edge would otherwise become an explicit closure,
        // making a zero-length insertion appear to have succeeded.
        if (piece.Closed && afterIndex == points.Count - 1 && points.Count > 1 &&
            points[0] != points[^1] && point == points[0])
            throw new ArgumentException("Nokta ekleme kapanış kenarında sıfır uzunluklu bir kenar oluşturuyor.", nameof(point));
        points.Insert(afterIndex + 1, point);
        ValidatePointEdit(piece.Closed, points, cancellationToken);
        return NewPiece(piece, points, "", preserveGrainline:true);
    }
    public static PatternPiece DeletePoint(PatternPiece piece, int index)
        => DeletePoint(piece, index, CancellationToken.None);

    /// <summary>Deletes a vertex from a detached contour. The two stored copies
    /// of an explicitly closed first vertex are removed together and the new
    /// first vertex is used to close the contour.</summary>
    public static PatternPiece DeletePoint(PatternPiece piece, int index, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if(piece.Notches is not null)
        {
            _=MarkedPieceTranslation.Translate(piece,0,0,cancellationToken);
            if(index<0 || index>=piece.Points.Count)throw new ArgumentOutOfRangeException(nameof(index));
            var removed=piece.Closed && piece.Points.Count>1 && piece.Points[0]==piece.Points[^1] && index==piece.Points.Count-1 ? 0 : index;
            if(piece.Notches.Any(mark=>mark.VertexIndex==removed))
                throw new ArgumentException("Çentik işaretli köşe silinemez; önce işareti gözden geçirin.",nameof(index));
            var unmarkedSource=piece.Clone();unmarkedSource.Notches=null;
            var edited=DeletePoint(unmarkedSource,index,cancellationToken);
            var marks=piece.Notches.Select(mark=>new NotchMark { VertexIndex=mark.VertexIndex>removed ? mark.VertexIndex-1 : mark.VertexIndex,Position=mark.Position }).ToList();
            foreach(var mark in marks)
                if(mark.VertexIndex<0 || mark.VertexIndex>=edited.Points.Count || edited.Points[mark.VertexIndex]!=mark.Position)
                    throw new ArgumentException("Çentik işareti korunamadı; nokta silinmedi.",nameof(piece));
            edited.Notches=marks;cancellationToken.ThrowIfCancellationRequested();return edited;
        }
        var points = SnapshotPointEdit(piece, cancellationToken);
        if (index < 0 || index >= points.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var repeatedClosure = piece.Closed && points.Count > 1 && points[0] == points[^1];
        if (repeatedClosure && (index == 0 || index == points.Count - 1))
        {
            points.RemoveAt(points.Count - 1);
            points.RemoveAt(0);
            if (points.Count > 0) points.Add(points[0]);
        }
        else points.RemoveAt(index);
        ValidatePointEdit(piece.Closed, points, cancellationToken);
        return NewPiece(piece, points, "", preserveGrainline:true);
    }

    // The caller must not mutate the source during snapshot creation. The edit
    // itself never mutates or retains the caller's point collection.
    private static List<Point2> SnapshotPointEdit(PatternPiece piece, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(piece);
        NotchSafety.RequireNoNotches(piece);
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
        if (piece.Notches is not null)
        {
            // v6 marks identify vertices; only unmarked vertices may move.
            _=MarkedPieceTranslation.Translate(piece,0,0,cancellationToken);
            var marks=piece.Notches.Select(mark=>mark.Clone()).ToList();
            var markedIndex=piece.Closed && piece.Points.Count>1 && piece.Points[0]==piece.Points[^1] && index==piece.Points.Count-1 ? 0 : index;
            if(marks.Any(mark=>mark.VertexIndex==markedIndex))
                throw new ArgumentException("Çentik işaretli köşe taşınamaz; önce işareti gözden geçirin.",nameof(index));
            var detached=piece.Clone();detached.Notches=null;
            var moved=MovePoint(detached,index,position,cancellationToken);
            foreach(var mark in marks)
                if(mark.VertexIndex>=moved.Points.Count || moved.Points[mark.VertexIndex]!=mark.Position)
                    throw new ArgumentException("Köşe işareti korunamadı; düzenleme uygulanmadı.",nameof(piece));
            moved.Notches=marks;cancellationToken.ThrowIfCancellationRequested();return moved;
        }
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
        var repeatedClosure = piece.Closed && points.Count > 1 && points[0] == points[^1];
        points[index] = position;
        if (repeatedClosure && (index == 0 || index == points.Count - 1))
        { points[0] = position; points[^1] = position; }
        if(piece.Closed && !repeatedClosure && points.Count>1 && points[0]==points[^1])
            throw new ArgumentException("Nokta taşıması kapanış kenarını sıfıra indiriyor; kontur değiştirilmedi.");
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
        return NewPiece(piece, points, "", preserveGrainline:true);
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
    private static PatternPiece NewPiece(PatternPiece source, List<Point2> points, string suffix, bool preserveGrainline=false, bool markGrainlineForReview=false)
    {
        var name = source.Name + suffix;
        if (name.Length > 1024) throw new ArgumentException("Üretilen parça adı 1024 karakteri aşıyor; kaynak adını kısaltın.");
        var line=preserveGrainline ? source.Grainline?.Clone() : null;
        if(line is not null && (markGrainlineForReview || !source.Points.SequenceEqual(points)))line.NeedsReview=true;
        return new() { Name = name, Points = points, Closed = source.Closed, Grainline=line };
    }
    private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
}


