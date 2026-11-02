using TekstilCAD.Core;

namespace TekstilCAD.Tests;

// Isolated test-only alternative; it is not used by PatternTools.Offset.
// Rounds the exact finite binary64 value times 1,000,000 to nearest integer,
// ties away from zero. The supported source domain is Geometry.MaxCoordinate.
internal static class ExperimentalExactGridRounding
{
    internal static bool TryRound(double value,out long result)
    {
        result=0;
        if(!double.IsFinite(value) || Math.Abs(value)>Geometry.MaxCoordinate)return false;
        ulong bits=unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        int encoded=(int)((bits>>52)&0x7ff);ulong fraction=bits&0x000f_ffff_ffff_ffffUL;
        ulong significand=encoded==0?fraction:fraction|(1UL<<52);
        int exponent=encoded==0?-1074:encoded-1075;
        UInt128 scaled=(UInt128)significand*1_000_000U;
        UInt128 rounded;
        if(exponent>=0)
        {
            if(exponent>=128 || scaled>(UInt128.MaxValue>>exponent))return false;
            rounded=scaled<<exponent;
        }
        else
        {
            int shift=-exponent;
            if(shift>=128)rounded=0;
            else
            {
                rounded=scaled>>shift;
                UInt128 remainder=scaled- (rounded<<shift);
                if(remainder>=((UInt128)1<<(shift-1)))rounded++;
            }
        }
        if(rounded>long.MaxValue)return false;
        long absolute=(long)rounded;result=(bits>>63)!=0?-absolute:absolute;
        return true;
    }
}
