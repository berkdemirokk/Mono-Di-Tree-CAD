namespace FittedResearch;

// Restricted research experiment for the source's two-dimensional tangent-only
// objective. Unsupported line-search branches fail closed; never production geometry.
internal static class TangentBfgsExperiment
{
    internal readonly record struct V(double X,double Y)
    {
        public static V operator +(V a,V b)=>new(a.X+b.X,a.Y+b.Y);
        public static V operator -(V a,V b)=>new(a.X-b.X,a.Y-b.Y);
        public static V operator *(V a,double k)=>new(a.X*k,a.Y*k);
        public double Dot(V b)=>X*b.X+Y*b.Y;
        public double Norm()=>Math.Sqrt(Dot(this));
    }
    internal sealed record Result(V Control,int Iterations)
    {
        public string Status=>"unresolved";public bool ExportAllowed=>false;public bool ProductionAllowed=>false;
    }
    internal static Result Solve(double degrees)
    {
        if(!double.IsFinite(degrees)||degrees<=0||degrees>=45)throw new ArgumentException("Unsupported angle");
        double ratio=Math.Tan(degrees*Math.PI/180)/4,n=Math.Sqrt(1+ratio*ratio); V target=new(1/n,-ratio/n);
        double F(V x) {double norm=x.Norm();if(!double.IsFinite(norm)||norm<1e-12)throw new InvalidOperationException("Degenerate tangent");double a=x.X/norm-target.X,b=x.Y/norm-target.Y;return a*a+b*b;}
        V Grad(V x) {const double e=1.4901161193847656e-8;double f=F(x);return new((F(x+new V(e,0))-f)/e,(F(x+new V(0,e))-f)/e);}
        V x=new(.5,0),g=Grad(x);double f0=F(x),oldold=f0+g.Norm()/2;double h00=1,h01=0,h10=0,h11=1;
        for(int k=0;k<20;k++) {
            if(Math.Max(Math.Abs(g.X),Math.Abs(g.Y))<1e-5)return new(x,k);
            V p=new(-h00*g.X-h01*g.Y,-h10*g.X-h11*g.Y);double d0=g.Dot(p);
            if(!double.IsFinite(d0)||d0>=0)throw new InvalidOperationException("Not a descent direction");
            double alpha=Math.Min(1,1.01*2*(f0-oldold)/d0);if(alpha<=0||!double.IsFinite(alpha))throw new InvalidOperationException("Invalid initial step");
            double sx=0,fx=f0,dx=d0,sy=0,fy=f0,dy=d0;bool bracket=false;double width=1e100,width1=2e100;
            V next=default,gn=default;double fn=0;bool accepted=false;
            for(int j=0;j<30;j++) {
                next=x+p*alpha;fn=F(next);gn=Grad(next);double dp=gn.Dot(p),test=f0+alpha*1e-4*d0;
                if(fn<=test&&Math.Abs(dp)<=.9*(-d0)) {accepted=true;break;}
                // The modified-function stage is intentionally not approximated.
                if(fn<=fx && fn>test)throw new InvalidOperationException("Unsupported modified line-search stage");
                double theta,scale,gamma,pc,qc,cubic,secant,step;double sign=dp*Math.Sign(dx);
                if(fn>fx) {
                    theta=3*(fx-fn)/(alpha-sx)+dx+dp;scale=Math.Max(Math.Abs(theta),Math.Max(Math.Abs(dx),Math.Abs(dp)));
                    gamma=scale*Math.Sqrt(Math.Max(0,Math.Pow(theta/scale,2)-(dx/scale)*(dp/scale)));if(alpha<sx)gamma=-gamma;
                    pc=(gamma-dx)+theta;qc=((gamma-dx)+gamma)+dp;cubic=sx+pc/qc*(alpha-sx);
                    secant=sx+(dx/((fx-fn)/(alpha-sx)+dx)/2)*(alpha-sx);
                    step=Math.Abs(cubic-sx)<=Math.Abs(secant-sx)?cubic:cubic+(secant-cubic)/2;bracket=true;
                } else if(sign<0) {
                    theta=3*(fx-fn)/(alpha-sx)+dx+dp;scale=Math.Max(Math.Abs(theta),Math.Max(Math.Abs(dx),Math.Abs(dp)));
                    gamma=scale*Math.Sqrt(Math.Max(0,Math.Pow(theta/scale,2)-(dx/scale)*(dp/scale)));if(alpha>sx)gamma=-gamma;
                    pc=(gamma-dp)+theta;qc=((gamma-dp)+gamma)+dx;cubic=alpha+pc/qc*(sx-alpha);
                    secant=alpha+dp/(dp-dx)*(sx-alpha);step=Math.Abs(cubic-alpha)>Math.Abs(secant-alpha)?cubic:secant;bracket=true;
                } else throw new InvalidOperationException("Unsupported line search branch");
                if(fn>fx) {sy=alpha;fy=fn;dy=dp;} else {if(sign<0){sy=sx;fy=fx;dy=dx;}sx=alpha;fx=fn;dx=dp;}
                if(bracket) {if(Math.Abs(sy-sx)>=.66*width1)step=sx+.5*(sy-sx);width1=width;width=Math.Abs(sy-sx);}
                if(!double.IsFinite(step)||step<=0||(bracket&&(step<=Math.Min(sx,sy)||step>=Math.Max(sx,sy))))throw new InvalidOperationException("Unsupported line-search step");
                alpha=step;
            }
            if(!accepted)throw new InvalidOperationException("Line search did not converge");
            V s=next-x,y=gn-g;double ys=y.Dot(s);if(!double.IsFinite(ys)||Math.Abs(ys)<1e-30)throw new InvalidOperationException("Degenerate Hessian update");
            double rho=1/ys,a00=1-rho*s.X*y.X,a01=-rho*s.X*y.Y,a10=-rho*s.Y*y.X,a11=1-rho*s.Y*y.Y;
            double b00=a00*h00+a01*h10,b01=a00*h01+a01*h11,b10=a10*h00+a11*h10,b11=a10*h01+a11*h11;
            (h00,h01,h10,h11)=(b00*a00+b01*a01+rho*s.X*s.X,b00*a10+b01*a11+rho*s.X*s.Y,b10*a00+b11*a01+rho*s.Y*s.X,b10*a10+b11*a11+rho*s.Y*s.Y);
            oldold=f0;f0=fn;x=next;g=gn;
        }
        throw new InvalidOperationException("Iteration limit");
    }
}
