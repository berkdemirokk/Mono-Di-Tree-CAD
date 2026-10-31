using System.Collections;
using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class AutomaticPerCopyCancellationBoundaryTests
{
    [Fact]
    public void CancellationWhileReadingAnglesStopsBeforeReturningLayoutAndPreservesSource()
    {
        var piece = new PatternPiece
        {
            Points = [new(0, 0), new(6, 0), new(6, 4), new(0, 4)],
            Grainline = new() { Start = new(1, 1), End = new(1, 2) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 3 }
        };
        var source = new ProjectDocument { Pieces = [piece] };
        var originalPoints = piece.Points.ToArray();
        using var cancellation = new CancellationTokenSource();
        var angles = new CancellingAngles(cancellation);
        var allowed = new Dictionary<Guid, IReadOnlyList<int>> { [piece.Id] = angles };

        Assert.Throws<OperationCanceledException>(() => AutomaticPerCopyNesting.Arrange(source, [piece.Id],
            allowed, 10, 0, 20, 0, GrainDirectionPolicy.Direction.TwoWay, 90, cancellation.Token));

        Assert.Equal(2, angles.Reads);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(originalPoints, piece.Points);
        Assert.Equal(3, piece.Metadata!.Quantity);
        Assert.Same(piece, Assert.Single(source.Pieces));
    }

    private sealed class CancellingAngles(CancellationTokenSource cancellation) : IReadOnlyList<int>
    {
        public int Reads { get; private set; }
        public int Count => 2;
        public int this[int index]
        {
            get
            {
                Reads++;
                if (index == 1) cancellation.Cancel();
                return index == 0 ? 0 : 90;
            }
        }
        public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)new[] { 0, 90 }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
