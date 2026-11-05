namespace FittedResearch;
// Geometric tangent candidate; source uses scipy BFGS and its exact optimizer
// result is not reproduced. Comparison is a numerical experiment only.
internal static class TangentCandidate
{
    internal readonly record struct Point(double X,double Y);
    internal static Point RelativeControl(Point start,Point end,bool tangentAtStart)
    {
        double dx=end.X-start.X,dy=end.Y-start.Y,length=Math.Sqrt(dx*dx+dy*dy);
        if(!double.IsFinite(length)||length<1e-6||!double.IsFinite(dy)||Math.Abs(dy)<1e-6) throw new ArgumentException("Invalid tangent geometry");
        double controlX=tangentAtStart?start.X:end.X;
        double controlY=tangentAtStart?start.Y+Math.CopySign(length/2,dy):end.Y-Math.CopySign(length/2,dy);
        double x=controlX-start.X,y=controlY-start.Y,denom=dx*dx+dy*dy;
        return new((x*dx+y*dy)/denom,(-x*dy+y*dx)/denom);
    }
}
