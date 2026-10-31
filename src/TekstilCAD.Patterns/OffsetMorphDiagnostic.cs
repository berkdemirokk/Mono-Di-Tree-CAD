using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental sufficient condition for an indexed linear contour morph to be
/// an isotopy with fixed nonzero local turn signs. It does not certify offset
/// distances, feature dimensions, quantization, or production suitability.
/// </summary>
internal static class OffsetMorphDiagnostic
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> target,
        out string reason, CancellationToken token = default)
    {
        reason = "";
        if (source.Count < 3 || source.Count != target.Count || source.Count > 1024)
            return Reject(out reason, "Invalid contour size.");
        int n = source.Count;
        var s = source.ToArray(); var t = target.ToArray();
        for (int i = 0; i < n; i++)
        {
            token.ThrowIfCancellationRequested();
            int j = (i + 1) % n, k = (i + 2) % n;
            var u = Sub(s[j], s[i]); var v = Sub(s[k], s[j]);
            var du = Sub(Sub(t[j], t[i]), u); var dv = Sub(Sub(t[k], t[j]), v);
            BigInteger c = Cross(u,v), b = Cross(du,v) + Cross(u,dv), a = Cross(du,dv);
            // Multiply by the initial sign and prove strict positivity over [0,1].
            int sign = c.Sign;
            if (sign == 0) return Reject(out reason, $"Turn {j} is initially zero.");
            a *= sign; b *= sign; c *= sign;
            if (a+b+c <= 0) return Reject(out reason, $"Turn {j} changes or reaches zero.");
            // A convex quadratic has an interior minimum exactly when -2a < b < 0.
            if (a > 0 && b < 0 && -b < 2*a && 4*a*c-b*b <= 0)
                return Reject(out reason, $"Turn {j} reaches zero during the morph.");
        }
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            token.ThrowIfCancellationRequested();
            if (j == i + 1 || (i == 0 && j == n - 1)) continue;
            var h1 = Hull([s[i], s[(i+1)%n], t[i], t[(i+1)%n]]);
            var h2 = Hull([s[j], s[(j+1)%n], t[j], t[(j+1)%n]]);
            if (!Separated(h1,h2)) return Reject(out reason, $"Swept edge hulls {i} and {j} overlap or touch.");
        }
        reason = "Indexed contour morph is an isotopy with fixed local turn signs only; dimensions are not certified.";
        return true;
    }

    private readonly record struct P(BigInteger X, BigInteger Y);
    private static P Sub(GridPoint a, GridPoint b) => new((BigInteger)a.X-b.X,(BigInteger)a.Y-b.Y);
    private static P Sub(P a, P b) => new(a.X-b.X,a.Y-b.Y);
    private static BigInteger Cross(P a, P b) => a.X*b.Y-a.Y*b.X;
    private static BigInteger Cross(GridPoint a, GridPoint b, GridPoint c) => Cross(Sub(b,a),Sub(c,a));
    private static List<GridPoint> Hull(GridPoint[] points)
    {
        var p=points.Distinct().OrderBy(x=>x.X).ThenBy(x=>x.Y).ToArray();
        if (p.Length <= 1) return p.ToList();
        var lower=new List<GridPoint>(); var upper=new List<GridPoint>();
        foreach(var q in p) { while(lower.Count>=2 && Cross(lower[^2],lower[^1],q)<=0) lower.RemoveAt(lower.Count-1); lower.Add(q); }
        for(int i=p.Length-1;i>=0;i--) { var q=p[i]; while(upper.Count>=2 && Cross(upper[^2],upper[^1],q)<=0) upper.RemoveAt(upper.Count-1); upper.Add(q); }
        lower.RemoveAt(lower.Count-1); upper.RemoveAt(upper.Count-1); lower.AddRange(upper); return lower;
    }
    private static bool Separated(List<GridPoint> a, List<GridPoint> b)
    {
        // Every hull of a valid source/target edge contains at least two points.
        // Axes normal to hull edges suffice for nondegenerate convex polygons;
        // also test tangent axes to handle line-segment hulls and endpoint gaps.
        if(a.Count<2 || b.Count<2) return false;
        foreach(var h in new[]{a,b})
        for(int i=0;i<h.Count;i++)
        {
            var d=Sub(h[(i+1)%h.Count],h[i]);
            if (Axis(d.X,d.Y) || Axis(-d.Y,d.X)) return true;
        }
        return false;
        bool Axis(BigInteger x, BigInteger y)
        {
            var ap=a.Select(p=>(BigInteger)p.X*x+(BigInteger)p.Y*y).ToArray();
            var bp=b.Select(p=>(BigInteger)p.X*x+(BigInteger)p.Y*y).ToArray();
            return ap.Max()<bp.Min() || bp.Max()<ap.Min();
        }
    }
    private static bool Reject(out string reason,string message) { reason=message; return false; }
}
