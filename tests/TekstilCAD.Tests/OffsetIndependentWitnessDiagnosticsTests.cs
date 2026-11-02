using System.Numerics;
using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Sampled numerical diagnostics only, not a feature-preservation proof.</summary>
public sealed class OffsetIndependentWitnessDiagnosticsTests(ITestOutputHelper log)
{
    private const long U=1_000_000;
    private readonly record struct Witness(double Distance,int SampleEdge,bool Midpoint,int TargetEdge);
    private static Witness Maximum(Path64 from,Path64 to)
    {
        var worst=new Witness(-1,0,false,0);
        for(int i=0;i<from.Count;i++)for(int half=0;half<2;half++)
        {
            var origin=from[i];var next=from[(i+1)%from.Count];
            double x=(next.X-origin.X)*half/2.0,y=(next.Y-origin.Y)*half/2.0;
            double best=double.PositiveInfinity;int edge=-1;
            for(int j=0;j<to.Count;j++)
            {
                var a=to[j];var b=to[(j+1)%to.Count];
                double ax=a.X-origin.X,ay=a.Y-origin.Y,dx=b.X-a.X,dy=b.Y-a.Y;
                double len=dx*dx+dy*dy,t=len==0?0:Math.Clamp(((x-ax)*dx+(y-ay)*dy)/len,0,1);
                double ex=x-ax-t*dx,ey=y-ay-t*dy,d=ex*ex+ey*ey;
                if(d<best){best=d;edge=j;}
            }
            double distance=Math.Sqrt(best);
            if(distance>worst.Distance)worst=new(distance,i,half==1,edge);
        }
        return worst;
    }

