using System.Numerics;
using TekstilCAD.Core;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Patterns;

/// <summary>
/// Experimental exact binary64-to-grid local error check. Every indexed point,
/// adjacent edge, local chord and signed local height is compared against an
/// explicitly positive physical budget in millimetres. This does not certify
/// global topology, semantic notch discovery, arbitrary multi-edge feature
/// dimensions, subsequent offset geometry, or production suitability. Inputs
/// must remain stable while snapshots are copied.
/// </summary>
internal static class OffsetBinaryGridPhysicalBudget
{
    private readonly record struct P(BigInteger X,BigInteger Y);

    internal static bool TryVerify(IReadOnlyList<Point2> source,IReadOnlyList<GridPoint> grid,
        double pointMm,double lengthMm,double heightMm,out string reason,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(grid);
        token.ThrowIfCancellationRequested();reason="Invalid size or physical budget.";int n=source.Count;
        if(n<3 || n>707 || grid.Count!=n || !Positive(pointMm) || !Positive(lengthMm) || !Positive(heightMm))return false;
        var input=new Point2[n];var indices=new GridPoint[n];int shift=Math.Min(Exponent(pointMm),Math.Min(Exponent(lengthMm),Exponent(heightMm)));
        var unique=new HashSet<GridPoint>();
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();input[i]=source[i];indices[i]=grid[i];
            if(!input[i].IsFinite || Math.Abs(input[i].X)>Geometry.MaxCoordinate || Math.Abs(input[i].Y)>Geometry.MaxCoordinate ||
                BigInteger.Abs(indices[i].X)>100_000_000_000_000L || BigInteger.Abs(indices[i].Y)>100_000_000_000_000L || !unique.Add(indices[i]))
            {reason=$"Point {i} is out of domain or grid vertices collide.";return false;}
            shift=Math.Min(shift,Math.Min(Exponent(input[i].X),Exponent(input[i].Y)));
        }
        // All integer coordinates have one common physical denominator:
        // 1,000,000 * 2^(1074-shift). No grid value is converted to double.
        int binaryShift=1074-shift;
        BigInteger gridUnit=BigInteger.One<<binaryShift;
        BigInteger pointBudget=(Exact(pointMm)>>shift)*1_000_000;
        BigInteger lengthBudget=(Exact(lengthMm)>>shift)*1_000_000;
        BigInteger heightBudget=(Exact(heightMm)>>shift)*1_000_000;
        var s=new P[n];var g=new P[n];
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();
            s[i]=new((Exact(input[i].X)>>shift)*1_000_000,(Exact(input[i].Y)>>shift)*1_000_000);
            g[i]=new((BigInteger)indices[i].X*gridUnit,(BigInteger)indices[i].Y*gridUnit);
            if(RoundAway(s[i].X,gridUnit)!=indices[i].X || RoundAway(s[i].Y,gridUnit)!=indices[i].Y)
            {reason=$"Point {i} is not the exact nearest source grid point.";return false;}
            var error=Vec(s[i],g[i]);
            if(Dot(error,error)>pointBudget*pointBudget)
            {reason=$"Point {i} exceeds physical position budget.";return false;}
        }
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();int prev=(i+n-1)%n,next=(i+1)%n;
            var se=Vec(s[i],s[next]);var ge=Vec(g[i],g[next]);
            if(Dot(se,se).IsZero || Dot(ge,ge).IsZero || !Within(Dot(se,se),BigInteger.One,Dot(ge,ge),BigInteger.One,lengthBudget))
            {reason=$"Edge {i} is degenerate or exceeds length budget.";return false;}
            if(!Measure(s[prev],s[i],s[next],out var sc,out var sl) || !Measure(g[prev],g[i],g[next],out var gc,out var gl))
            {reason=$"Turn {i} has degenerate or ambiguous chord.";return false;}
            if(sc.Sign!=gc.Sign) {reason=$"Turn {i} was flattened or reversed.";return false;}
            if(!Within(sl,BigInteger.One,gl,BigInteger.One,lengthBudget) ||
                !Within(sc*sc,sl,gc*gc,gl,heightBudget))
            {reason=$"Turn {i} exceeds chord or local height budget.";return false;}
        }
        token.ThrowIfCancellationRequested();
        reason="All indexed source/grid point, edge, chord and local height budgets passed only; complete garment features and production suitability are not certified.";
        return true;
    }

    private static bool Positive(double value)=>double.IsFinite(value) && value>0;
    private static P Vec(P a,P b)=>new(b.X-a.X,b.Y-a.Y);
    private static BigInteger Dot(P a,P b)=>a.X*b.X+a.Y*b.Y;
    private static bool Measure(P a,P b,P c,out BigInteger cross,out BigInteger chord2)
    {
        var chord=Vec(a,c);var witness=Vec(a,b);chord2=Dot(chord,chord);
        cross=chord.X*witness.Y-chord.Y*witness.X;var projection=Dot(chord,witness);
        return !chord2.IsZero && projection>0 && projection<chord2;
    }
    private static bool Within(BigInteger a,BigInteger b,BigInteger c,BigInteger d,BigInteger tolerance)
    {
        var left=a*d+c*b-tolerance*tolerance*b*d;
        return left.Sign<=0 || left*left<=4*a*c*b*d;
    }
    private static BigInteger RoundAway(BigInteger n,BigInteger positiveDenominator)
    {
        var rounded=(BigInteger.Abs(n)+positiveDenominator/2)/positiveDenominator;
        return n.Sign<0 ? -rounded : rounded;
    }
    private static int Exponent(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int e=(int)((bits>>52)&0x7ff);
        return (bits & 0x7fff_ffff_ffff_ffffL)==0 ? 1074 : e==0 ? 0 : e-1;
    }
    private static BigInteger Exact(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int e=(int)((bits>>52)&0x7ff);
        long fraction=bits & 0x000f_ffff_ffff_ffffL;
        BigInteger mantissa=e==0 ? fraction : fraction | (1L<<52);
        if(bits<0)mantissa=-mantissa;
        return mantissa<<(e==0?0:e-1);
    }
}
