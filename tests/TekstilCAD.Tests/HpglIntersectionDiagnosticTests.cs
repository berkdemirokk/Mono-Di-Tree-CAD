using TekstilCAD.FileFormats;
using P = TekstilCAD.FileFormats.HpglIntersectionDiagnostic.Point;

namespace TekstilCAD.Tests;

public sealed class HpglIntersectionDiagnosticTests
{
    private static bool Oracle(IReadOnlyList<P> points, bool closed)
    {
        var edges = new List<(P A, P B)>();
        var count = closed ? points.Count : points.Count - 1;
        for (var i = 0; i < count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            if (a != b) edges.Add((a, b));
        }
        static Int128 Cross(P a, P b, P c) => ((Int128)b.X-a.X)*((Int128)c.Y-a.Y)-((Int128)b.Y-a.Y)*((Int128)c.X-a.X);
        static bool On(P a,P b,P p) => Cross(a,b,p)==0 && p.X>=Math.Min(a.X,b.X) && p.X<=Math.Max(a.X,b.X) && p.Y>=Math.Min(a.Y,b.Y) && p.Y<=Math.Max(a.Y,b.Y);
        static bool Opp(Int128 a, Int128 b) => a<0 && b>0 || a>0 && b<0;
        for (var i=0;i<edges.Count;i++) for(var j=i+1;j<edges.Count;j++)
        {
            var (a,b)=edges[i]; var (c,d)=edges[j];
            if (j==i+1 || closed && i==0 && j==edges.Count-1)
            {
                var shared=j==i+1?b:a; var first=j==i+1?a:b; var second=j==i+1?d:c;
                var dot=((Int128)first.X-shared.X)*((Int128)second.X-shared.X)+((Int128)first.Y-shared.Y)*((Int128)second.Y-shared.Y);
                if (Cross(first,shared,second)==0 && dot>0) return false;
            }
            else if (Opp(Cross(a,b,c),Cross(a,b,d)) && Opp(Cross(c,d,a),Cross(c,d,b)) || On(a,b,c)||On(a,b,d)||On(c,d,a)||On(c,d,b)) return false;
        }
        return true;
    }
    private static void Compare(List<P> points, bool closed)
    {
        var copy=points.ToArray(); var expected=Oracle(points,closed);
        var error=Record.Exception(()=>HpglIntersectionDiagnostic.Validate(points,closed));
        Assert.Equal(expected,error is null);
        if (!expected) Assert.IsType<InvalidDataException>(error);
        Assert.Equal(copy,points);
    }
    [Fact]
    public void CrossingOverlapBacktrackingAndExplicitClosureMatchBruteOracle()
    {
        List<List<P>> inputs = [
            [new(0,0),new(100,0),new(100,100),new(0,100)],
            [new(0,0),new(100,100),new(0,100),new(100,0)],
            [new(0,0),new(100,0),new(40,0),new(100,100),new(0,100)],
            [new(0,0),new(100,0),new(100,100),new(0,100),new(0,0)],
            [new(0,0),new(0,0),new(100,0),new(100,100),new(0,100)],
            [new(0,0),new(100,0),new(100,100),new(50,100),new(50,0),new(0,0)]
        ];
        foreach(var input in inputs) foreach(var closed in new[]{false,true}) { Compare(input,closed); input.Reverse(); Compare(input,closed); input.Reverse(); }
    }
    [Fact]
    public void ExtremeIntegerCoordinatesDoNotOverflowAndMatchOracle()
    {
        List<P> corners=[new(int.MinValue,int.MinValue),new(int.MaxValue,int.MinValue),new(int.MaxValue,int.MaxValue),new(int.MinValue,int.MaxValue)];
        Compare(corners,true); (corners[1],corners[2])=(corners[2],corners[1]); Compare(corners,true);
    }
    [Fact]
    public void SeededPathsAndCrossingsMatchIndependentBruteForce()
    {
        var rng=new Random(20261031);
        for(var run=0;run<200;run++)
        {
            var count=rng.Next(4,32);var points=Enumerable.Range(0,count).Select(i=>
            { var angle=Math.Tau*i/count; var radius=10000+rng.Next(0,3000); return new P((int)Math.Round(radius*Math.Cos(angle)),(int)Math.Round(radius*Math.Sin(angle))); }).ToList();
            Compare(points,true); Compare(points,false);
            (points[1],points[count/2])=(points[count/2],points[1]);Compare(points,true); points.Reverse();Compare(points,true);
        }
    }
    [Theory]
    [InlineData(4097)]
    [InlineData(10000)]
    public void LargeSimpleContourIsCheckedWithoutMutation(int count)
    {
        var points=Enumerable.Range(0,count).Select(i=> { var angle=Math.Tau*i/count;return new P((int)Math.Round(1_000_000*Math.Cos(angle)),(int)Math.Round(1_000_000*Math.Sin(angle))); }).ToList();
        var copy=points.ToArray(); var result=HpglIntersectionDiagnostic.Validate(points,true);
        Assert.True(result.BroadphaseRejected>0);Assert.Equal(copy,points);
    }
    [Fact]
    public void BudgetAndCancellationFailWithoutChangingInput()
    {
        List<P> points=[new(0,0),new(100,0),new(100,100),new(0,100)];var copy=points.ToArray();
        Assert.Throws<InvalidDataException>(()=>HpglIntersectionDiagnostic.Validate(points,true,maxPairs:1));
        using var cts=new CancellationTokenSource();cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>HpglIntersectionDiagnostic.Validate(points,true,cancellationToken:cts.Token));
        Assert.Equal(copy,points);
    }
}
