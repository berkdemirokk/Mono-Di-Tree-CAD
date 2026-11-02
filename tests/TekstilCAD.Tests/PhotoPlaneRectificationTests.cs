using TekstilCAD.Core;

namespace TekstilCAD.Tests;

public class PhotoPlaneRectificationTests
{
    private static Point2 Project(double u,double v)
    {
        var denominator=1+.3*u+.2*v;
        return new((100+400*u+70*v)/denominator,(80+30*u+220*v)/denominator);
    }
    private static readonly Point2[] Corners=[Project(0,0),Project(1,0),Project(1,1),Project(0,1)];

    [Fact]
    public void ProjectiveRectangleRestoresCornersAndIndependentInteriorPoints()
    {
        var samples=new[] { (0d,0d),(1d,0d),(1d,1d),(0d,1d),(.15,.8),(.65,.35),(.2,.1) };
        var pixels=samples.Select(p=>Project(p.Item1,p.Item2)).ToArray();var before=pixels.ToArray();var originalCorners=Corners.ToArray();
        var actual=PhotoPlaneRectification.Convert(512,512,Corners,200,100,pixels);
        for(int i=0;i<samples.Length;i++) { Assert.Equal(samples[i].Item1*200,actual[i].X,7);Assert.Equal(samples[i].Item2*100,actual[i].Y,7); }
        Assert.Equal(before,pixels);Assert.Equal(originalCorners,Corners);
        Assert.Throws<NotSupportedException>(()=>((IList<Point2>)actual)[0]=new(1,1));
    }
    [Fact]
    public void IndependentDenseGridMatchesKnownForwardProjection()
    {
        var grid=(from y in Enumerable.Range(1,9) from x in Enumerable.Range(1,9) select (u:x/10d,v:y/10d)).ToArray();
        var actual=PhotoPlaneRectification.Convert(512,512,Corners,370,210,grid.Select(p=>Project(p.u,p.v)).ToArray());
        for(int i=0;i<grid.Length;i++) { Assert.InRange(Math.Abs(actual[i].X-grid[i].u*370),0,1e-8);Assert.InRange(Math.Abs(actual[i].Y-grid[i].v*210),0,1e-8); }
    }
    [Fact]
    public void ManyPerspectiveViewsRecoverIndependentlyProjectedPoints()
    {
        var random=new Random(11531);
        for(int i=0;i<200;i++)
        {
            double g=random.NextDouble()*.7-.35,h=random.NextDouble()*.7-.35;
            Point2 Forward(double u,double v) { var w=1+g*u+h*v;return new((200+250*u+30*v)/w,(120+20*u+180*v)/w); }
            Point2[] q=[Forward(0,0),Forward(1,0),Forward(1,1),Forward(0,1)];
            var sample=new[] { (u:.2,v:.3),(u:.8,v:.15),(u:.73,v:.89),(u:.1,v:.65) };
            var actual=PhotoPlaneRectification.Convert(2048,2048,q,200,350,sample.Select(p=>Forward(p.u,p.v)).ToArray());
            for(int j=0;j<sample.Length;j++) { Assert.InRange(Math.Abs(actual[j].X-200*sample[j].u),0,1e-7);Assert.InRange(Math.Abs(actual[j].Y-350*sample[j].v),0,1e-7); }
        }
    }
    [Fact]
    public void ReverseWindingIsAcceptedAndHasTheExplicitCorrespondingOrientation()
    {
        Point2[] q=[new(10,10),new(10,90),new(90,90),new(90,10)];
        var actual=PhotoPlaneRectification.Convert(100,100,q,160,80,[new(10,10),new(10,90),new(90,90),new(90,10)]);
        Assert.Equal(new Point2(0,0),actual[0]);Assert.Equal(160,actual[1].X,8);Assert.Equal(80,actual[2].Y,8);
        Assert.Equal(0,actual[3].X,8);Assert.Equal(80,actual[3].Y,8);
    }
    [Fact]
    public void FoldedConcaveDuplicateAndFlatReferencesAreRejected()
    {
        Point2[][] invalid=[ [new(10,10),new(90,90),new(90,10),new(10,90)],
            [new(10,10),new(90,10),new(30,30),new(10,90)], [new(10,10),new(10,10),new(90,90),new(10,90)],
            [new(10,10),new(30,10),new(60,10),new(90,10)] ];
        foreach(var q in invalid)Assert.ThrowsAny<ArgumentException>(()=>PhotoPlaneRectification.Convert(100,100,q,100,100,[new(20,20),new(30,30)]));
    }
    [Fact]
    public void OutsideReferenceOutOfImageAndCollapsedTraceAreRejected()
    {
        Point2[] q=[new(10,10),new(90,10),new(90,90),new(10,90)];
        Assert.Throws<ArgumentException>(()=>PhotoPlaneRectification.Convert(100,100,q,100,100,[new(20,20),new(9,20)]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoPlaneRectification.Convert(100,100,q,100,100,[new(20,20),new(100,20)]));
        Assert.Throws<ArgumentException>(()=>PhotoPlaneRectification.Convert(100,100,q,100,100,[new(20,20),new(20,20)]));
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(100000001)]
    public void InvalidDimensionsAreRejected(double size)
        =>Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoPlaneRectification.Convert(512,512,Corners,size,100,[Project(.2,.2),Project(.8,.8)]));
    [Fact]
    public void CountImageLimitsAndCancellationAreEnforced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoPlaneRectification.Convert(1,512,Corners,100,100,[Project(.2,.2),Project(.8,.8)]));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhotoPlaneRectification.Convert(512,512,Corners,100,100,[Project(.2,.2)]));
        Assert.ThrowsAny<OperationCanceledException>(()=>PhotoPlaneRectification.Convert(512,512,Corners,100,100,[Project(.2,.2),Project(.8,.8)],new CancellationToken(true)));
    }
}
