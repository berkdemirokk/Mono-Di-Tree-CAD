namespace FittedResearch;

// Straight-base triangular cut. Inputs are cm. This reproduces the geometric
// triangle (side_len overload), not scipy's curved-edge cut optimizer.
internal static class DartCut
{
    internal readonly record struct P(double X,double Y);
    internal readonly record struct Triangle(P Start,P Tip,P End);
    internal static Triangle Horizontal(double startX,double endX,double y,double width,double legLength,bool tipUp)
    {
        if(new[]{startX,endX,y,width,legLength}.Any(v=>!double.IsFinite(v)) || width<=0 || legLength<=0 || Math.Abs(endX-startX)<1e-10 || Math.Abs(Math.Abs(endX-startX)-width)>1e-8 || legLength<=width/2) throw new ArgumentException("Invalid dart triangle.");
        double depth=Math.Sqrt(legLength*legLength-width*width/4);
        return new(new(startX,y),new((startX+endX)/2,y+(tipUp?depth:-depth)),new(endX,y));
    }
    internal static bool SimpleClosed(IReadOnlyList<P> points)
    {
        if(points.Count<3 || points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y))) return false;
        double Cross(P a,P b,P c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        bool On(P a,P b,P p)=>p.X>=Math.Min(a.X,b.X)&&p.X<=Math.Max(a.X,b.X)&&p.Y>=Math.Min(a.Y,b.Y)&&p.Y<=Math.Max(a.Y,b.Y);
        bool Intersects(P a,P b,P c,P d)
        {
            double x=Cross(a,b,c),y=Cross(a,b,d),z=Cross(c,d,a),w=Cross(c,d,b);
            return x*y<0&&z*w<0 || x==0&&On(a,b,c) || y==0&&On(a,b,d) || z==0&&On(c,d,a) || w==0&&On(c,d,b);
        }
        double area=0; for(int i=0;i<points.Count;i++) { var a=points[i]; var b=points[(i+1)%points.Count]; area+=a.X*b.Y-a.Y*b.X; }
        if(!double.IsFinite(area) || area==0) return false;
        for(int i=0;i<points.Count;i++)
        {
            P a=points[i],b=points[(i+1)%points.Count]; if(a==b) return false;
            for(int j=i+1;j<points.Count;j++)
            {
                P c=points[j],d=points[(j+1)%points.Count];
                if(j==i+1 || i==0&&j==points.Count-1)
                {
                    P common=j==i+1?b:a, first=j==i+1?a:b, last=j==i+1?d:c;
                    if(Cross(first,common,last)==0 && (first.X-common.X)*(last.X-common.X)+(first.Y-common.Y)*(last.Y-common.Y)>0) return false;
                }
                else if(Intersects(a,b,c,d)) return false;
            }
        }
        return true;
    }
}
