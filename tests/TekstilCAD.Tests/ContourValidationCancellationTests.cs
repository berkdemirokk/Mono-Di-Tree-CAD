using System.Collections;
using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public sealed class ContourValidationCancellationTests
{
    [Fact]
    public void CancelledValidationDoesNotMutateContour()
    {
        List<Point2> points=[new(0,0),new(10,0),new(10,10),new(0,10)];
        var before=points.ToArray();
        using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>Geometry.ValidateContour(points,cancellation.Token));
        Assert.Equal(before,points);
    }

    [Fact]
    public void CancelDuringIntersectionScanIsObserved()
    {
        var points=Enumerable.Range(0,128).Select(i=>
        {
            double angle=Math.Tau*i/128;
            return new Point2(1000*Math.Cos(angle),1000*Math.Sin(angle));
        }).ToArray();
        using var cancellation=new CancellationTokenSource();
        var probe=new CancelOnIndexAccess(points,cancellation,5000);
        Assert.Throws<OperationCanceledException>(()=>Geometry.ValidateContour(probe,cancellation.Token));
        Assert.True(probe.Accesses>=5000);
        Assert.Equal(128,points.Length);
    }

    [Fact]
    public void UncancelledOverloadKeepsValidAndInvalidDecisions()
    {
        List<Point2> valid=[new(0,0),new(10,0),new(10,10),new(0,10),new(0,0)];
        Geometry.ValidateContour(valid);
        Geometry.ValidateContour(valid,CancellationToken.None);
        List<Point2> invalid=[new(0,0),new(4,4),new(0,3),new(5,0)];
        var original=Assert.Throws<ArgumentException>(()=>Geometry.ValidateContour(invalid));
        var overloaded=Assert.Throws<ArgumentException>(()=>Geometry.ValidateContour(invalid,CancellationToken.None));
        Assert.Equal(original.Message,overloaded.Message);
    }

    private sealed class CancelOnIndexAccess(Point2[] points,CancellationTokenSource cancellation,int threshold) : IReadOnlyList<Point2>
    {
        public int Accesses { get; private set; }
        public int Count=>points.Length;
        public Point2 this[int index]
        {
            get { if(++Accesses==threshold)cancellation.Cancel(); return points[index]; }
        }
        public IEnumerator<Point2> GetEnumerator()
        { for(var i=0;i<points.Length;i++)yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
