using System.Collections;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfBoundedSelectionsTests
{
    private sealed class MisreportedCollection(int reported,int yielded,Action<int>? onYield=null) : IReadOnlyCollection<int>
    {
        public int Count => reported;
        public int Visited { get; private set; }
        public IEnumerator<int> GetEnumerator()
        {
            for(var i=0;i<yielded;i++) { Visited++;onYield?.Invoke(Visited);yield return i; }
        }
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }

    [Fact]
    public void MisreportedCountCannotCauseUnboundedEnumeration()
    {
        var source=new MisreportedCollection(1,1000000);
        Assert.Throws<InvalidDataException>(()=>DxfBoundedSelections.Copy(source,3,CancellationToken.None));
        Assert.Equal(4,source.Visited);
    }

    [Fact]
    public void CollectionThatShrinksDuringEnumerationIsRejected()
    {
        var source=new MisreportedCollection(2,1);
        Assert.Throws<InvalidDataException>(()=>DxfBoundedSelections.Copy(source,3,CancellationToken.None));
    }

    [Fact]
    public void CancellationInterruptsEnumeration()
    {
        using var cancellation=new CancellationTokenSource();
        var source=new MisreportedCollection(3,3,n=> { if(n==2)cancellation.Cancel(); });
        Assert.Throws<OperationCanceledException>(()=>DxfBoundedSelections.Copy(source,3,cancellation.Token));
        Assert.Equal(2,source.Visited);
    }
}
