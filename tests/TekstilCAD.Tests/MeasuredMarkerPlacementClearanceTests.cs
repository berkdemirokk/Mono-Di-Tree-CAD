using TekstilCAD.Core;
using TekstilCAD.Core.Commands;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class MeasuredMarkerPlacementClearanceTests
{
    [Fact]
    public void ParsedMeasuredCommandAppliesRequestedGapAndMarginsToRealPlacements()
    {
        var front=Piece("Ön",2,20,30);
        var back=Piece("Arka",1,25,35);
        var source=new ProjectDocument { Pieces=[front,back] };
        var parsed=CommandParser.Parse(
            "150 cm kumaşa iki Ön, bir Arka, parça arası boşluk 5 mm ve kumaş kenar payı 10 mm yerleştir",
            new ParserContext([front.Name,back.Name]));
        var command=Assert.IsType<HandoffCommand>(parsed.Command);
        var request=Assert.IsType<MeasuredNestingRequest>(command.NestingRequest);

        var result=ProductionNestingPlan.Arrange(source,[front.Id,back.Id],request.FabricWidthMm,
            request.GapMm,double.PositiveInfinity,request.EdgeMarginMm);

        Assert.Equal(3,result.Placements.Count);
        Assert.Equal(request.GapMm,result.Nesting.Gap);
        Assert.Equal(request.EdgeMarginMm,result.Nesting.EdgeMargin);
        var boxes=result.Nesting.Layout.Pieces.Select(piece=>Geometry.Bounds(piece.Points)).ToArray();
        Assert.All(boxes,box=>
        {
            Assert.True(box.MinX>=request.EdgeMarginMm);
            Assert.True(box.MinY>=request.EdgeMarginMm);
            Assert.True(request.FabricWidthMm-box.MaxX>=request.EdgeMarginMm);
            Assert.True(result.Nesting.UsedLength-box.MaxY>=request.EdgeMarginMm);
        });
        for(var i=0;i<boxes.Length;i++)
        for(var j=i+1;j<boxes.Length;j++)
        {
            var a=boxes[i];var b=boxes[j];
            Assert.True(b.MinX-a.MaxX>=request.GapMm || a.MinX-b.MaxX>=request.GapMm ||
                b.MinY-a.MaxY>=request.GapMm || a.MinY-b.MaxY>=request.GapMm,
                $"Placed copies {i} and {j} do not preserve the requested gap.");
        }
        Assert.Equal(2,front.Metadata!.Quantity);
        Assert.Equal(1,back.Metadata!.Quantity);
        Assert.Equal(new Point2(50,50),front.Points[0]);
        Assert.Equal(new Point2(50,50),back.Points[0]);
    }

    private static PatternPiece Piece(string name,int quantity,double width,double height)=>new()
    {
        Name=name,
        Points=[new(50,50),new(50+width,50),new(50+width,50+height),new(50,50+height)],
        Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=quantity }
    };
}
