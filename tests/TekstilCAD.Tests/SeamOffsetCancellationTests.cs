using System.Reflection;
using Clipper2Lib;
using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class SeamOffsetCancellationTests
{
    private static PatternPiece Square() => new() { Name="Kare", Points=[new(0,0),new(100,0),new(100,100),new(0,100)] };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void AlreadyCancelledDoesNotChangeSource(double allowance)
    {
        var source=Square(); var original=source.Clone();
        using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PatternTools.Offset(source,allowance,cancelled.Token));
        Assert.Equal(original.Name,source.Name);
        Assert.Equal(original.Points,source.Points);
        Assert.Equal(original.Id,source.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UncancelledOverloadMatchesOriginalApi(double allowance)
    {
        var source=Square(); var before=source.Points.ToArray();
        var oldResult=PatternTools.Offset(source,allowance);
        var newResult=PatternTools.Offset(source,allowance,CancellationToken.None);
        Assert.Equal(oldResult.Name,newResult.Name);
        Assert.Equal(oldResult.Points,newResult.Points);
        Assert.Equal(before,source.Points);
    }

    [Fact]
    public void UncancelledOverloadStillRejectsCollapsedRecess()
    {
        var source=new PatternPiece { Points=[new(0,0),new(100,0),new(100,100),new(52,100),new(52,20),new(48,20),new(48,100),new(0,100)] };
        var original=source.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(source,5,CancellationToken.None));
        Assert.Equal(original,source.Points);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncancelledUnverifiedLobedContourRetainsDetachedPreview(bool reverse)
    {
        var source = new PatternPiece { Points = Enumerable.Range(0, 512).Select(i =>
        {
            double a = Math.Tau * i / 512, r = 1000 + 200 * Math.Cos(3 * a);
            return new Point2(r * Math.Cos(a), r * Math.Sin(a));
        }).ToList() };
        if (reverse) source.Points.Reverse();
        var before = source.Points.ToArray();
        using var cancellation = new CancellationTokenSource();
        var failure = Assert.Throws<OffsetLocalDetailUnverifiedException>(() => PatternTools.Offset(source, 1, cancellation.Token));
        Assert.False(failure.Preview.IsDefaultOrEmpty);
        Assert.NotNull(failure.ComparisonSide);
        Assert.NotNull(failure.ComparisonIndex);
        Assert.NotNull(failure.SamplePoint);
        Assert.Equal(failure.ComparisonSide == OffsetComparisonSide.Source ? failure.ComparisonIndex : null, failure.SourceIndex);
        var preview = failure.Preview.ToArray();
        Assert.All(preview, p => Assert.True(p.IsFinite));
        source.Points[0] = new Point2(0, 0);
        Assert.Equal(preview, failure.Preview.ToArray());
        Assert.Equal(before.Skip(1), source.Points.Skip(1));
    }

    [Fact]
    public void LocalComparisonIdentifiesTheContourContainingAnUnmatchedSample()
    {
        Path64 source=[new(0,0),new(100,0),new(100,100),new(0,100)];
        Path64 withSpike=[new(0,0),new(25,0),new(25,-20),new(30,0),new(100,0),new(100,100),new(0,100)];
        var method=typeof(PatternTools).GetMethod("TryFindUnmatchedSample",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] reverse=[source,withSpike,1d,CancellationToken.None,null,null,null];
        Assert.True((bool)method.Invoke(null,reverse)!);
        Assert.Equal(OffsetComparisonSide.Recovered,reverse[4]);
        Assert.InRange((int)reverse[5]!,0,withSpike.Count-1);
        object?[] forward=[withSpike,source,1d,CancellationToken.None,null,null,null];
        Assert.True((bool)method.Invoke(null,forward)!);
        Assert.Equal(OffsetComparisonSide.Source,forward[4]);
        object?[] same=[source,source,1d,CancellationToken.None,null,null,null];
        Assert.False((bool)method.Invoke(null,same)!);
    }

    [Fact]
    public void LocalMatchingHonorsCancellationAndRetainsOriginalThreeArgumentHelper()
    {
        var flags=BindingFlags.NonPublic|BindingFlags.Static;
        var original=typeof(PatternTools).GetMethod("LocallyMatches",flags)!;
        var cancellable=typeof(PatternTools).GetMethod("LocallyMatchesWithCancellation",flags)!;
        Path64 square=[new(0,0),new(10,0),new(10,10),new(0,10)];
        Assert.True((bool)original.Invoke(null,[square,square,8d])!);
        using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
        var error=Assert.Throws<TargetInvocationException>(()=>cancellable.Invoke(null,[square,square,8d,cancelled.Token]));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
    }
}
