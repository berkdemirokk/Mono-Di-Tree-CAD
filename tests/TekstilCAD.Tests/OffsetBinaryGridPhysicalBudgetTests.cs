using System.Reflection;
using System.Diagnostics;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Xunit.Abstractions;
using static TekstilCAD.Patterns.OffsetMiterIntervalDiagnostic;

namespace TekstilCAD.Tests;

public sealed class OffsetBinaryGridPhysicalBudgetTests(ITestOutputHelper output)
{
    private static (bool Ok,string Reason) Verify(Point2[] source,GridPoint[] grid,double point,double length,double height)
    {
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetBinaryGridPhysicalBudget",true)!
            .GetMethod("TryVerify",BindingFlags.NonPublic|BindingFlags.Static)!;
        object?[] args=[source,grid,point,length,height,null,CancellationToken.None];
        return ((bool)method.Invoke(null,args)!,(string)args[5]!);
    }
    private static GridPoint[] Grid(Point2[] p)=>p.Select(v=>new GridPoint(
        (long)Math.Round(v.X*1_000_000,MidpointRounding.AwayFromZero),
        (long)Math.Round(v.Y*1_000_000,MidpointRounding.AwayFromZero))).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameGridDoesNotEraseDifferentSourceWidthBudget(bool reverse)
    {
        const double inset=.00000049;
        Point2[] outer=[new(0,0),new(100,0),new(100,100),new(0,100)];
        Point2[] inner=[new(inset,inset),new(100-inset,inset),new(100-inset,100-inset),new(inset,100-inset)];
        if(reverse){Array.Reverse(outer);Array.Reverse(inner);}
        var grid=Grid(outer); Assert.Equal(grid,Grid(inner));
        var before=inner.ToArray();
        Assert.True(Verify(outer,grid,.000001,.00000097,.000001).Ok);
        Assert.False(Verify(inner,grid,.000001,.00000097,.000001).Ok);
        Assert.True(Verify(inner,grid,.000001,.0000015,.000001).Ok);
        Assert.False(Verify(inner,grid,.0000006,.0000015,.000001).Ok);
        Assert.Equal(before,inner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameGridDoesNotEraseDifferentSourceLocalHeight(bool reverse)
    {
        Point2[] deeper=[new(0,0),new(100,0),new(100,100),new(50,100-.00001049),new(0,100)];
        Point2[] shallower=[new(0,0),new(100,0),new(100,100),new(50,100-.00000951),new(0,100)];
        if(reverse){Array.Reverse(deeper);Array.Reverse(shallower);}
        var grid=Grid(deeper);Assert.Equal(grid,Grid(shallower));
        Assert.False(Verify(deeper,grid,.000001,.000002,.00000048).Ok);
        Assert.False(Verify(shallower,grid,.000001,.000002,.00000048).Ok);
        Assert.True(Verify(deeper,grid,.000001,.000002,.00000051).Ok);
        Assert.True(Verify(shallower,grid,.000001,.000002,.00000051).Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneGridNotchIsNotDeclaredFlat(bool reverse)
    {
        Point2[] source=[new(0,0),new(100,0),new(100,100),new(50.000002,100),
            new(50.000002,99.999999),new(50,99.999999),new(50,100),new(0,100)];
        if(reverse)Array.Reverse(source);
        var grid=Grid(source);var before=source.ToArray();
        Assert.True(Verify(source,grid,.000001,.000002,.000001).Ok);
        var flat=grid.ToArray();
        for(int i=0;i<flat.Length;i++)if(flat[i].Y==99_999_999)flat[i]=flat[i] with {Y=100_000_000};
        Assert.False(Verify(source,flat,.01,.01,.01).Ok);
        Assert.Equal(before,source);
    }

    [Fact]
    public void EmptyOrInvalidBudgetsAndWrongGridAreRejected()
    {
        Point2[] source=[new(0,0),new(100,0),new(100,100),new(0,100)];var grid=Grid(source);
        Assert.False(Verify(source,grid,0,.1,.1).Ok);
        Assert.False(Verify(source,grid,.1,double.NaN,.1).Ok);
        Assert.False(Verify(source,grid,.1,.1,double.PositiveInfinity).Ok);
        var wrong=grid.ToArray();wrong[0]=wrong[0] with {X=1};
        Assert.False(Verify(source,wrong,.1,.1,.1).Ok);
    }

    [Fact]
    public void ExactDyadicPointBudgetAcceptsEqualityAndRejectsOneUlpLess()
    {
        double displacement=Math.ScaleB(1.0,-21);
        Point2[] source=[new(displacement,0),new(100,0),new(100,100),new(0,100)];
        var grid=Grid(source);
        Assert.Equal(0,grid[0].X);
        Assert.True(Verify(source,grid,displacement,1,1).Ok);
        Assert.False(Verify(source,grid,Math.BitDecrement(displacement),1,1).Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorrectlyRoundedGridCannotFlattenSourceTurnEvenWithLargeBudgets(bool reverse)
    {
        Point2[] source=[new(0,0),new(100,.0000004),new(200,0),new(200,100),new(0,100)];
        if(reverse)Array.Reverse(source);
        var result=Verify(source,Grid(source),.1,.1,.1);
        Assert.False(result.Ok);Assert.Contains("flattened or reversed",result.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeasureExactPhysicalBudgetForOriginal512LobedSource(bool reverse)
    {
        var fixture=typeof(SmoothAllowanceRegressionTests).GetMethod("Radial",BindingFlags.NonPublic|BindingFlags.Static)!;
        var piece=(PatternPiece)fixture.Invoke(null,[512,true])!;
        if(reverse)piece.Points.Reverse();var source=piece.Points.ToArray();var before=source.ToArray();
        var timer=Stopwatch.StartNew();var result=Verify(source,Grid(source),.000001,.000003,.000003);
        output.WriteLine($"reverse={reverse}; vertices={source.Length}; accepted={result.Ok}; elapsed_ms={timer.Elapsed.TotalMilliseconds:F3}; reason={result.Reason}");
        Assert.True(result.Ok,result.Reason);Assert.Equal(before,source);
    }

    [Fact]
    public void BudgetAndCancellationRejectWithoutAnApproval()
    {
        Point2[] tooMany=Enumerable.Range(0,708).Select(i=>new Point2(i,(double)i*i)).ToArray();
        Assert.False(Verify(tooMany,Grid(tooMany),.1,.1,.1).Ok);
        var method=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetBinaryGridPhysicalBudget",true)!
            .GetMethod("TryVerify",BindingFlags.NonPublic|BindingFlags.Static)!;
        Point2[] triangle=[new(0,0),new(100,0),new(0,100)];
        using var c=new CancellationTokenSource();c.Cancel();
        var ex=Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,[triangle,Grid(triangle),.1,.1,.1,null,c.Token]));
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
    }
}
