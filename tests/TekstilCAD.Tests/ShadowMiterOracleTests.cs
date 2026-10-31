using Clipper2Lib;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

// Diagnostic only: this independent floating-point construction does not establish
// production-grade numerical error bounds or replace the production safety guard.
public class ShadowMiterOracleTests
{
    private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;

    private static List<Point2> Candidate(IReadOnlyList<Point2> source, double allowance)
    {
        var orientation = Math.Sign(Geometry.SignedArea(source));
        var result = new List<Point2>();
        for (int i = 0; i < source.Count; i++)
        {
            var a = source[(i + source.Count - 1) % source.Count];
            var b = source[i];
            var c = source[(i + 1) % source.Count];
            var u = b - a; var v = c - b;
            var ul = Math.Sqrt(u.X*u.X + u.Y*u.Y); var vl = Math.Sqrt(v.X*v.X + v.Y*v.Y);
            var q = b + new Point2(orientation*u.Y/ul, -orientation*u.X/ul)*allowance;
            var r = b + new Point2(orientation*v.Y/vl, -orientation*v.X/vl)*allowance;
            var determinant = Cross(u,v);
            if (Math.Abs(determinant) < 1e-12) throw new InvalidOperationException("Parallel edges cannot be matched reliably.");
            result.Add(q + u*(Cross(r-q,v)/determinant));
        }
        return result;
    }

    private static double DistanceToBoundary(Point2 point, IReadOnlyList<Point2> polygon)
    {
        var best = double.PositiveInfinity;
        for (int i=0; i<polygon.Count; i++)
        {
            var a=polygon[i]; var d=polygon[(i+1)%polygon.Count]-a;
            var length=d.X*d.X+d.Y*d.Y;
            var t=Math.Clamp(((point.X-a.X)*d.X+(point.Y-a.Y)*d.Y)/length,0,1);
            var dx=point.X-a.X-t*d.X; var dy=point.Y-a.Y-t*d.Y;
            best=Math.Min(best,Math.Sqrt(dx*dx+dy*dy));
        }
        return best;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmoothLobedCandidateMatchesClipperLocally(bool reverse)
    {
        var points=Enumerable.Range(0,512).Select(i=> { var a=2*Math.PI*i/512; var r=1000+200*Math.Cos(3*a); return new Point2(r*Math.Cos(a),r*Math.Sin(a)); }).ToList();
        if(reverse) points.Reverse();
        var expected=Candidate(points,1);
        Assert.False(Geometry.HasSelfIntersections(expected));
        var path=new PathD(points.Select(p=>new PointD(p.X,p.Y)));
        var output=Clipper.InflatePaths(new PathsD { path },1,JoinType.Miter,EndType.Polygon,2,6);
        Assert.Single(output);
        var actual=output[0].Select(p=>new Point2(p.x,p.y)).ToList();
        Assert.All(expected,p=>Assert.InRange(DistanceToBoundary(p,actual),0,0.00002));
        Assert.All(actual,p=>Assert.InRange(DistanceToBoundary(p,expected),0,0.00002));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingNarrowRecessMakesIndependentMiterCandidateInvalid(bool reverse)
    {
        List<Point2> points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)];
        if(reverse) points.Reverse();
        var candidate=Candidate(points,5);
        Assert.True(Geometry.HasSelfIntersections(candidate));
    }
}
