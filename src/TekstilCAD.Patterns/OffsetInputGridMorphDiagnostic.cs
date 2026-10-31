using System.Numerics;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>Experimental exact binary64-to-actual-grid isotopy diagnostic only.
/// Does not certify feature dimensions, offset geometry or production fitness.</summary>
internal static class OffsetInputGridMorphDiagnostic
{
    private const int DenominatorBits = 1074;
    private readonly record struct P(BigInteger X, BigInteger Y);

    internal static bool TryVerify(IReadOnlyList<Point2> input, IReadOnlyList<GridPoint> grid,
        out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(grid);
        token.ThrowIfCancellationRequested();
        reason = "Invalid contour size.";
        int n = input.Count;
        if (n < 3 || n > 1024 || grid.Count != n) return false;
        var s = new P[n]; var t = new P[n];
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            var p = input[i]; var g = grid[i];
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
            { reason = "Nonfinite input."; return false; }
            s[i] = new P(ExactScaled(p.X), ExactScaled(p.Y));
            t[i] = new P((BigInteger)g.X << DenominatorBits, (BigInteger)g.Y << DenominatorBits);
        }
        // Inputs are snapshots. Caller supplies the actual production grid path in the same order;
        // this method deliberately does not assert how that path was rounded.
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            int j = (i + 1) % n, k = (i + 2) % n;
            var u = Sub(s[j],s[i]); var v = Sub(s[k],s[j]);
            var du = Sub(Sub(t[j],t[i]),u); var dv = Sub(Sub(t[k],t[j]),v);
            BigInteger c = Cross(u,v), b = Cross(du,v)+Cross(u,dv), a = Cross(du,dv);
            int sign = c.Sign;
            if (sign == 0) { reason = $"Turn {j} initially zero."; return false; }
            a *= sign; b *= sign; c *= sign;
            if (a+b+c <= 0 || (a > 0 && b < 0 && -b < 2*a && 4*a*c-b*b <= 0))
            { reason = $"Turn {j} reaches zero during morph."; return false; }
        }
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            token.ThrowIfCancellationRequested();
            if (j == i+1 || (i == 0 && j == n-1)) continue;
            var h1 = Hull([s[i],s[(i+1)%n],t[i],t[(i+1)%n]]);
            var h2 = Hull([s[j],s[(j+1)%n],t[j],t[(j+1)%n]]);
            if (!Separated(h1,h2)) { reason = $"Swept edge hulls {i} and {j} overlap or touch."; return false; }
        }
        reason = "Indexed input-to-grid morph is an isotopy with fixed turn signs only; dimensions and offset are not certified.";
        return true;
    }

    // Exact binary64 value * 1,000,000 * 2^1074; no floating-point arithmetic.
    private static BigInteger ExactScaled(double value)
    {
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7ff);
        ulong fraction = bits & ((1UL << 52)-1);
        ulong mantissa = exponent == 0 ? fraction : fraction | (1UL << 52);
        int shift = exponent == 0 ? 0 : exponent - 1;
        BigInteger result = ((BigInteger)mantissa * 1_000_000) << shift;
        return (bits >> 63) == 0 ? result : -result;
    }
    private static P Sub(P a,P b) => new(a.X-b.X,a.Y-b.Y);
    private static BigInteger Cross(P a,P b) => a.X*b.Y-a.Y*b.X;
    private static BigInteger Cross(P a,P b,P c) => Cross(Sub(b,a),Sub(c,a));
    private static List<P> Hull(P[] points)
    {
        var p=points.Distinct().OrderBy(x=>x.X).ThenBy(x=>x.Y).ToArray();
        if(p.Length<=1) return p.ToList();
        var lower=new List<P>(); var upper=new List<P>();
        foreach(var q in p) { while(lower.Count>=2 && Cross(lower[^2],lower[^1],q)<=0) lower.RemoveAt(lower.Count-1); lower.Add(q); }
        for(int i=p.Length-1;i>=0;i--) { var q=p[i]; while(upper.Count>=2 && Cross(upper[^2],upper[^1],q)<=0) upper.RemoveAt(upper.Count-1); upper.Add(q); }
        lower.RemoveAt(lower.Count-1); upper.RemoveAt(upper.Count-1); lower.AddRange(upper); return lower;
    }
    private static bool Separated(List<P> a,List<P> b)
    {
        if(a.Count<2 || b.Count<2) return false;
        foreach(var h in new[]{a,b}) for(int i=0;i<h.Count;i++)
        {
            var d=Sub(h[(i+1)%h.Count],h[i]);
            if(Axis(d.X,d.Y) || Axis(-d.Y,d.X)) return true;
        }
        return false;
        bool Axis(BigInteger x,BigInteger y)
        {
            var ap=a.Select(p=>p.X*x+p.Y*y).ToArray();
            var bp=b.Select(p=>p.X*x+p.Y*y).ToArray();
            return ap.Max()<bp.Min() || bp.Max()<ap.Min();
        }
    }
}
