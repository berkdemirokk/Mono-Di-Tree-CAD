using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OffsetUnverifiedPreviewTests
{
    [Fact]
    public void UnverifiedContourIsImmutablePreviewAndSourceIsUnchanged()
    {
        var source=new PatternPiece { Name="Loblu kontur", Closed=true, Points=Enumerable.Range(0,512).Select(i=>{
            double a=Math.Tau*i/512,r=1000+200*Math.Cos(3*a);
            return new Point2(r*Math.Cos(a),r*Math.Sin(a));
        }).ToList() };
        var before=source.Points.ToArray();
        var failure=Assert.Throws<OffsetLocalDetailUnverifiedException>(()=>PatternTools.Offset(source,1));
        Assert.False(failure.Preview.IsDefaultOrEmpty);
        var captured=failure.Preview.ToArray();
        Assert.All(captured,p=>{ Assert.True(double.IsFinite(p.X)); Assert.True(double.IsFinite(p.Y)); });
        source.Points[0]=new Point2(0,0);
        Assert.Equal(captured,failure.Preview.ToArray());
        Assert.Equal(before.Skip(1),source.Points.Skip(1));
        Assert.True(typeof(OffsetLocalDetailUnverifiedException).GetProperty(nameof(failure.Preview))!.SetMethod is null);
    }

    [Fact]
    public void ClosedNarrowRecessRemainsRejected()
    {
        var source=new PatternPiece { Points=[new(0,0),new(100,0),new(100,100),new(52,100),
            new(52,20),new(48,20),new(48,100),new(0,100)] };
        var before=source.Points.ToArray();
        Assert.ThrowsAny<ArgumentException>(()=>PatternTools.Offset(source,5));
        Assert.Equal(before,source.Points);
    }
}
