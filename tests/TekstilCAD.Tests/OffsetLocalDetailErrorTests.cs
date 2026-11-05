using TekstilCAD.Core;
using TekstilCAD.Patterns;
using Clipper2Lib;

namespace TekstilCAD.Tests;

public sealed class OffsetLocalDetailErrorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnverifiedLocalDetailHasSpecificArgumentExceptionWithoutChangingSource(bool reverse)
    {
        var source = new PatternPiece
        {
            Name = "Loblu kaynak", Closed = true,
            Points = Enumerable.Range(0, 512).Select(i =>
            {
                double angle = Math.Tau * i / 512;
                double radius = 1000 + 200 * Math.Cos(3 * angle);
                return new Point2(radius * Math.Cos(angle), radius * Math.Sin(angle));
            }).ToList(),
            Grainline = new Grainline { Start = new(0, 0), End = new(0, 20), NeedsReview = false }
        };
        if (reverse) source.Points.Reverse();
        var before = new ProjectDocument { Pieces = [source.Clone()] };
        var current = new ProjectDocument { Pieces = [source] };

        var error = Assert.Throws<OffsetLocalDetailUnverifiedException>(() => PatternTools.Offset(source, 1));

        Assert.IsAssignableFrom<ArgumentException>(error);
        // This fixture fails in the inverse-to-source direction. A recovered
        // vertex is not a source vertex: never invent a source index for it.
        Assert.Equal(OffsetComparisonSide.Recovered,error.ComparisonSide);
        Assert.Null(error.SourceIndex);
        Assert.NotNull(error.ComparisonIndex);Assert.NotNull(error.Sample);Assert.NotNull(error.SamplePoint);
        const double scale=1_000_000;
        var grid=new Path64(source.Points.Select(p=>new Point64(
            (long)Math.Round(p.X*scale,MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y*scale,MidpointRounding.AwayFromZero))));
        var forward=Clipper.InflatePaths(new Paths64 { grid },scale,JoinType.Miter,EndType.Polygon,2);
        var recovered=Assert.Single(Clipper.InflatePaths(forward,-scale,JoinType.Miter,EndType.Polygon,2));
        var index=error.ComparisonIndex.Value;Assert.InRange(index,0,recovered.Count-1);
        var a=recovered[index];var b=recovered[(index+1)%recovered.Count];
        Assert.True(error.Sample is OffsetLocalSample.Vertex or OffsetLocalSample.EdgeMidpoint);
        double x=a.X,y=a.Y;
        if(error.Sample==OffsetLocalSample.EdgeMidpoint){x+=(b.X-a.X)/2d;y+=(b.Y-a.Y)/2d;}
        Assert.Equal(x/scale,error.SamplePoint.Value.X,12);
        Assert.Equal(y/scale,error.SamplePoint.Value.Y,12);
        // Independently verify that the reported point actually lies beyond
        // the unchanged eight-grid-unit tolerance from EVERY source segment.
        double closest=double.PositiveInfinity;
        for(int i=0;i<grid.Count;i++)
        {
            var c=grid[i];var d=grid[(i+1)%grid.Count];
            double dx=d.X-c.X,dy=d.Y-c.Y,px=x-c.X,py=y-c.Y;
            var length=dx*dx+dy*dy;
            var t=length==0 ? 0 : Math.Clamp((px*dx+py*dy)/length,0,1);
            closest=Math.Min(closest,Math.Sqrt(Math.Pow(px-t*dx,2)+Math.Pow(py-t*dy,2)));
        }
        Assert.True(closest>8,$"Reported recovered sample was only {closest} grid units from source.");
        Assert.True(DocumentSession.ContentEquals(before, current));
    }

    [Fact]
    public void MessageOnlyExceptionDoesNotInventAnyLocation()
    {
        var error=new OffsetLocalDetailUnverifiedException();
        Assert.Null(error.SourceIndex);Assert.Null(error.ComparisonIndex);Assert.Null(error.ComparisonSide);
        Assert.Null(error.Sample);Assert.Null(error.SamplePoint);Assert.Empty(error.Preview);
    }

    [Fact]
    public void InvalidAmountAndOpenContourAreNotMisreportedAsLocalDetailFailure()
    {
        var open = new PatternPiece { Closed = false, Points = [new(0, 0), new(10, 0), new(10, 10)] };
        var openError = Assert.Throws<ArgumentException>(() => PatternTools.Offset(open, 1));
        Assert.IsNotType<OffsetLocalDetailUnverifiedException>(openError);
        var amountError = Assert.Throws<ArgumentOutOfRangeException>(() => PatternTools.Offset(open, -1));
        Assert.IsNotType<OffsetLocalDetailUnverifiedException>(amountError);
    }
}
