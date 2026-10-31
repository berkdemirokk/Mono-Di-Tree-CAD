using System.Security.Cryptography;
using System.Text;
using TekstilCAD.Core;
using TekstilCAD.Nesting;
namespace TekstilCAD.Tests;
public class ProductionNestingIdentityTests
{
    private static PatternPiece Piece() => new() { Points=[new(0,0),new(10,0),new(10,10),new(0,10)], Metadata=new() { Role="Kesim", Quantity=1, Material="Pamuk" } };
    [Fact] public void CopyIdCollisionWithUnselectedSourceIsRejected()
    {
        var selected=Piece();
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes($"TekstilCAD.production-copy.v1:{selected.Id:D}:1"));
        var other=Piece(); other.Id=new Guid(hash.AsSpan(0,16));
        var source=new ProjectDocument { Pieces=[selected,other] };
        Assert.Throws<InvalidOperationException>(()=>ProductionNestingPlan.Arrange(source,[selected.Id],100));
        Assert.Equal(2,source.Pieces.Count); Assert.Equal(1,selected.Metadata!.Quantity);
    }
    [Theory]
    [InlineData(" Pamuk", null)]
    [InlineData("Pamuk\n", null)]
    [InlineData("Pamuk", " ")]
    public void InvalidMetadataIsRejected(string material,string? size)
    {
        var piece=Piece(); piece.Metadata!.Material=material; piece.Metadata.Size=size;
        Assert.Throws<ArgumentException>(()=>ProductionNestingPlan.Arrange(new ProjectDocument { Pieces=[piece] },[piece.Id],100));
    }
    [Fact] public void OversizedMaterialIsRejected()
    {
        var piece=Piece(); piece.Metadata!.Material=new string('x',129);
        Assert.Throws<ArgumentException>(()=>ProductionNestingPlan.Arrange(new ProjectDocument { Pieces=[piece] },[piece.Id],100));
    }
}
