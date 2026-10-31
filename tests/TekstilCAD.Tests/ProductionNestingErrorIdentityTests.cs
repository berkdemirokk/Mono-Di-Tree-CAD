using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class ProductionNestingErrorIdentityTests
{
    private static PatternPiece Piece(string name,double width,string role="Kesim",int quantity=2) => new()
    {
        Name=name, Points=[new(0,0),new(width,0),new(width,20),new(0,20)],
        Metadata=new PieceMetadata { Role=role,Quantity=quantity,Material="Pamuk" }
    };

    [Fact]
    public void InvalidRoleIdentifiesTheCorrectSourceEvenWhenNamesAreIdenticalAndContainControls()
    {
        var first=Piece("Ön\u202E\n",10);var second=Piece("Ön\u202E\n",10,"Yardımcı");
        var project=new ProjectDocument { Pieces=[first,second] };var before=project.Clone();
        var error=Assert.Throws<ArgumentException>(()=>ProductionNestingPlan.Arrange(project,[first.Id,second.Id],100));
        Assert.True(NestingPieceError.TryGet(error,out var pieceId,out var reason));
        Assert.IsAssignableFrom<ArgumentException>(error);
        Assert.Equal(second.Id,pieceId);
        Assert.DoesNotContain('\u202E',error.Message);Assert.DoesNotContain('\n',reason);
        Assert.True(DocumentSession.ContentEquals(before,project));
        Assert.Equal("Ön\u202E\n",second.Name);
    }

    [Fact]
    public void TooWideExpandedCopyReportsItsOriginalSourceIdentity()
    {
        var narrow=Piece("Aynı\u202E",10);var wide=Piece("Aynı\u202E",90,quantity:3);
        var project=new ProjectDocument { Pieces=[narrow,wide] };var before=project.Clone();
        var error=Assert.Throws<ArgumentException>(()=>ProductionNestingPlan.Arrange(project,[narrow.Id,wide.Id],50));
        Assert.True(NestingPieceError.TryGet(error,out var pieceId,out _));
        Assert.Equal(wide.Id,pieceId);
        Assert.NotEqual(narrow.Id,pieceId);
        Assert.DoesNotContain('\u202E',error.Message);
        Assert.True(DocumentSession.ContentEquals(before,project));
        Assert.Equal(3,wide.Metadata!.Quantity);
    }

    [Fact]
    public void DirectNestingFailureReportsTheActualMotorPieceIdentity()
    {
        var first=Piece("Aynı",10);var second=Piece("Aynı",90);
        var before=new ProjectDocument { Pieces=[first,second] }.Clone();
        var error=Assert.Throws<ArgumentException>(()=>NestingEngine.Arrange([first,second],50));
        Assert.True(NestingPieceError.TryGet(error,out var pieceId,out _));
        Assert.Equal(second.Id,pieceId);
        Assert.True(DocumentSession.ContentEquals(before,new ProjectDocument { Pieces=[first,second] }));
    }
}
