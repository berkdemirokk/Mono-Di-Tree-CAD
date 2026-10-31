using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class ProductionExpansionRefactorTests
{
    private sealed class MisreportedSelection : IReadOnlyCollection<Guid>
    {
        private readonly Guid first;
        public MisreportedSelection(Guid first) => this.first = first;
        public int Count => 1;
        public int Produced { get; private set; }
        public IEnumerator<Guid> GetEnumerator()
        {
            Produced++; yield return first;
            for (int i = 0; i < 10_000; i++) { Produced++; yield return Guid.NewGuid(); }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void SelectionEnumerationIsBoundedByDeclaredCount()
    {
        var piece = Piece(); var selected = new MisreportedSelection(piece.Id);
        Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(new() { Pieces = [piece] }, selected, 100));
        Assert.Equal(2, selected.Produced);
    }

    private static PatternPiece Piece(double width = 10, int quantity = 2) => new()
    {
        Points = [new(0, 0), new(width, 0), new(width, 20), new(0, 20)],
        Metadata = new() { Role = "Kesim", Quantity = quantity, Material = "Pamuk" }
    };

    [Fact]
    public void ExpansionPreservesSourceOrderIdentityAndDocumentAcrossCalls()
    {
        var first = Piece(); var second = Piece(quantity: 1);
        var source = new ProjectDocument { Pieces = [first, second] }; var before = source.Clone();
        var a = ProductionNestingPlan.Arrange(source, [second.Id, first.Id], 100);
        var b = ProductionNestingPlan.Arrange(source, [first.Id, second.Id], 100);
        Assert.Equal(a.Placements, b.Placements);
        Assert.Equal(new[] { first.Id, first.Id, second.Id }, a.Placements.Select(p => p.SourcePieceId));
        Assert.Equal(new[] { 1, 2, 1 }, a.Placements.Select(p => p.CopyNumber));
        Assert.Equal(3, a.Placements.Select(p => p.PlacedPieceId).Distinct().Count());
        Assert.All(a.Nesting.Layout.Pieces, p => Assert.Equal(1, p.Metadata!.Quantity));
        Assert.True(DocumentSession.ContentEquals(before, source));
    }

    [Fact]
    public void SelectionAndCancellationErrorsPrecedeEngineAndNullValidation()
    {
        var piece = Piece(); var source = new ProjectDocument { Pieces = [piece] };
        var error = Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(source, [piece.Id, piece.Id], 0));
        Assert.Contains("seçimi", error.Message);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProductionNestingPlan.Arrange(null!, null!, 0, 5, double.PositiveInfinity, 0, cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => ProductionNestingPlan.Arrange(null!, null!, 100));
    }

    [Fact]
    public void EngineErrorUsesSourceIdentityAndLeavesSourceUntouched()
    {
        var piece = Piece(width: 90, quantity: 3);
        var source = new ProjectDocument { Pieces = [piece] }; var before = source.Clone();
        var error = Assert.Throws<ArgumentException>(() => ProductionNestingPlan.Arrange(source, [piece.Id], 50));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _));
        Assert.Equal(piece.Id, id);
        Assert.True(DocumentSession.ContentEquals(before, source));
    }
}
