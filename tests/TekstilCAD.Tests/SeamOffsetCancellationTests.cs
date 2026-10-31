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
