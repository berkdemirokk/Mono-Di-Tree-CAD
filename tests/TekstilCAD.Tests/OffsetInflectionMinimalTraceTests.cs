using System.Collections;
using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Version-specific diagnostic of one corner and Clipper's cleanup, not a correctness proof.</summary>
public sealed class OffsetInflectionMinimalTraceTests(ITestOutputHelper log)
{
    private const long U=1_000_000;
    private static Point64 Lobed(int i)
    {
        double a=Math.Tau*i/512,r=1000+200*Math.Cos(3*a);
        return new((long)Math.Round(r*Math.Cos(a)*U,MidpointRounding.AwayFromZero),
            (long)Math.Round(r*Math.Sin(a)*U,MidpointRounding.AwayFromZero));
    }
    private static Path64 Minimal()
    {
        var a=Lobed(64);var b=Lobed(65);var c=Lobed(66);
        double dx=c.X-a.X,dy=c.Y-a.Y,len=Math.Sqrt(dx*dx+dy*dy);
        long x=(long)Math.Round(-dy/len*50*U),y=(long)Math.Round(dx/len*50*U);
        return new Path64{a,b,c,new(c.X+x,c.Y+y),new(a.X+x,a.Y+y)};
    }
    private static (Path64 Raw,Path64 Clean) Forward(Path64 path,long delta)
    {
        var o=new ClipperOffset(2);o.AddPath(path,JoinType.Miter,EndType.Polygon);
        var type=typeof(ClipperOffset);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var groups=(IList)type.GetField("_groupList",flags)!.GetValue(o)!;
        type.GetField("_delta",flags)!.SetValue(o,(double)delta);
        type.GetField("_mitLimSqr",flags)!.SetValue(o,0.5);
        type.GetMethod("DoGroupOffset",flags)!.Invoke(o,[groups[0]]);
        var raw=Assert.Single((Paths64)type.GetField("_solution",flags)!.GetValue(o)!);
        var clean=Assert.Single(Clipper.InflatePaths(new Paths64{path},delta,JoinType.Miter,EndType.Polygon,2));
        return (raw,clean);
    }
    private static double MinEdge(Path64 path)=>Enumerable.Range(0,path.Count).Min(i=>
    {
        var a=path[i];var b=path[(i+1)%path.Count];return Math.Sqrt(Math.Pow((double)b.X-a.X,2)+Math.Pow((double)b.Y-a.Y,2));
    });
    private static double MaxVertexDistance(Path64 from,Path64 to)=>from.Max(p=>to.Select((a,i)=>
    {
        var b=to[(i+1)%to.Count];double dx=(double)b.X-a.X,dy=(double)b.Y-a.Y,px=(double)p.X-a.X,py=(double)p.Y-a.Y;
        double len=dx*dx+dy*dy,t=len==0?0:Math.Clamp((px*dx+py*dy)/len,0,1);
        return Math.Sqrt(Math.Pow(px-t*dx,2)+Math.Pow(py-t*dy,2));
    }).Min());
    private static (double X,double Y) Intersection(Point64 a,Point64 b,Point64 c,Point64 d)
    {
        double ux=(double)b.X-a.X,uy=(double)b.Y-a.Y,vx=(double)d.X-c.X,vy=(double)d.Y-c.Y;
        double wx=(double)c.X-a.X,wy=(double)c.Y-a.Y,t=(wx*vy-wy*vx)/(ux*vy-uy*vx);
        return (a.X+t*ux,a.Y+t*uy);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreeConsecutiveRealVerticesAndTwoClosingVerticesTraceCleanup(bool reverse)
    {
        var source=Minimal();if(reverse)source.Reverse();var (raw,clean)=Forward(source,U);
        var back=Assert.Single(Clipper.InflatePaths(new Paths64{clean},-U,JoinType.Miter,EndType.Polygon,2));
        var high=new Path64(source.Select(p=>new Point64(p.X*10,p.Y*10)));var (rawHigh,cleanHigh)=Forward(high,10*U);
        var down=new Path64(cleanHigh.Select(p=>new Point64((long)Math.Round(p.X/10.0,MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y/10.0,MidpointRounding.AwayFromZero))));
        var backHigh=Assert.Single(Clipper.InflatePaths(new Paths64{down},-U,JoinType.Miter,EndType.Polygon,2));
        int created=clean.Count(p=>!raw.Any(q=>q.X==p.X && q.Y==p.Y));
        int pivot=raw.FindIndex(p=>p.X==Lobed(65).X && p.Y==Lobed(65).Y);
        Assert.True(pivot>=0);
        var intersection=Intersection(raw[(pivot+raw.Count-2)%raw.Count],raw[(pivot+raw.Count-1)%raw.Count],
            raw[(pivot+1)%raw.Count],raw[(pivot+2)%raw.Count]);
        int highPivot=rawHigh.FindIndex(p=>p.X==Lobed(65).X*10 && p.Y==Lobed(65).Y*10);
        Assert.True(highPivot>=0);
        var highIntersection=Intersection(rawHigh[(highPivot+rawHigh.Count-2)%rawHigh.Count],rawHigh[(highPivot+rawHigh.Count-1)%rawHigh.Count],
            rawHigh[(highPivot+1)%rawHigh.Count],rawHigh[(highPivot+2)%rawHigh.Count]);
        var added=Assert.Single(clean.Where(p=>!raw.Any(q=>q.X==p.X && q.Y==p.Y)));
        double intersectionError=Math.Sqrt(Math.Pow(added.X-intersection.X,2)+Math.Pow(added.Y-intersection.Y,2));
        log.WriteLine($"reverse={reverse} source={source.Count} raw={raw.Count} clean={clean.Count} unionCreated={created} rawMin={MinEdge(raw):F6} cleanMin={MinEdge(clean):F6} back->source={MaxVertexDistance(back,source):F6}; high raw={rawHigh.Count} clean={cleanHigh.Count} downMin={MinEdge(down):F6} highBack->source={MaxVertexDistance(backHigh,source):F6}");
        log.WriteLine("source="+string.Join(" ",source.Select(p=>$"({p.X},{p.Y})")));
        log.WriteLine("raw="+string.Join(" ",raw.Select(p=>$"({p.X},{p.Y})")));
        log.WriteLine("clean="+string.Join(" ",clean.Select(p=>$"({p.X},{p.Y})")));
        log.WriteLine($"unrounded adjacent raw-edge intersection=({intersection.X:F6},{intersection.Y:F6}); new union vertex distance={intersectionError:F6} grid");
        log.WriteLine($"high-grid adjacent raw-edge intersection in source grid=({highIntersection.X/10:F6},{highIntersection.Y/10:F6}); intersection displacement={Math.Sqrt(Math.Pow(intersection.X-highIntersection.X/10,2)+Math.Pow(intersection.Y-highIntersection.Y/10,2)):F6} grid");
        Assert.Equal(5,source.Count);
        Assert.Equal(7,raw.Count);Assert.Equal(6,clean.Count);Assert.Equal(1,created);
        Assert.InRange(MinEdge(clean),1.4,1.5);Assert.InRange(intersectionError,400,550);
        Assert.InRange(MaxVertexDistance(back,source),540,546);
        Assert.Equal(5,cleanHigh.Count);Assert.InRange(MaxVertexDistance(backHigh,source),0,8);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneGridNotchHitsSeparateSmallSharpRecessGuard(bool reverse)
    {
        var piece=new PatternPiece{Closed=true,Points=[new(0,0),new(100,0),new(100,100),
            new(50.000002,100),new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)]};
        if(reverse)piece.Points.Reverse();
        var ex=Assert.Throws<ArgumentException>(()=>PatternTools.Offset(piece,1));
        Assert.Contains("Çok küçük keskin girinti",ex.Message);
    }
}
