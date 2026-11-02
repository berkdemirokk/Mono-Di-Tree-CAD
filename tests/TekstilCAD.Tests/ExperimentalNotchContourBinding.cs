using System.Numerics;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

// Test-only insertion binding. Caller keeps inputs stable during snapshots;
// no persistence, curve, offset or manufacturing validation is implied.
internal static class ExperimentalNotchContourBinding
{
    internal static bool Verify(PatternPiece source,PatternPiece output,Guid expectedOutputId,
        ExperimentalPhysicalNotchV1? spec,out string reason)
    {
        reason="Missing, invalid or mismatched contour identity.";
        if(source is null || output is null || source.Points is null || output.Points is null || spec is null ||
           expectedOutputId==Guid.Empty || output.Id!=expectedOutputId || !source.Closed || !output.Closed ||
           source.Points.Count<3 || source.Points.Count>4093 || output.Points.Count!=source.Points.Count+3)return false;
        var input=new PatternPiece{Id=source.Id,Closed=source.Closed,Points=source.Points.ToList()};
        var result=output.Points.ToArray();int n=input.Points.Count,m=result.Length,edge=spec.SourceEdgeIndex;
        if(edge<0 || edge>=n || input.Points[0]==input.Points[^1] || result[0]==result[^1])return false;
        int start=-1;
        for(int shift=0;shift<m;shift++)
        {
            bool matches=true;
            for(int k=0;k<n;k++)
                if(!SameBits(input.Points[k],result[(shift+k+(k>edge?3:0))%m])){matches=false;break;}
            if(matches){if(start>=0){reason="Ambiguous cyclic source mapping.";return false;}start=shift;}
        }
        if(start<0){reason="Original vertices, source edge, or order differ.";return false;}
        Point2[] v=[result[(start+edge+1)%m],result[(start+edge+2)%m],result[(start+edge+3)%m]];
        if(!ExperimentalPhysicalNotchValidation.Verify(input,spec,v,out reason))return false;
        if(!OnSourceEdge(input.Points[edge],input.Points[(edge+1)%n],v[0],v[2]))
        {reason="Actual mouth points are not strictly ordered on the exact source edge.";return false;}
        try
        {
            Geometry.ValidateContour(result);
            double inputArea=Geometry.SignedArea(input.Points),resultArea=Geometry.SignedArea(result);
            if(Math.Sign(inputArea)!=Math.Sign(resultArea) || Math.Abs(resultArea)>=Math.Abs(inputArea))
            {reason="Output changes orientation or does not reduce area.";return false;}
        }
        catch(ArgumentException){reason="Output contour is invalid or intersects itself.";return false;}
        reason="Experimental straight-edge insertion binding passed existing contour checks; numerical bounds, offset mapping and manufacturing are not certified.";
        return true;
    }
    private static bool SameBits(Point2 a,Point2 b)=>BitConverter.DoubleToInt64Bits(a.X)==BitConverter.DoubleToInt64Bits(b.X) &&
        BitConverter.DoubleToInt64Bits(a.Y)==BitConverter.DoubleToInt64Bits(b.Y);
    private static BigInteger Exact(double v)
    {
        long bits=BitConverter.DoubleToInt64Bits(v);int e=(int)((bits>>52)&0x7ff);long f=bits&0x000f_ffff_ffff_ffffL;
        BigInteger m=e==0?f:f|(1L<<52);if(bits<0)m=-m;return m<<(e==0?0:e-1);
    }
    private static bool OnSourceEdge(Point2 a,Point2 b,Point2 x,Point2 y)
    {
        if(!a.IsFinite || !b.IsFinite || !x.IsFinite || !y.IsFinite)return false;
        BigInteger dx=Exact(b.X)-Exact(a.X),dy=Exact(b.Y)-Exact(a.Y),len=dx*dx+dy*dy;
        BigInteger xx=Exact(x.X)-Exact(a.X),xy=Exact(x.Y)-Exact(a.Y);
        BigInteger yx=Exact(y.X)-Exact(a.X),yy=Exact(y.Y)-Exact(a.Y);
        var d1=xx*dx+xy*dy;var d2=yx*dx+yy*dy;
        return len>0 && dx*xy-dy*xx==0 && dx*yy-dy*yx==0 && d1>0 && d2>d1 && d2<len;
    }
}
