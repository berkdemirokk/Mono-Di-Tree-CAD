using System.Collections;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfApprovedLayerBoundsTests
{
    private sealed class UnreliableLayers(int reported,int yielded) : IReadOnlyCollection<string>
    {
        public int Count=>reported;
        public int Visited { get; private set; }
        public IEnumerator<string> GetEnumerator()
        {
            for(var i=0;i<yielded;i++) { Visited++;yield return "L"+i; }
        }
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InconsistentApprovalCollectionIsRejectedBeforeReadingSource(bool verified)
    {
        var layers=new UnreliableLayers(1,2);
        using var input=new MemoryStream(new byte[] {1,2,3});
        Assert.Throws<InvalidDataException>(()=>
        {
            if(verified)DxfFormat.ImportWithBlockPlacementsVerified(input,"test",1,DxfTextEncoding.Utf8,0,layers,new string('0',64),3);
            else DxfFormat.ImportWithBlockPlacements(input,"test",1,DxfTextEncoding.Utf8,0,layers);
        });
        Assert.Equal(0,input.Position);
    }

    [Fact]
    public void DishonestEnumeratorCannotExceedPieceSelectionBound()
    {
        var layers=new UnreliableLayers(1,1000000);
        using var input=new MemoryStream();
        Assert.Throws<InvalidDataException>(()=>DxfFormat.ImportWithBlockPlacements(input,"test",1,DxfTextEncoding.Utf8,0,layers));
        Assert.Equal(ProjectStorage.MaximumPieces+1,layers.Visited);
        Assert.Equal(0,input.Position);
    }
}
