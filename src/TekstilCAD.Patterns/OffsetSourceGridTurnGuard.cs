using System.Numerics;
using TekstilCAD.Core;

namespace TekstilCAD.Patterns;

/// <summary>
/// Rejects a change in any indexed local turn when the actual binary64 source
/// coordinates are rounded to the offset grid. This is a necessary safeguard,
/// not a proof of all feature dimensions, global topology, or offset accuracy.
/// </summary>
internal static class OffsetSourceGridTurnGuard
{
    private readonly record struct ExactPoint(BigInteger X,BigInteger Y);

    internal static bool PreservesTurns(IReadOnlyList<Point2> source,IReadOnlyList<(long X,long Y)> grid,
        CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);ArgumentNullException.ThrowIfNull(grid);
        token.ThrowIfCancellationRequested();int n=source.Count;
        if(n<3||n>Geometry.MaxValidatedContourVertices||grid.Count!=n)return false;
        int shift=1074;
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();
            if(!source[i].IsFinite)return false;
            shift=Math.Min(shift,Math.Min(Exponent(source[i].X),Exponent(source[i].Y)));
        }
        var exact=new ExactPoint[n];
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();
            exact[i]=new(Exact(source[i].X)>>shift,Exact(source[i].Y)>>shift);
        }
        for(int i=0;i<n;i++)
        {
            token.ThrowIfCancellationRequested();int prev=(i+n-1)%n,next=(i+1)%n;
            var a=exact[prev];var b=exact[i];var c=exact[next];
            var sourceTurn=(b.X-a.X)*(c.Y-b.Y)-(b.Y-a.Y)*(c.X-b.X);
            var ga=grid[prev];var gb=grid[i];var gc=grid[next];
            BigInteger ux=(BigInteger)gb.X-ga.X,uy=(BigInteger)gb.Y-ga.Y;
            BigInteger vx=(BigInteger)gc.X-gb.X,vy=(BigInteger)gc.Y-gb.Y;
            if(sourceTurn.Sign!=(ux*vy-uy*vx).Sign)return false;
        }
        return true;
    }

    // Represents a finite double exactly as an integer times 2^-1074.
    private static BigInteger Exact(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int exponent=(int)((bits>>52)&0x7ff);
        long fraction=bits & 0x000f_ffff_ffff_ffffL;
        BigInteger significand=exponent==0?fraction:fraction | (1L<<52);
        if(bits<0)significand=-significand;
        return significand<<(exponent==0?0:exponent-1);
    }
    private static int Exponent(double value)
    {
        long bits=BitConverter.DoubleToInt64Bits(value);int exponent=(int)((bits>>52)&0x7ff);
        return (bits & 0x7fff_ffff_ffff_ffffL)==0?1074:exponent==0?0:exponent-1;
    }
}
