using TekstilCAD.Core;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OpenPathProductionInformationTests
{
    [Fact]
    public void JoinRequiresExplicitOmissionAndPreservesBothSources()
    {
        var first=new PatternPiece { Name="Ön",Closed=false,Points=[new(0,0),new(10,0)],Metadata=new PieceMetadata { Size="M",Quantity=2 } };
        var second=new PatternPiece { Name="Yan",Closed=false,Points=[new(10,0),new(10,10)],Grainline=new Grainline { Start=new(8,0),End=new(8,5),NeedsReview=true } };
        var oldFirst=first.Clone();var oldSecond=second.Clone();
        Assert.Throws<ArgumentException>(()=>OpenPathJoin.Join(first,second,0));
        var result=OpenPathJoin.Join(first,second,0,omitSourceProductionData:true).Piece;
        Assert.Null(result.Metadata);Assert.Null(result.Grainline);
        Assert.Equal(oldFirst.Metadata!.Size,first.Metadata!.Size);Assert.Equal(oldFirst.Metadata.Quantity,first.Metadata.Quantity);
        Assert.Equal(oldSecond.Grainline!.Start,second.Grainline!.Start);Assert.Equal(oldSecond.Grainline.End,second.Grainline.End);
        Assert.True(second.Grainline.NeedsReview);Assert.Equal(oldFirst.Points,first.Points);Assert.Equal(oldSecond.Points,second.Points);
    }

    [Fact]
    public void ClosureRequiresExplicitOmissionAndPreservesSource()
    {
        var source=new PatternPiece { Name="Ön",Closed=false,Points=[new(0,0),new(10,0),new(10,10)],
            Metadata=new PieceMetadata { Role="Ön",Material="Kumaş" },Grainline=new Grainline { Start=new(2,1),End=new(2,8) } };
        var original=source.Clone();
        Assert.Throws<ArgumentException>(()=>OpenPathClosure.Close(source,15));
        var result=OpenPathClosure.Close(source,15,omitSourceProductionData:true).Piece;
        Assert.True(result.Closed);Assert.Null(result.Metadata);Assert.Null(result.Grainline);
        Assert.Equal(original.Metadata!.Material,source.Metadata!.Material);Assert.Equal(original.Grainline!.End,source.Grainline!.End);
        Assert.Equal(original.Points,source.Points);Assert.False(source.Closed);
    }
}
