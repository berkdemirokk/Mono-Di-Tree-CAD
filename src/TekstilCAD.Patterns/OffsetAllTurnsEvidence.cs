using System.Numerics;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental exact per-index comparison of every local three-vertex chord
/// and signed height, including flat, convex and concave turns. This does not
/// label smoothness, identify garment features, prove the supplied index mapping,
/// validate global contours, or authorize an offset. Inputs must stay stable
/// during copying; caller-selected tolerances are integer grid units.
/// </summary>
internal static class OffsetAllTurnsEvidence
{
    internal static bool TryVerify(IReadOnlyList<GridPoint> source, IReadOnlyList<GridPoint> candidate,
        long chordTolerance, long heightTolerance, out string reason, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(candidate);
        token.ThrowIfCancellationRequested(); int n=source.Count;
        reason="Invalid contour size or tolerance.";
        if(n<3 || n>707 || candidate.Count!=n || chordTolerance<0 || heightTolerance<0) return false;
        var s=new GridPoint[n]; var c=new GridPoint[n];
        for(int i=0;i<n;i++) { token.ThrowIfCancellationRequested(); s[i]=source[i]; c[i]=candidate[i]; }
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested(); int prev=(i+n-1)%n, next=(i+1)%n;
            if(!Measure(s[prev],s[i],s[next],out var sx,out var sd) ||
               !Measure(c[prev],c[i],c[next],out var cx,out var cd))
            { reason=$"Turn {i} has degenerate or non-projecting chord."; return false; }
            if(sx.Sign!=cx.Sign) { reason=$"Turn {i} changed or lost its sign."; return false; }
            if(!Within(sd,BigInteger.One,cd,BigInteger.One,chordTolerance) ||
               !Within(sx*sx,sd,cx*cx,cd,heightTolerance))
            { reason=$"Turn {i} exceeds chord or height tolerance."; return false; }
        }
        reason="All indexed local turns measured only; full contour mapping, garment features and production fitness are not certified.";
        return true;
    }

    private static bool Measure(GridPoint a,GridPoint b,GridPoint c,out BigInteger cross,out BigInteger chordSquared)
    {
        BigInteger dx=(BigInteger)c.X-a.X,dy=(BigInteger)c.Y-a.Y;
        BigInteger x=(BigInteger)b.X-a.X,y=(BigInteger)b.Y-a.Y;
        cross=dx*y-dy*x; chordSquared=dx*dx+dy*dy;
        var projection=x*dx+y*dy;
        return !chordSquared.IsZero && projection>0 && projection<chordSquared;
    }

    // Exact test for |sqrt(a/b)-sqrt(c/d)| <= t with positive denominators.
    private static bool Within(BigInteger a,BigInteger b,BigInteger c,BigInteger d,long tolerance)
    {
        BigInteger t=tolerance;
        var left=a*d+c*b-t*t*b*d;
        return left.Sign<=0 || left*left<=4*a*c*b*d;
    }
}
