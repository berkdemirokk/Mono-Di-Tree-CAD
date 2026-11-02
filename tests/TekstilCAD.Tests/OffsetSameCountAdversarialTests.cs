using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

/// <summary>Adversarial indexed candidates; an experimental pass is not production approval.</summary>
public sealed class OffsetSameCountAdversarialTests(ITestOutputHelper log)
{
    private const long U=1_000_000;
    private static (bool Ok,string Reason) Verify(GridPoint[] source,GridPoint[] candidate,long delta=U)
    {
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetBudgetEvidenceExperiment",true)!
            .GetMethod("TryVerify",BindingFlags.Static|BindingFlags.NonPublic)!;
        IReadOnlyList<GridPoint>[] paths=[candidate];
        object?[] args=[source,paths,delta,125L,126L,1_000_000L,1_000L,500L,null,CancellationToken.None];
        bool ok=(bool)m.Invoke(null,args)!;return (ok,(string)args[8]!);
    }
    private static GridPoint[] Ideal(GridPoint[] source,long delta=U)
    {
        var m=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetCandidateProof",true)!
            .GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic)!;
        object?[] args=[source,delta,null,null,CancellationToken.None];
        Assert.True((bool)m.Invoke(null,args)!,(string?)args[3]);
        return ((IReadOnlyList<GridPoint>)args[2]!).ToArray();
    }
    private static string Production(GridPoint[] source,double delta=1)
    {
        var piece=new PatternPiece{Closed=true,Points=source.Select(p=>new Point2((double)p.X/U,(double)p.Y/U)).ToList()};
        try{_ = PatternTools.Offset(piece,delta);return "accepted";}
        catch(ArgumentException ex){return ex.GetType().Name+": "+ex.Message;}
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilledOneGridNotchKeepsEveryVertexButIsRejected(bool reverse)
    {
        GridPoint[] s=[new(0,0),new(100*U,0),new(100*U,100*U),new(50*U+2,100*U),
            new(50*U+2,100*U-1),new(50*U,100*U-1),new(50*U,100*U),new(0,100*U)];
        // Distinct points retain the same sequence, while the original recess
        // is replaced by a tiny outward bump rather than being represented.
        GridPoint[] c=[new(-U,-U),new(101*U,-U),new(101*U,101*U),new(50*U+3,101*U),
            new(50*U+3,101*U+1),new(50*U-1,101*U+1),new(50*U-1,101*U),new(-U,101*U)];
        if(reverse){Array.Reverse(s);Array.Reverse(c);}
        Assert.Equal(s.Length,c.Length);Assert.Equal(c.Length,c.Distinct().Count());
        var r=Verify(s,c);var production=Production(s);
        log.WriteLine($"one-grid reverse={reverse}; evidence={r.Ok} reason={r.Reason}; production={production}");
        Assert.False(r.Ok);Assert.Contains("Çok küçük keskin girinti",production);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChannelNarrowingWithoutPointLossIsMeasured(bool reverse)
    {
        GridPoint[] s=[new(0,0),new(120*U,0),new(120*U,120*U),new(60*U+500,120*U),
            new(60*U+500,60*U),new(60*U-500,60*U),new(60*U-500,120*U),new(0,120*U)];
        const long delta=200;
        var c=Ideal(s,delta);var original=c.ToArray();
        var boundary=original.ToArray();
        boundary[3]=boundary[3] with {X=boundary[3].X-50};boundary[4]=boundary[4] with {X=boundary[4].X-50};
        boundary[5]=boundary[5] with {X=boundary[5].X+50};boundary[6]=boundary[6] with {X=boundary[6].X+50};
        // Move both opposing walls toward the opening's centre by 75 grid
        // units while keeping each point, its index and all turn signs.
        c[3]=c[3] with {X=c[3].X-75};c[4]=c[4] with {X=c[4].X-75};
        c[5]=c[5] with {X=c[5].X+75};c[6]=c[6] with {X=c[6].X+75};
        if(reverse){Array.Reverse(s);Array.Reverse(c);Array.Reverse(original);Array.Reverse(boundary);}
        Assert.Equal(s.Length,c.Length);Assert.Equal(c.Length,c.Distinct().Count());
        long before=Math.Abs(original[reverse?4:3].X-original[reverse?1:6].X);
        long after=Math.Abs(c[reverse?4:3].X-c[reverse?1:6].X);
        Assert.Equal(150,before-after);Assert.Equal(600,before);Assert.Equal(450,after);
        var r=Verify(s,c,delta);var production=Production(s,(double)delta/U);
        var edge=Verify(s,boundary,delta);
        log.WriteLine($"narrowed reverse={reverse}; widthBefore={before} widthAfter={after} evidence={r.Ok} reason={r.Reason}; production={production}");
        Assert.Equal("accepted",production);Assert.False(r.Ok);Assert.Contains("minimum separation",r.Reason);
        Assert.True(edge.Ok,edge.Reason);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearbySidesCannotBeRemappedAndClosed(bool reverse)
    {
        GridPoint[] s=[new(0,0),new(120*U,0),new(120*U,120*U),new(60*U+50,120*U),
            new(60*U+50,20*U),new(60*U-50,20*U),new(60*U-50,120*U),new(0,120*U)];
        GridPoint[] c=[new(-U,-U),new(121*U,-U),new(121*U,121*U),new(60*U-1,121*U),
            new(60*U+1,21*U),new(60*U-1,21*U),new(60*U+1,121*U),new(-U,121*U)];
        if(reverse){Array.Reverse(s);Array.Reverse(c);}
        Assert.Equal(s.Length,c.Length);Assert.Equal(c.Length,c.Distinct().Count());
        var r=Verify(s,c);var production=Production(s);
        log.WriteLine($"remapped reverse={reverse}; evidence={r.Ok} reason={r.Reason}; production={production}");
        Assert.False(r.Ok);Assert.NotEqual("accepted",production);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdealAtCoordinateLimitCanBePushedPastLimitWithinBudget(bool reverse)
    {
        const long max=100_000_000L*U, delta=200;
        GridPoint[] s=[new(max-10_200,0),new(max-delta,0),new(max-delta,10_000),new(max-10_200,10_000)];
        var c=Ideal(s,delta);Assert.Equal(max,c.Max(p=>p.X));
        for(int i=0;i<c.Length;i++)c[i]=c[i] with {X=c[i].X+125};
        if(reverse){Array.Reverse(s);Array.Reverse(c);}
        var result=Verify(s,c,delta);var production=Production(s,(double)delta/U);
        log.WriteLine($"limit reverse={reverse}; max={c.Max(p=>p.X)} evidence={result.Ok} reason={result.Reason}; production={production}");
        Assert.False(result.Ok);Assert.Contains("coordinate range",result.Reason);Assert.Equal(max+125,c.Max(p=>p.X));
    }
}
