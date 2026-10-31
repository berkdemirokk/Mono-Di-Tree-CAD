using System.Collections;
using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class ProductionNestingCancellationTests
{
    private static PatternPiece Piece(string name,int quantity=2) => new()
    {
        Name=name,Closed=true,Points=[new(0,0),new(10,0),new(10,20),new(0,20)],
        Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=quantity }
    };

    [Fact]
    public void PreCancelledProductionAndEngineReturnNoResultAndLeaveSourceUntouched()
    {
        var a=Piece("Ön");var source=new ProjectDocument { Pieces=[a] };var before=source.Clone();
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>ProductionNestingPlan.Arrange(source,[a.Id],100,5,double.PositiveInfinity,0,cancellation.Token));
        Assert.Throws<OperationCanceledException>(()=>NestingEngine.Arrange([a],100,5,double.PositiveInfinity,0,cancellation.Token));
        Assert.True(DocumentSession.ContentEquals(before,source));
    }

    [Fact]
    public void CancellationDuringProductionSelectionDoesNotMutateSource()
    {
        var a=Piece("Ön");var b=Piece("Arka");var source=new ProjectDocument { Pieces=[a,b] };var before=source.Clone();
        using var cancellation=new CancellationTokenSource();
        var selection=new CancellingCollection<Guid>([a.Id,b.Id],cancellation);
        Assert.Throws<OperationCanceledException>(()=>ProductionNestingPlan.Arrange(source,selection,100,5,double.PositiveInfinity,0,cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(DocumentSession.ContentEquals(before,source));
    }

    [Fact]
    public void CancellationDuringEnginePreflightDoesNotMutatePieces()
    {
        var a=Piece("Ön");var b=Piece("Arka");var source=new ProjectDocument { Pieces=[a,b] };var before=source.Clone();
        using var cancellation=new CancellationTokenSource();
        var input=new CancellingList<PatternPiece>([a,b],cancellation);
        Assert.Throws<OperationCanceledException>(()=>NestingEngine.Arrange(input,100,5,double.PositiveInfinity,0,cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(DocumentSession.ContentEquals(before,source));
    }

    [Fact]
    public void UncancelledProductionMatchesExistingApiAndPreservesSource()
    {
        var a=Piece("Ön",3);var source=new ProjectDocument { Pieces=[a] };var before=source.Clone();
        var existing=ProductionNestingPlan.Arrange(source,[a.Id],100,5);
        var result=ProductionNestingPlan.Arrange(source,[a.Id],100,5,double.PositiveInfinity,0,CancellationToken.None);
        Assert.Equal(existing.Placements,result.Placements);
        Assert.Equal(existing.Nesting.UsedLength,result.Nesting.UsedLength);
        Assert.True(DocumentSession.ContentEquals(existing.Nesting.Layout,result.Nesting.Layout));
        Assert.True(DocumentSession.ContentEquals(before,source));
    }

    [Fact]
    public void FingerprintSupportsCancellationAndPreservesExistingHash()
    {
        var project=new ProjectDocument { Pieces=[new PatternPiece { Name="Ön",Points=[new(0,0),new(10,0),new(0,20)] }] };
        var before=project.Clone();
        var expected=NestingSnapshot.ComputeLayoutFingerprint(project);
        Assert.Equal(expected,NestingSnapshot.ComputeLayoutFingerprint(project,CancellationToken.None));
        using var source=new CancellationTokenSource();source.Cancel();
        Assert.Throws<OperationCanceledException>(()=>NestingSnapshot.ComputeLayoutFingerprint(project,source.Token));
        Assert.True(DocumentSession.ContentEquals(before,project));
    }
    private sealed class CancellingCollection<T>(IReadOnlyList<T> values,CancellationTokenSource cancellation) : IReadOnlyCollection<T>
    {
        public int Count=>values.Count;
        public IEnumerator<T> GetEnumerator()
        {
            for(var i=0;i<values.Count;i++)
            {
                if(i==1)cancellation.Cancel();
                yield return values[i];
            }
        }
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }

    private sealed class CancellingList<T>(IReadOnlyList<T> values,CancellationTokenSource cancellation) : IReadOnlyList<T>
    {
        public int Count=>values.Count;
        public T this[int index]=>values[index];
        public IEnumerator<T> GetEnumerator()
        {
            for(var i=0;i<values.Count;i++)
            {
                if(i==1)cancellation.Cancel();
                yield return values[i];
            }
        }
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
