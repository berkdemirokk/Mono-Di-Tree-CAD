using System.Collections;
using TekstilCAD.Core;
using TekstilCAD.Grading;

namespace TekstilCAD.Tests;

public sealed class BulkGradeEngineBoundaryTests
{
    private static PatternPiece Square() => new()
    {
        Name = "Temel", Closed = true,
        Points = [new(0,0), new(100,0), new(100,100), new(0,100)],
        Metadata = new PieceMetadata { Role = "Kesim", Size = "M", Material = "Pamuk", Quantity = 7 }
    };

    [Fact]
    public void InvalidLaterSizeRejectsWholeBatchAndPreservesSourceAndRules()
    {
        var source = Square();
        var before = source.Clone();
        Point2[] increments = [new(0,0), new(-100,0), new(-100,-100), new(0,-100)];
        var originalIncrements = increments.ToArray();
        IReadOnlyList<PatternPiece>? results = null;

        // The first size is valid; the second collapses all four vertices.
        Assert.Throws<InvalidDataException>(() => results = GradingEngine.GenerateSizes(source, increments,
            [new GradeSize("M",0), new GradeSize("Bozuk",1)]));
        Assert.Null(results);
        Assert.Equal(before.Id, source.Id);
        Assert.Equal(before.Name, source.Name);
        Assert.Equal(before.Closed, source.Closed);
        Assert.Equal(before.Points, source.Points);
        Assert.Equal(before.Metadata!.Quantity, source.Metadata!.Quantity);
        Assert.Equal(before.Metadata.Material, source.Metadata.Material);
        Assert.Equal(originalIncrements, increments);
    }

    [Fact]
    public void CancellationAfterFirstSizeDoesNotReturnPartialBatchOrMutateSource()
    {
        var source = Square();
        var before = source.Clone();
        Point2[] increments = [new(-1,-1), new(1,-1), new(1,1), new(-1,1)];
        using var cancellation = new CancellationTokenSource();
        var sizes = new CancelSecondSizeOnGeneration(cancellation);
        IReadOnlyList<PatternPiece>? results = null;

        Assert.Throws<OperationCanceledException>(() => results = GradingEngine.GenerateSizes(source, increments, sizes, cancellation.Token));
        Assert.True(sizes.FirstGenerationCompleted);
        Assert.Null(results);
        Assert.Equal(before.Id, source.Id);
        Assert.Equal(before.Name, source.Name);
        Assert.Equal(before.Points, source.Points);
        Assert.Equal(before.Metadata!.Quantity, source.Metadata!.Quantity);
    }

    [Fact]
    public void GeneratedSizeMutationCannotChangeSiblingOrSource()
    {
        var source = Square();
        var before = source.Points.ToArray();
        Point2[] increments = [new(-1,-1), new(1,-1), new(1,1), new(-1,1)];
        var result = GradingEngine.GenerateSizes(source, increments, [new("S",-1), new("L",1)]);
        var secondBefore = result[1].Points.ToArray();

        result[0].Points[0] = new(999,999);
        Assert.Equal(before, source.Points);
        Assert.Equal(secondBefore, result[1].Points);
        Assert.NotEqual(result[0].Id, result[1].Id);
    }

    [Fact]
    public void ExcessSizeCountRejectedBeforeEnumeratingSizes()
    {
        var source = new PatternPiece { Closed=false, Points=[new(0,0),new(10,0)] };
        var sizes = new UnreadableOversizedSizes();
        Assert.Throws<ArgumentException>(() => GradingEngine.GenerateSizes(source, [new(0,0),new(0,0)], sizes));
        Assert.False(sizes.Enumerated);
    }

    private sealed class CancelSecondSizeOnGeneration(CancellationTokenSource cancellation) : IReadOnlyList<GradeSize>
    {
        private readonly GradeSize[] values = [new("S",-1), new("L",1)];
        private int passes;
        public bool FirstGenerationCompleted { get; private set; }
        public int Count => values.Length;
        public GradeSize this[int index] => values[index];
        public IEnumerator<GradeSize> GetEnumerator()
        {
            var currentPass = ++passes;
            for (var i=0; i<values.Length; i++)
            {
                if (currentPass == 2 && i == 1)
                {
                    FirstGenerationCompleted = true;
                    cancellation.Cancel();
                }
                yield return values[i];
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class UnreadableOversizedSizes : IReadOnlyList<GradeSize>
    {
        public bool Enumerated { get; private set; }
        public int Count => 1001;
        public GradeSize this[int index] => throw new InvalidOperationException("Oversized input must not be read.");
        public IEnumerator<GradeSize> GetEnumerator()
        {
            Enumerated = true;
            throw new InvalidOperationException("Oversized input must not be enumerated.");
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
