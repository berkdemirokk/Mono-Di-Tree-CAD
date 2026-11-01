using System.Reflection;
using TekstilCAD.Core;
using TekstilCAD.Patterns;
namespace TekstilCAD.Tests;

public class OffsetSourceGridTurnGuardTests
{
    private static (long X,long Y)[] Grid(IReadOnlyList<Point2> source)=>source.Select(p=>(
        (long)Math.Round(p.X*1_000_000,MidpointRounding.AwayFromZero),
        (long)Math.Round(p.Y*1_000_000,MidpointRounding.AwayFromZero))).ToArray();
    private static bool Guard(Point2[] source,(long X,long Y)[] grid,CancellationToken token=default)
    {
        var type=typeof(PatternTools).Assembly.GetType("TekstilCAD.Patterns.OffsetSourceGridTurnGuard",true)!;
        var method=type.GetMethod("PreservesTurns",BindingFlags.Static|BindingFlags.NonPublic)!;
        try { return (bool)method.Invoke(null,[source,grid,token])!; }
        catch(TargetInvocationException ex) when(ex.InnerException is OperationCanceledException) { throw ex.InnerException; }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffsetRejectsLongEdgedSourceNotchErasedByGrid(bool reverse)
    {
        var piece=new PatternPiece { Closed=true,Points=[new(0,0),new(100,0.0000004),new(200,0),new(200,100),new(0,100)] };
        if(reverse)piece.Points.Reverse();var before=piece.Points.ToArray();
        var ex=Assert.Throws<ArgumentException>(()=>PatternTools.Offset(piece,0.000005));
        Assert.Contains("Izgara yuvarlaması",ex.Message);Assert.Equal(before,piece.Points);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedShallowTurnIsNotRejectedByThisGuard(bool reverse)
    {
        Point2[] source=[new(0,0),new(100,0.00001),new(200,0),new(200,100),new(0,100)];
        if(reverse)Array.Reverse(source);
        Assert.True(Guard(source,Grid(source)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseLobesPassTurnGuardDespiteExistingInverseFailure(bool reverse)
    {
        var source=Enumerable.Range(0,512).Select(i=>{
            double a=2*Math.PI*i/512,r=1000+200*Math.Cos(3*a);return new Point2(r*Math.Cos(a),r*Math.Sin(a));
        }).ToArray();if(reverse)Array.Reverse(source);
        Assert.True(Guard(source,Grid(source)));
    }
    [Fact]
    public void SourceCollinearityChangedByGridIsRejected()
    {
        var b=new Point2(0.0000004,0.0000008);
        Point2[] source=[new(0,0),b,new(b.X*2,b.Y*2),new(1,1),new(0,1)];
        Assert.False(Guard(source,Grid(source)));
    }
    [Fact]
    public void RectangleOffsetStillSucceeds()
    {
        var piece=new PatternPiece { Closed=true,Points=[new(0,0),new(100,0),new(100,100),new(0,100)] };
        var result=PatternTools.Offset(piece,1);
        Assert.True(Math.Abs(Geometry.SignedArea(result.Points))>Math.Abs(Geometry.SignedArea(piece.Points)));
    }
    [Fact]
    public void GuardHonorsCancellation()
    {
        Point2[] source=[new(0,0),new(100,0),new(0,100)];
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>Guard(source,Grid(source),cancelled.Token));
    }
}
