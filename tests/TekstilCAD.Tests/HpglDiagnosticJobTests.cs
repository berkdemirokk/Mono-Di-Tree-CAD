using System.Collections;
using TekstilCAD.FileFormats;
using P = TekstilCAD.FileFormats.HpglIntersectionDiagnostic.Point;
using PlotPath = TekstilCAD.FileFormats.HpglIntersectionDiagnostic.Path;

namespace TekstilCAD.Tests;

public sealed class HpglDiagnosticJobTests
{
    private static P[] Square(int offset) => [new(offset,0),new(offset+10,0),new(offset+10,10),new(offset,10)];

    [Fact]
    public void AggregatePairBudgetAppliesAcrossPathsAndLeavesInputsUntouched()
    {
        var a=Square(0);var b=Square(30);var beforeA=a.ToArray();var beforeB=b.ToArray();
        PlotPath[] job=[new(a,true),new(b,true)];
        // Four nonzero edges per square: 6 + 6 pair comparisons across the job.
        Assert.Throws<InvalidDataException>(()=>HpglIntersectionDiagnostic.ValidateJob(job,maxPairs:11));
        var result=HpglIntersectionDiagnostic.ValidateJob(job,maxPairs:12);
        Assert.Equal(12,result.PairCount);
        Assert.Equal(beforeA,a);Assert.Equal(beforeB,b);
    }

    [Fact]
    public void SeparatePathsMayOverlapBecauseCrossPathIntersectionIsNotChecked()
    {
        var a=Square(0);var b=Square(0); PlotPath[] job=[new(a,true),new(b,true)];
        var result=HpglIntersectionDiagnostic.ValidateJob(job,maxPairs:12);
        Assert.Equal(12,result.PairCount);
        Assert.Equal(a,b);
    }

    [Fact]
    public void InvalidPathStopsJobAndPreservesEveryInput()
    {
        var valid=Square(0);P[] crossing=[new(0,0),new(20,20),new(0,20),new(20,0)];
        var a=valid.ToArray();var b=crossing.ToArray();PlotPath[] job=[new(valid,true),new(crossing,true)];
        Assert.Throws<InvalidDataException>(()=>HpglIntersectionDiagnostic.ValidateJob(job));
        Assert.Equal(a,valid);Assert.Equal(b,crossing);
    }

    [Fact]
    public void PrecancelAndCancellationDuringSnapshotStopWithoutMutatingInputs()
    {
        var source=Enumerable.Range(0,2048).Select(i=>new P(i,0)).ToArray(); var before=source.ToArray();
        using var cts=new CancellationTokenSource();
        PlotPath[] job=[new(new CancellingPoints(source,cts),false)];
        Assert.ThrowsAny<OperationCanceledException>(()=>HpglIntersectionDiagnostic.ValidateJob(job,cancellationToken:cts.Token));
        Assert.True(cts.IsCancellationRequested);Assert.Equal(before,source);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>HpglIntersectionDiagnostic.ValidateJob([new(Square(0),true)],cancellationToken:cancelled.Token));
    }

    [Fact]
    public void EmptyJobAndInvalidBudgetAreExplicit()
    {
        Assert.Throws<InvalidDataException>(()=>HpglIntersectionDiagnostic.ValidateJob(Array.Empty<PlotPath>()));
        Assert.Throws<ArgumentOutOfRangeException>(()=>HpglIntersectionDiagnostic.ValidateJob(Array.Empty<PlotPath>(),maxPairs:-1));
    }

    private sealed class CancellingPoints(P[] source,CancellationTokenSource cts) : IReadOnlyList<P>
    {
        public int Count => source.Length;
        public P this[int index] { get { if(index==0)cts.Cancel();return source[index]; } }
        public IEnumerator<P> GetEnumerator() => ((IEnumerable<P>)source).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

