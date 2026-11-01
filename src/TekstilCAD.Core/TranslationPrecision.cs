using System.Numerics;

namespace TekstilCAD.Core;

// Arithmetic guard for represented double coordinates. It checks edge and
// anchor vectors and independent overall axis spans; it is not proof that
// every shallow local detail survives, nor a physical manufacturing tolerance.
internal static class TranslationPrecision
{
    private const long Denominator = 1_000_000_000;
    private readonly record struct BinaryNumber(BigInteger Significand, int Exponent);

    private static BinaryNumber Exact(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7ff);
        var fraction = bits & 0x000f_ffff_ffff_ffffL;
        if (exponent == 0x7ff) throw new ArgumentOutOfRangeException(nameof(value));
        if (exponent == 0 && fraction == 0) return new(BigInteger.Zero, 0);
        var significand = new BigInteger(exponent == 0 ? fraction : fraction | (1L << 52));
        if (bits < 0) significand = -significand;
        return new(significand, exponent == 0 ? -1074 : exponent - 1075);
    }

    private static BigInteger Aligned(BinaryNumber value, int exponent) => value.Significand << (value.Exponent - exponent);

    private static bool SameVector(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        var ax = Exact(a.X); var ay = Exact(a.Y); var bx = Exact(b.X); var by = Exact(b.Y);
        var cx = Exact(c.X); var cy = Exact(c.Y); var dx = Exact(d.X); var dy = Exact(d.Y);
        var exponent = Math.Min(Math.Min(Math.Min(ax.Exponent, ay.Exponent), Math.Min(bx.Exponent, by.Exponent)),
            Math.Min(Math.Min(cx.Exponent, cy.Exponent), Math.Min(dx.Exponent, dy.Exponent)));
        var sx = Aligned(bx, exponent) - Aligned(ax, exponent);
        var sy = Aligned(by, exponent) - Aligned(ay, exponent);
        var tx = Aligned(dx, exponent) - Aligned(cx, exponent);
        var ty = Aligned(dy, exponent) - Aligned(cy, exponent);
        var scale = BigInteger.Max(BigInteger.Abs(sx), BigInteger.Abs(sy));
        var error = BigInteger.Max(BigInteger.Abs(tx-sx), BigInteger.Abs(ty-sy));
        return error * Denominator <= scale;
    }

    private static bool SameSpan(double sourceMin, double sourceMax, double targetMin, double targetMax)
    {
        var a = Exact(sourceMin); var b = Exact(sourceMax); var c = Exact(targetMin); var d = Exact(targetMax);
        var exponent = Math.Min(Math.Min(a.Exponent, b.Exponent), Math.Min(c.Exponent, d.Exponent));
        var source = Aligned(b, exponent) - Aligned(a, exponent);
        var target = Aligned(d, exponent) - Aligned(c, exponent);
        return BigInteger.Abs(target-source) * Denominator <= source;
    }

    internal static void Validate(IReadOnlyList<Point2> source, IReadOnlyList<Point2> target,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Count != target.Count) throw new ArgumentException("Taşınan nokta sayısı değişti.");
        if (source.Count == 0) return;
        var sMinX=source[0].X; var sMaxX=sMinX; var sMinY=source[0].Y; var sMaxY=sMinY;
        var tMinX=target[0].X; var tMaxX=tMinX; var tMinY=target[0].Y; var tMaxY=tMinY;
        for (var i=0; i<source.Count; i++)
        {
            if((i & 63)==0)cancellationToken.ThrowIfCancellationRequested();
            var s=source[i]; var t=target[i];
            sMinX=Math.Min(sMinX,s.X); sMaxX=Math.Max(sMaxX,s.X);
            sMinY=Math.Min(sMinY,s.Y); sMaxY=Math.Max(sMaxY,s.Y);
            tMinX=Math.Min(tMinX,t.X); tMaxX=Math.Max(tMaxX,t.X);
            tMinY=Math.Min(tMinY,t.Y); tMaxY=Math.Max(tMaxY,t.Y);
            var previous=(i+source.Count-1)%source.Count;
            if (!SameVector(source[previous],s,target[previous],t) || !SameVector(source[0],s,target[0],t))
                throw new ArgumentException("Taşıma sırasında parça şekli sayısal hassasiyetle korunamıyor. Daha küçük koordinatlar kullanın.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameSpan(sMinX,sMaxX,tMinX,tMaxX) || !SameSpan(sMinY,sMaxY,tMinY,tMaxY))
            throw new ArgumentException("Taşıma sırasında parçanın eni veya boyu sayısal hassasiyetle korunamıyor. Daha küçük koordinatlar kullanın.");
    }
}
