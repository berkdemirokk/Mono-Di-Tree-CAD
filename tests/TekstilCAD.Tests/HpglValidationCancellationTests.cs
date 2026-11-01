using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class HpglValidationCancellationTests
{
    [Fact]
    public void CancellationAtFirstValidationWinsOverInvalidSource()
    {
        var source=new ProjectDocument { Name=null! };
        using var cancellation=new CancellationTokenSource();
        var phase=0;
        Assert.Throws<OperationCanceledException>(()=>HpglFormat.PrepareForVerification(source,40,cancellation.Token,(stage,_)=>
        {
            phase=stage;if(stage==1)cancellation.Cancel();
        }));
        Assert.Equal(1,phase);
    }

    [Fact]
    public void CancellationAtDetachedValidationWinsAndSourceRemainsUnchanged()
    {
        var source=new ProjectDocument { Name="Kaynak",Pieces=[new PatternPiece { Points=[new(0,0),new(10,0),new(0,10)] }] };
        var before=source.Clone();using var cancellation=new CancellationTokenSource();var phase=0;
        Assert.Throws<OperationCanceledException>(()=>HpglFormat.PrepareForVerification(source,40,cancellation.Token,(stage,document)=>
        {
            phase=stage;
            if(stage==2) { document.Name=null!;cancellation.Cancel(); }
        }));
        Assert.Equal(2,phase);Assert.True(DocumentSession.ContentEquals(before,source));
    }
}
