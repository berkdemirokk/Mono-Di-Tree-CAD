using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class AutomaticPerCopyNestingTests
{
    private sealed class EditingPolicy : IReadOnlyDictionary<Guid, IReadOnlyList<int>>
    {
        private readonly Dictionary<Guid, IReadOnlyList<int>> data;
        private readonly Action edit;
        private bool edited;
        public EditingPolicy(Dictionary<Guid, IReadOnlyList<int>> data, Action edit) { this.data = data; this.edit = edit; }
        public int Count => data.Count;
        public IEnumerable<Guid> Keys => data.Keys;
        public IEnumerable<IReadOnlyList<int>> Values => data.Values;
        public IReadOnlyList<int> this[Guid id] => data[id];
        public bool ContainsKey(Guid id) => data.ContainsKey(id);
        public bool TryGetValue(Guid id, out IReadOnlyList<int> value)
        {
            if (!edited) { edited = true; edit(); }
            return data.TryGetValue(id, out value!);
        }
        public IEnumerator<KeyValuePair<Guid, IReadOnlyList<int>>> GetEnumerator() => data.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static PatternPiece Piece() => new()
    {
        Points = [new(0, 0), new(6, 0), new(6, 4), new(0, 4)],
        Grainline = new() { Start = new(1, 1), End = new(1, 2) },
        Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 3 }
    };

    [Fact]
    public void MixedAnglesFitAndPreserveSource()
    {
        var p = Piece(); var source = new ProjectDocument { Pieces = [p] };
        var allowed = new Dictionary<Guid, IReadOnlyList<int>> { [p.Id] = new[] { 0, 90 } };
        var result = AutomaticPerCopyNesting.Arrange(source, [p.Id], allowed, 10, 0, 10, 0, GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.Equal(10, result.Plan.Nesting.UsedLength);
        Assert.Contains(0, result.PlacedRotations.Values);
        Assert.Contains(90, result.PlacedRotations.Values);
        Assert.Equal(3, result.Plan.Placements.Count);
        var chosen = result.Plan.Placements.ToDictionary(p => (p.SourcePieceId, p.CopyNumber),
            p => result.PlacedRotations[p.PlacedPieceId]);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(source, [p.Id], result, chosen,
            GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 10));
        Assert.True(PerCopyTraceVerifier.IsValidForAllowedAngles(source, [p.Id], result, allowed,
            GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 10));
        Assert.Equal(new Point2(6, 0), p.Points[1]);
        Assert.Equal(3, p.Metadata!.Quantity);
    }

    [Fact]
    public void AnglePolicyCallbackCannotChangeTheCapturedSource()
    {
        var piece = Piece(); var source = new ProjectDocument { Pieces = [piece] }; var original = source.Clone();
        var values = new Dictionary<Guid, IReadOnlyList<int>> { [piece.Id] = [0, 90] };
        var policy = new EditingPolicy(values, () => piece.Points[1] = new Point2(7, 0));
        var result = AutomaticPerCopyNesting.Arrange(source, [piece.Id], policy, 10, 0, 10, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        var chosen = result.Plan.Placements.ToDictionary(p => (p.SourcePieceId, p.CopyNumber),
            p => result.PlacedRotations[p.PlacedPieceId]);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(original, [piece.Id], result, chosen,
            GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 10));
        Assert.False(PerCopyTraceVerifier.IsValidForConstraints(source, [piece.Id], result, chosen,
            GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 10));
    }

    [Fact]
    public void GrainFiltersAnglesAndMissingGrainReportsSource()
    {
        var p = Piece(); var source = new ProjectDocument { Pieces = [p] };
        var allowed = new Dictionary<Guid, IReadOnlyList<int>> { [p.Id] = new[] { 0, 90 } };
        var result = AutomaticPerCopyNesting.Arrange(source, [p.Id], allowed, 10, 0, 20, 0, GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.All(result.PlacedRotations.Values, angle => Assert.Equal(0, angle));
        p.Grainline = null;
        var error = Assert.Throws<ArgumentException>(() => AutomaticPerCopyNesting.Arrange(source, [p.Id], allowed, 10, 0, 20, 0, GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _));
        Assert.Equal(p.Id, id);
    }

    [Fact]
    public void InvalidAngleListAndCancellationAreRejected()
    {
        var p = Piece(); var source = new ProjectDocument { Pieces = [p] };
        var allowed = new Dictionary<Guid, IReadOnlyList<int>> { [p.Id] = new[] { 0, 45 } };
        var error = Assert.Throws<ArgumentException>(() => AutomaticPerCopyNesting.Arrange(source, [p.Id], allowed, 10, 0, 20, 0, GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _));
        Assert.Equal(p.Id, id);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AutomaticPerCopyNesting.Arrange(source, [p.Id], allowed, 10, 0, 20, 0, GrainDirectionPolicy.Direction.TwoWay, 90, cancellation.Token));
    }

    [Fact]
    public void ExtraUnselectedAnglePolicyIsNotSilentlyIgnored()
    {
        var piece = Piece(); var other = Piece();
        var source = new ProjectDocument { Pieces = [piece, other] };
        var allowed = new Dictionary<Guid, IReadOnlyList<int>> { [piece.Id] = [0], [other.Id] = [0] };
        var error = Assert.Throws<ArgumentException>(() => AutomaticPerCopyNesting.Arrange(source, [piece.Id], allowed,
            10, 0, 20, 0, GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.Equal("allowedAngles", error.ParamName);
        Assert.Equal(3, piece.Metadata!.Quantity);
    }

    [Fact]
    public void RetriesFirstAllowedAnglesWhenGreedyOrderExceedsLength()
    {
        PatternPiece Rectangle(int width, int height) => new()
        {
            Points = [new(0, 0), new(width, 0), new(width, height), new(0, height)],
            Grainline = new() { Start = new(0, 0), End = new(0, 1) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var a = Rectangle(6, 8); var b = Rectangle(4, 1);
        var c = Rectangle(6, 7); var d = Rectangle(4, 6);
        var source = new ProjectDocument { Pieces = [a, b, c, d] };
        var ids = source.Pieces.Select(p => p.Id).ToArray();
        var allowed = new Dictionary<Guid, IReadOnlyList<int>>
            { [a.Id] = [0], [b.Id] = [0], [c.Id] = [0, 90], [d.Id] = [0] };
        var result = AutomaticPerCopyNesting.Arrange(source, ids, allowed, 10, 0, 16, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.Equal(16, result.Plan.Nesting.UsedLength);
        var mapping = Assert.Single(result.Plan.Placements, p => p.SourcePieceId == c.Id);
        Assert.Equal(0, result.PlacedRotations[mapping.PlacedPieceId]);
        var chosen = result.Plan.Placements.ToDictionary(p => (p.SourcePieceId, p.CopyNumber),
            p => result.PlacedRotations[p.PlacedPieceId]);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(source, ids, result, chosen,
            GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 16));
    }

    [Fact]
    public void ChoosesShorterVerifiedAlternativeEvenWhenFirstLayoutFits()
    {
        PatternPiece Rectangle(int width, int height) => new()
        {
            Points = [new(0, 0), new(width, 0), new(width, height), new(0, height)],
            Grainline = new() { Start = new(0, 0), End = new(0, 1) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var a = Rectangle(6, 8); var b = Rectangle(4, 1);
        var c = Rectangle(6, 7); var d = Rectangle(4, 6);
        var source = new ProjectDocument { Pieces = [a, b, c, d] };
        var ids = source.Pieces.Select(p => p.Id).ToArray();
        var allowed = new Dictionary<Guid, IReadOnlyList<int>>
            { [a.Id] = [0], [b.Id] = [0], [c.Id] = [0, 90], [d.Id] = [0] };
        var result = AutomaticPerCopyNesting.Arrange(source, ids, allowed, 10, 0, 100, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.Equal(16, result.Plan.Nesting.UsedLength);
        Assert.True(PerCopyTraceVerifier.IsValidForAllowedAngles(source, ids, result, allowed,
            GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 100));
    }
    [Fact]
    public void KeepsVerifiedFirstLayoutWhenAlternativeLosesPrecision()
    {
        PatternPiece Rectangle(double width, double height) => new()
        {
            Points = [new(0, 0), new(width, 0), new(width, height), new(0, height)],
            Grainline = new() { Start = new(0, 0), End = new(0, 1) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var wide = Rectangle(90_000_000, 300);
        var narrow = Rectangle(0.0000011, 200);
        var source = new ProjectDocument { Pieces = [narrow, wide] };
        var ids = new[] { narrow.Id, wide.Id };
        var allowed = new Dictionary<Guid, IReadOnlyList<int>> { [wide.Id] = [0], [narrow.Id] = [0, 90] };
        var firstAngles = new Dictionary<(Guid, int), int> { [(wide.Id, 1)] = 0, [(narrow.Id, 1)] = 90 };
        var alternateAngles = new Dictionary<(Guid, int), int> { [(wide.Id, 1)] = 0, [(narrow.Id, 1)] = 0 };
        var error = Assert.Throws<ArgumentException>(() => PerCopyOrthogonalNesting.Arrange(source, ids,
            alternateAngles, 100_000_000, 0, 1000, 0, GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.True(NestingPieceError.TryGet(error, out var failedId, out _));
        Assert.Equal(narrow.Id, failedId);

        var result = AutomaticPerCopyNesting.Arrange(source, ids, allowed, 100_000_000, 0, 1000, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(source, ids, result, firstAngles,
            GrainDirectionPolicy.Direction.TwoWay, 90, 100_000_000, 0, 0, 1000));
    }
    [Fact]
    public void KeepsVerifiedFirstLayoutWhenAlternativeCannotPreserveEdgeMargin()
    {
        const double width = 38389624.38411131;
        const double margin = 1638038.7319391903;
        const double fabric = 41665701.847989686;
        PatternPiece Rectangle(double w, double h) => new()
        {
            Points = [new(0, 0), new(w, 0), new(w, h), new(0, h)],
            Grainline = new() { Start = new(0, 0), End = new(0, 100) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var small = Rectangle(100, 1000); var large = Rectangle(width, width - 500);
        var source = new ProjectDocument { Pieces = [small, large] };
        var ids = new[] { small.Id, large.Id };
        var allowed = new Dictionary<Guid, IReadOnlyList<int>> { [small.Id] = [0], [large.Id] = [0, 90] };
        var firstAngles = new Dictionary<(Guid, int), int> { [(small.Id, 1)] = 0, [(large.Id, 1)] = 90 };
        var alternateAngles = new Dictionary<(Guid, int), int> { [(small.Id, 1)] = 0, [(large.Id, 1)] = 0 };
        var first = PerCopyOrthogonalNesting.Arrange(source, ids, firstAngles, fabric, 0, 100_000_000, margin,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(source, ids, first, firstAngles,
            GrainDirectionPolicy.Direction.TwoWay, 90, fabric, 0, margin, 100_000_000));
        var error = Assert.Throws<InvalidOperationException>(() => PerCopyOrthogonalNesting.Arrange(source, ids,
            alternateAngles, fabric, 0, 100_000_000, margin, GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.Equal(true, error.Data["TekstilCAD.Nesting.ClearancePrecision"]);

        var result = AutomaticPerCopyNesting.Arrange(source, ids, allowed, fabric, 0, 100_000_000, margin,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(source, ids, result, firstAngles,
            GrainDirectionPolicy.Direction.TwoWay, 90, fabric, 0, margin, 100_000_000));
    }
    [Fact]
    public void RetriesAlternativeWhenGreedyPlacementLosesPiecePrecision()
    {
        PatternPiece Rectangle(double width, double height) => new()
        {
            Points = [new(0, 0), new(width, 0), new(width, height), new(0, height)],
            Grainline = new() { Start = new(0, 0), End = new(0, 1) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var wide = Rectangle(90_000_000, 300);
        var narrow = Rectangle(0.0001, 200);
        var source = new ProjectDocument { Pieces = [wide, narrow] };
        var ids = new[] { wide.Id, narrow.Id };
        var allowed = new Dictionary<Guid, IReadOnlyList<int>> { [wide.Id] = [0], [narrow.Id] = [90, 0] };
        var greedy = new Dictionary<(Guid, int), int> { [(wide.Id, 1)] = 0, [(narrow.Id, 1)] = 0 };
        var alternate = new Dictionary<(Guid, int), int> { [(wide.Id, 1)] = 0, [(narrow.Id, 1)] = 90 };
        const double fabric = 90_000_100;

        var error = Assert.Throws<ArgumentException>(() => PerCopyOrthogonalNesting.Arrange(source, ids,
            greedy, fabric, 0, 1000, 0, GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.True(NestingPieceError.TryGet(error, out var failedId, out _));
        Assert.Equal(narrow.Id, failedId);
        var verified = PerCopyOrthogonalNesting.Arrange(source, ids, alternate, fabric, 0, 1000, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(source, ids, verified, alternate,
            GrainDirectionPolicy.Direction.TwoWay, 90, fabric, 0, 0, 1000));

        var result = AutomaticPerCopyNesting.Arrange(source, ids, allowed, fabric, 0, 1000, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(source, ids, result, alternate,
            GrainDirectionPolicy.Direction.TwoWay, 90, fabric, 0, 0, 1000));
    }
    [Fact]
    public void ReportsWhenLengthRetryWouldExceedWorkBudget()
    {
        PatternPiece Rectangle(int width, int height, bool detailed)
        {
            var points = new List<Point2>();
            int divisions = detailed ? 500 : 1;
            for (int i = 0; i < divisions; i++) points.Add(new((double)width * i / divisions, 0));
            for (int i = 0; i < divisions; i++) points.Add(new(width, (double)height * i / divisions));
            for (int i = 0; i < divisions; i++) points.Add(new(width - (double)width * i / divisions, height));
            for (int i = 0; i < divisions; i++) points.Add(new(0, height - (double)height * i / divisions));
            return new PatternPiece { Points = points, Grainline = new() { Start = new(0, 0), End = new(0, 1) },
                Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 } };
        }
        var a = Rectangle(6, 8, true); var b = Rectangle(4, 1, true);
        var c = Rectangle(6, 7, false); var d = Rectangle(4, 6, false);
        var source = new ProjectDocument { Pieces = [a, b, c, d] };
        var ids = source.Pieces.Select(p => p.Id).ToArray();
        var allowed = new Dictionary<Guid, IReadOnlyList<int>>
            { [a.Id] = [0], [b.Id] = [0], [c.Id] = [0, 90], [d.Id] = [0] };
        var error = Assert.Throws<InvalidOperationException>(() => AutomaticPerCopyNesting.Arrange(source,
            ids, allowed, 10, 0, 16, 0, GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.Contains("alternatif açı düzeni", error.Message);
        Assert.NotNull(error.InnerException);
        Assert.Equal(2000, a.Points.Count);
        Assert.Equal(2000, b.Points.Count);
    }
}
