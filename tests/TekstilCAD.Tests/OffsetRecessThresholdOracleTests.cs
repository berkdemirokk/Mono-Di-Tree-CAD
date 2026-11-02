using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;

namespace TekstilCAD.Tests;

/// <summary>Hand-derived orthogonal reference around the exact recess-closure threshold.
/// This synthetic corpus is not a garment or production tolerance certification.</summary>
public sealed class OffsetRecessThresholdOracleTests(ITestOutputHelper output)
{
    private const long Scale=1_000_000, Allowance=5_000_000;
    private readonly record struct G(long X,long Y);
    private static Point2 Mm(G p)=>new(p.X/(double)Scale,p.Y/(double)Scale);
    private static G Grid(Point2 p)=>new(checked((long)Math.Round(p.X*Scale,MidpointRounding.AwayFromZero)),checked((long)Math.Round(p.Y*Scale,MidpointRounding.AwayFromZero)));
    private static bool OnSegment(G p,G a,G b)
    {
        if(a.X==b.X)return p.X==a.X && p.Y>=Math.Min(a.Y,b.Y) && p.Y<=Math.Max(a.Y,b.Y);
        if(a.Y==b.Y)return p.Y==a.Y && p.X>=Math.Min(a.X,b.X) && p.X<=Math.Max(a.X,b.X);
        throw new InvalidOperationException("Reference is not axis aligned.");
    }

    [Theory]
    [InlineData(-1_000_000L)] [InlineData(-1L)] [InlineData(0L)]
    [InlineData(1L)] [InlineData(2L)] [InlineData(8L)] [InlineData(100L)] [InlineData(10_000L)] [InlineData(1_000_000L)]
    public void EveryAcceptedRecessMatchesAnalyticBoundaryAndClosedRecessesAreRejected(long gap)
    {
        // For an outward offset d, the vertical sides of the U move toward one
        // another by d each. Their original gap is 2d+gap. Thus gap<=0 closes
        // the recess; gap>0 has exactly the following orthogonal miter boundary.
        const long one=Scale;var right=50*one+gap;
        G[] source=[new(0,0),new(100*one,0),new(100*one,100*one),new(right,100*one),new(right,20*one),new(40*one,20*one),new(40*one,100*one),new(0,100*one)];
        G[] expected=[new(-5*one,-5*one),new(105*one,-5*one),new(105*one,105*one),new(right-Allowance,105*one),new(right-Allowance,25*one),new(45*one,25*one),new(45*one,105*one),new(-5*one,105*one)];
        foreach(var reverse in new[] { false,true }) foreach(var shift in new[] { 0,3 })
        {
            var ordered=source.Select(Mm).ToList();if(reverse)ordered.Reverse();ordered=ordered.Skip(shift).Concat(ordered.Take(shift)).ToList();
            var piece=new PatternPiece { Closed=true,Points=ordered };var before=piece.Points.ToArray();PatternPiece? result=null;string outcome;
            try { result=PatternTools.Offset(piece,5);outcome="accepted"; }
            catch(ArgumentException ex) { outcome="rejected: "+ex.GetType().Name; }
            Assert.Equal(before,piece.Points);
            output.WriteLine($"gap={gap} grid, reverse={reverse}, start={shift}: {outcome}");
            if(gap<=0) { Assert.Null(result);continue; }
            if(gap==1_000_000)Assert.NotNull(result); // A full 1 mm opening remains.
            if(result is null)continue; // Conservative refusal is recorded, not treated as geometric success.
            var actual=result.Points.Select(Grid).ToList();if(actual.Count>1 && actual[0]==actual[^1])actual.RemoveAt(actual.Count-1);
            foreach(var corner in expected)Assert.Contains(corner,actual);
            for(var i=0;i<actual.Count;i++)
            {
                var a=actual[i];var b=actual[(i+1)%actual.Count];
                Assert.True(Enumerable.Range(0,expected.Length).Any(j=>OnSegment(a,expected[j],expected[(j+1)%expected.Length]) && OnSegment(b,expected[j],expected[(j+1)%expected.Length])),
                    $"Unexpected edge {a}–{b}, gap={gap}, reverse={reverse}, start={shift}.");
            }
        }
    }
}
