using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

internal sealed record RectangularNotch(int MouthStart, int BottomStart, int BottomEnd, int MouthEnd,
    BigInteger WidthSquared, BigInteger DepthSquared);

/// <summary>
/// Experimental restricted decomposition: every concave turn must belong to
/// a four-vertex rectangular pocket. Squared dimensions use exact grid units.
/// Ambiguity, touching, nested/overlapping pocket boxes and unsupported contours
/// are rejected. Not garment-semantic discovery or production authorization.
/// Input must remain stable while copied.
/// </summary>
internal static class OffsetRectangularNotchDiscovery
{
    internal static bool TryDiscover(IReadOnlyList<GridPoint> source, out RectangularNotch[] features,
        out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        features = []; reason = "Invalid contour size."; token.ThrowIfCancellationRequested();
        int n = source.Count;
        if (n < 4 || n > 707) return false;
        var p = new GridPoint[n]; var unique = new HashSet<GridPoint>();
        for (int i=0;i<n;i++) { token.ThrowIfCancellationRequested(); p[i]=source[i]; if (!unique.Add(p[i])) { reason="Repeated vertex."; return false; } }
        BigInteger area=0;
        for (int i=0;i<n;i++) { token.ThrowIfCancellationRequested(); area+=(BigInteger)p[i].X*p[(i+1)%n].Y-(BigInteger)p[i].Y*p[(i+1)%n].X; }
        if (area.IsZero) { reason="Zero signed area."; return false; }
        var concave=new bool[n];
        for (int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested(); var cross=Cross(p[(i+n-1)%n],p[i],p[(i+1)%n]);
            if (cross.IsZero) { reason=$"Ambiguous turn {i}."; return false; }
            concave[i]=cross.Sign!=area.Sign;
        }
        for (int i=0;i<n;i++) for (int j=i+1;j<n;j++)
        {
            token.ThrowIfCancellationRequested();
            if (j==i+1 || (i==0 && j==n-1)) continue;
            if (Intersects(p[i],p[(i+1)%n],p[j],p[(j+1)%n])) { reason="Contour intersects or touches itself."; return false; }
        }
        var used=new bool[n]; var result=new List<RectangularNotch>();
        for (int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();
            if (!concave[i] || used[i]) continue;
            int prev=(i+n-1)%n, next=(i+1)%n, end=(i+2)%n;
            // A concave pair has a unique first vertex, including across the seam.
            if (concave[prev] && !used[prev]) continue;
            if (!concave[next] || concave[prev] || concave[end] || used[prev] || used[i] || used[next] || used[end])
            { reason=$"Concave turn {i} does not define an isolated pocket."; return false; }
            var leg=Vec(p[prev],p[i]); var floor=Vec(p[i],p[next]); var other=Vec(p[next],p[end]); var mouth=Vec(p[prev],p[end]);
            if (Dot(leg,floor)!=0 || leg.X!=-other.X || leg.Y!=-other.Y || floor.X!=mouth.X || floor.Y!=mouth.Y || Dot(mouth,mouth).IsZero)
            { reason=$"Pocket {i} is not an exact rectangle."; return false; }
            // Its open mouth must not meet any other source boundary.
            for (int k=0;k<n;k++)
            {
                token.ThrowIfCancellationRequested(); int k2=(k+1)%n;
                if (k==prev || k==i || k==next || k2==prev || k==end) continue;
                if (Intersects(p[prev],p[end],p[k],p[k2])) { reason="Pocket mouth touches another boundary."; return false; }
            }
            foreach (var old in result)
            {
                token.ThrowIfCancellationRequested();
                if (!BoxesDisjoint(p[prev],p[i],p[next],p[end],p[old.MouthStart],p[old.BottomStart],p[old.BottomEnd],p[old.MouthEnd]))
                { reason="Pocket regions overlap or are ambiguous."; return false; }
            }
            used[prev]=used[i]=used[next]=used[end]=true;
            result.Add(new(prev,i,next,end,Dot(mouth,mouth),Dot(leg,leg)));
        }
        for (int i=0;i<n;i++) { token.ThrowIfCancellationRequested(); if (concave[i] && !used[i]) { reason=$"Unpaired concave turn {i}."; return false; } }
        features=result.ToArray(); reason="Restricted rectangular pockets only; garment semantics and preservation are not certified."; return true;
    }

    private static (BigInteger X,BigInteger Y) Vec(GridPoint a,GridPoint b) => ((BigInteger)b.X-a.X,(BigInteger)b.Y-a.Y);
    private static BigInteger Dot((BigInteger X,BigInteger Y) a,(BigInteger X,BigInteger Y) b) => a.X*b.X+a.Y*b.Y;
    private static BigInteger Cross(GridPoint a,GridPoint b,GridPoint c) { var u=Vec(a,b); var v=Vec(a,c); return u.X*v.Y-u.Y*v.X; }
    private static bool On(GridPoint a,GridPoint b,GridPoint p) => Math.Min(a.X,b.X)<=p.X && p.X<=Math.Max(a.X,b.X) && Math.Min(a.Y,b.Y)<=p.Y && p.Y<=Math.Max(a.Y,b.Y);
    private static bool Intersects(GridPoint a,GridPoint b,GridPoint c,GridPoint d)
    {
        int x=Cross(a,b,c).Sign, y=Cross(a,b,d).Sign, z=Cross(c,d,a).Sign, w=Cross(c,d,b).Sign;
        return (x==0 && On(a,b,c)) || (y==0 && On(a,b,d)) || (z==0 && On(c,d,a)) || (w==0 && On(c,d,b)) || (x*y<0 && z*w<0);
    }
    private static bool BoxesDisjoint(GridPoint a,GridPoint b,GridPoint c,GridPoint d,GridPoint e,GridPoint f,GridPoint g,GridPoint h)
    {
        static bool Apart(long a,long b,long c,long d,long e,long f,long g,long h) => Math.Max(Math.Max(a,b),Math.Max(c,d))<Math.Min(Math.Min(e,f),Math.Min(g,h)) || Math.Max(Math.Max(e,f),Math.Max(g,h))<Math.Min(Math.Min(a,b),Math.Min(c,d));
        return Apart(a.X,b.X,c.X,d.X,e.X,f.X,g.X,h.X) || Apart(a.Y,b.Y,c.Y,d.Y,e.Y,f.Y,g.Y,h.Y);
    }
}