    // Compare the selected vertex/midpoint to every segment using integer
    // rational arithmetic, independently of the double-based witness search.
    private static bool ExactlyWithinEight(Path64 from,Path64 to,Witness w)
    {
        var p=from[w.SampleEdge];var q=from[(w.SampleEdge+1)%from.Count];
        BigInteger x=(BigInteger)p.X*2+(w.Midpoint?(BigInteger)q.X-p.X:0);
        BigInteger y=(BigInteger)p.Y*2+(w.Midpoint?(BigInteger)q.Y-p.Y:0);
        for(int i=0;i<to.Count;i++)
        {
            var a=to[i];var b=to[(i+1)%to.Count];
            BigInteger ax=(BigInteger)a.X*2,ay=(BigInteger)a.Y*2;
            BigInteger dx=((BigInteger)b.X-a.X)*2,dy=((BigInteger)b.Y-a.Y)*2;
            BigInteger px=x-ax,py=y-ay,len=dx*dx+dy*dy,dot=px*dx+py*dy;
            if(len.IsZero || dot<=0){if(px*px+py*py<=256)return true;}
            else if(dot>=len){var ex=px-dx;var ey=py-dy;if(ex*ex+ey*ey<=256)return true;}
            else {var cross=px*dy-py*dx;if(cross*cross<=256*len)return true;}
        }
        return false;
    }
    private static double Turn(Path64 path,int i)
    {
        var a=path[(i+path.Count-1)%path.Count];var b=path[i];var c=path[(i+1)%path.Count];
        double ux=b.X-a.X,uy=b.Y-a.Y,vx=c.X-b.X,vy=c.Y-b.Y;
        return Math.Atan2(ux*vy-uy*vx,ux*vx+uy*vy)*180/Math.PI;
    }
    private void Report(string name,Path64 source,Path64 forward,Path64 recovered)
    {
        var a=Maximum(source,recovered);var b=Maximum(recovered,source);
        var shortest=Enumerable.Range(0,forward.Count).Select(i=>
        {
            var p=forward[i];var q=forward[(i+1)%forward.Count];
            return (Index:i,Length:Math.Sqrt(Math.Pow((double)q.X-p.X,2)+Math.Pow((double)q.Y-p.Y,2)));
        }).MinBy(x=>x.Length);
        log.WriteLine($"{name}; counts={source.Count}/{forward.Count}/{recovered.Count}; source->inverse={a.Distance:F6} (edge {a.SampleEdge}, midpoint {a.Midpoint}, source turn {Turn(source,a.SampleEdge):F6}°, exact <=8 {ExactlyWithinEight(source,recovered,a)}); inverse->source={b.Distance:F6} (edge {b.SampleEdge}, midpoint {b.Midpoint}, nearest source edge {b.TargetEdge}, source endpoint turns {Turn(source,b.TargetEdge):F6}°/{Turn(source,(b.TargetEdge+1)%source.Count):F6}°, exact <=8 {ExactlyWithinEight(recovered,source,b)}); forward shortest={shortest.Length:F6} grid at {shortest.Index}");
    }
    private static (Path64 Forward,Path64 Recovered) Offset(Path64 source,long delta=U)
    {
        var f=Assert.Single(Clipper.InflatePaths(new Paths64{source},delta,JoinType.Miter,EndType.Polygon,2));
        var r=Assert.Single(Clipper.InflatePaths(new Paths64{f},-delta,JoinType.Miter,EndType.Polygon,2));
        return (f,r);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LobedWitnessLocatesInverseArtifact(bool reverse)
    {
        var source=new Path64(Enumerable.Range(0,512).Select(i=>
        {
            double a=Math.Tau*i/512,r=1000+200*Math.Cos(3*a);
            return new Point64((long)Math.Round(r*Math.Cos(a)*U,MidpointRounding.AwayFromZero),
                (long)Math.Round(r*Math.Sin(a)*U,MidpointRounding.AwayFromZero));
        }));
        if(reverse)source.Reverse();var (forward,recovered)=Offset(source);
        var a=Maximum(source,recovered);var b=Maximum(recovered,source);Report($"lobed reverse={reverse}",source,forward,recovered);
        Assert.True(a.Distance<8);Assert.True(b.Distance>8);
        Assert.True(ExactlyWithinEight(source,recovered,a));Assert.False(ExactlyWithinEight(recovered,source,b));
    }
    [Theory]
    [InlineData(1L,false)]
    [InlineData(1L,true)]
    [InlineData(500L,false)]
    [InlineData(500L,true)]
    [InlineData(5000L,false)]
    [InlineData(5000L,true)]
    public void CompareRealNotchLossAgainstSameWitness(long depth,bool reverse)
    {
        var source=new Path64{new(0,0),new(100*U,0),new(100*U,100*U),
            new(50*U+2*depth,100*U),new(50*U+2*depth,100*U-depth),
            new(50*U,100*U-depth),new(50*U,100*U),new(0,100*U)};
        if(reverse)source.Reverse();var (forward,recovered)=Offset(source);
        Report($"notch depth={depth} reverse={reverse}",source,forward,recovered);
        var a=Maximum(source,recovered);var b=Maximum(recovered,source);
        if(depth==1){Assert.True(ExactlyWithinEight(source,recovered,a));Assert.True(ExactlyWithinEight(recovered,source,b));}
        else Assert.False(ExactlyWithinEight(source,recovered,a));
        if(depth==500){Assert.True(a.Distance<543);Assert.True(b.Distance<543);}
        var piece=new PatternPiece{Closed=true,Points=source.Select(p=>new Point2((double)p.X/U,(double)p.Y/U)).ToList()};
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(piece,1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedNarrowChannelRemainsDetectable(bool reverse)
    {
        var source=new Path64{new(0,0),new(100*U,0),new(100*U,100*U),new(52*U,100*U),
            new(52*U,20*U),new(48*U,20*U),new(48*U,100*U),new(0,100*U)};
        if(reverse)source.Reverse();var (forward,recovered)=Offset(source,5*U);
        Report($"channel reverse={reverse}",source,forward,recovered);
        Assert.False(ExactlyWithinEight(source,recovered,Maximum(source,recovered)));
        var piece=new PatternPiece{Closed=true,Points=source.Select(p=>new Point2((double)p.X/U,(double)p.Y/U)).ToList()};
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(piece,5));
    }
}
