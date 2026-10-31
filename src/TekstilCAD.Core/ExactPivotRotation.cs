using System.Numerics;

namespace TekstilCAD.Core;

// Computes with the represented binary64 coefficients and rounds only once.
// The coefficients themselves approximate the mathematical sine and cosine.
internal static class ExactPivotRotation
{
    private readonly record struct Dyadic(BigInteger N, int E)
    {
        internal static Dyadic FromDouble(double value)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7ff);
            ulong fraction = bits & 0x000f_ffff_ffff_ffffUL;
            BigInteger n = exponent == 0 ? fraction : fraction | (1UL << 52);
            if ((bits >> 63) != 0) n = -n;
            return new(n, exponent == 0 ? -1074 : exponent - 1075);
        }
        public static Dyadic operator +(Dyadic a, Dyadic b)
        {
            int e = Math.Min(a.E, b.E);
            return new((a.N << (a.E - e)) + (b.N << (b.E - e)), e);
        }
        public static Dyadic operator -(Dyadic a, Dyadic b) => a + new Dyadic(-b.N, b.E);
        public static Dyadic operator *(Dyadic a, Dyadic b) => new(a.N * b.N, a.E + b.E);
        internal double Round()
        {
            if (N.IsZero) return 0d;
            bool negative = N.Sign < 0;
            BigInteger n = BigInteger.Abs(N);
            int top = checked((int)n.GetBitLength() - 1 + E);
            int unit = Math.Max(top - 52, -1074);
            int shift = unit - E;
            BigInteger rounded;
            if (shift > 0)
            {
                rounded = n >> shift;
                BigInteger remainder = n - (rounded << shift);
                BigInteger half = BigInteger.One << (shift - 1);
                if (remainder > half || (remainder == half && !rounded.IsEven)) rounded++;
            }
            else rounded = n << -shift;
            if (rounded.IsZero) return BitConverter.Int64BitsToDouble(negative ? long.MinValue : 0);
            if (rounded.GetBitLength() > 53) { rounded >>= 1; unit++; }
            int resultTop = checked((int)rounded.GetBitLength() - 1 + unit);
            ulong sign = negative ? 1UL << 63 : 0;
            if (resultTop > 1023) return BitConverter.Int64BitsToDouble((long)(sign | 0x7ff0_0000_0000_0000UL));
            ulong bits;
            if (resultTop < -1022) bits = (ulong)rounded;
            else
            {
                int left = 53 - (int)rounded.GetBitLength();
                rounded <<= left;
                bits = ((ulong)(resultTop + 1023) << 52) | ((ulong)rounded & 0x000f_ffff_ffff_ffffUL);
            }
            return BitConverter.Int64BitsToDouble((long)(sign | bits));
        }
    }

    private readonly record struct ExactPoint(Dyadic X, Dyadic Y);
    internal static List<Point2> Rotate(IReadOnlyList<Point2> points, double c, double s, Point2 origin)
    {
        var dc = Dyadic.FromDouble(c); var ds = Dyadic.FromDouble(s);
        var ox = Dyadic.FromDouble(origin.X); var oy = Dyadic.FromDouble(origin.Y);
        var result = new List<Point2>(points.Count);
        var exact = new List<ExactPoint>(points.Count);
        foreach (var point in points)
        {
            var x = Dyadic.FromDouble(point.X) - ox; var y = Dyadic.FromDouble(point.Y) - oy;
            var target = new ExactPoint(ox + x * dc - y * ds, oy + x * ds + y * dc);
            var limit = Dyadic.FromDouble(Geometry.MaxCoordinate);
            if ((target.X - limit).N.Sign > 0 || (target.X + limit).N.Sign < 0 ||
                (target.Y - limit).N.Sign > 0 || (target.Y + limit).N.Sign < 0)
                throw new ArgumentOutOfRangeException(nameof(points), "Coordinates must be within the supported world range.");
            var rounded = new Point2(target.X.Round(), target.Y.Round());
            Geometry.Validate(rounded);
            exact.Add(target); result.Add(rounded);
        }
        for (int i = 0; i < points.Count; i++)
        {
            int previous = (i + points.Count - 1) % points.Count;
            if (!PreservesVector(exact[previous], exact[i], result[previous], result[i]) ||
                !PreservesVector(exact[0], exact[i], result[0], result[i]))
                throw new ArgumentException("Döndürme sırasında parça şekli sayısal hassasiyetle korunamıyor. Daha küçük koordinatlar kullanın.", nameof(points));
        }
        return result;
    }

    // Compare the two-dimensional error against the larger component of the
    // exact transformed vector. This avoids unstable per-coordinate checks
    // when an individual component is close to zero. Edges and anchor vectors
    // are checked; this is not a manufacturing tolerance or a proof of topology.
    private static bool PreservesVector(ExactPoint a, ExactPoint b, Point2 c, Point2 d)
    {
        var x = b.X - a.X; var y = b.Y - a.Y;
        var rx = Dyadic.FromDouble(d.X) - Dyadic.FromDouble(c.X);
        var ry = Dyadic.FromDouble(d.Y) - Dyadic.FromDouble(c.Y);
        int e = Math.Min(Math.Min(x.E, y.E), Math.Min(rx.E, ry.E));
        BigInteger sx = x.N << (x.E - e), sy = y.N << (y.E - e);
        BigInteger tx = rx.N << (rx.E - e), ty = ry.N << (ry.E - e);
        var scale = BigInteger.Max(BigInteger.Abs(sx), BigInteger.Abs(sy));
        var error = BigInteger.Max(BigInteger.Abs(tx - sx), BigInteger.Abs(ty - sy));
        // Check each meaningful component independently, including nonzero
        // rounded values. Components at coefficient-noise scale are exempt:
        // near-axis cancellation can create them from the approximated trig
        // coefficients alone. This threshold is a heuristic, not a proof of
        // preservation of every shallow feature.
        const long componentFloor = 1_000_000_000_000_000;
        const long tolerance = 1_000_000_000;
        if (BigInteger.Abs(sx) * componentFloor > scale &&
            BigInteger.Abs(tx - sx) * tolerance > BigInteger.Abs(sx)) return false;
        if (BigInteger.Abs(sy) * componentFloor > scale &&
            BigInteger.Abs(ty - sy) * tolerance > BigInteger.Abs(sy)) return false;
        return error * tolerance <= scale;
    }
}


