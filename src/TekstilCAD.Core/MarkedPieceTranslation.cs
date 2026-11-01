using System.Numerics;

namespace TekstilCAD.Core;

/// <summary>Creates a detached translation and retains visual marks at their exact contour vertices.</summary>
public static class MarkedPieceTranslation
{
    // Exact binary64 integer units, used to measure displacement rather than
    // subtracting doubles and possibly rounding away the error a second time.
    private static BigInteger Units(double value)
    {
        var bits=BitConverter.DoubleToInt64Bits(value);
        var exponent=(int)((bits >> 52) & 0x7ff);
        var mantissa=bits & 0x000f_ffff_ffff_ffffL;
        if(exponent!=0)mantissa|=1L<<52;
        var units=new BigInteger(mantissa) << (exponent==0 ? 0 : exponent-1);
        return bits<0 ? -units : units;
    }

    // Same 1e-9 relative budget as TranslationPrecision's vector checks.
    // This is a numeric representation limit, not a manufacturing tolerance.
    private static bool DisplacementMatches(double source,double target,double requested)
    {
        var delta=Units(requested);
        var error=Units(target)-Units(source)-delta;
        return BigInteger.Abs(error)*1_000_000_000 <= BigInteger.Abs(delta);
    }
    public static (List<Point2> Points, List<NotchMark>? Notches) Translate(PatternPiece source, double dx, double dy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Points is null || source.Points.Count == 0) throw new ArgumentException("Parça noktaları eksik.", nameof(source));
        var marks = source.Notches;
        if (marks is not null)
        {
            if (marks.Count == 0) throw new ArgumentException("Boş işaret listesi geçersiz.", nameof(source));
            var indices = new HashSet<int>();
            foreach (var mark in marks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (mark is null || mark.VertexIndex < 0 || mark.VertexIndex >= source.Points.Count ||
                    (source.Closed && source.Points.Count > 1 && source.Points[0] == source.Points[^1] && mark.VertexIndex == source.Points.Count - 1) ||
                    !indices.Add(mark.VertexIndex) || mark.Position != source.Points[mark.VertexIndex])
                    throw new ArgumentException("İşaret kaynak konturla eşleşmiyor; taşıma uygulanmadı.", nameof(source));
            }
        }
        var translated = Geometry.Translate(source.Points, dx, dy);
        for (var i = 0; i < translated.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DisplacementMatches(source.Points[i].X, translated[i].X, dx) || !DisplacementMatches(source.Points[i].Y, translated[i].Y, dy))
                throw new ArgumentException("Taşıma miktarı bir noktada sayısal hassasiyetle temsil edilemiyor.", nameof(source));
        }
        cancellationToken.ThrowIfCancellationRequested();
        // A valid mark can have the opposite signed-zero representation from
        // its equal vertex. Keep its own bits on each axis that did not move.
        var translatedMarks = marks?.Select(mark => new NotchMark { VertexIndex = mark.VertexIndex,
            Position = new Point2(dx == 0 ? mark.Position.X : translated[mark.VertexIndex].X,
                                  dy == 0 ? mark.Position.Y : translated[mark.VertexIndex].Y) }).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        return (translated, translatedMarks);
    }
}
