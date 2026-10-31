using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class PerCopyOrthogonalNestingTests
{
    private sealed class EditingAngles : IReadOnlyDictionary<(Guid, int), int>
    {
        private readonly Dictionary<(Guid, int), int> values;
        private readonly Action edit;
        private bool edited;
        public EditingAngles(Dictionary<(Guid, int), int> values, Action edit) { this.values = values; this.edit = edit; }
        public int Count => values.Count;
        public IEnumerable<(Guid, int)> Keys => values.Keys;
        public IEnumerable<int> Values => values.Values;
        public int this[(Guid, int) key] => values[key];
        public bool ContainsKey((Guid, int) key) => values.ContainsKey(key);
        public bool TryGetValue((Guid, int) key, out int value)
        {
            if (!edited) { edited = true; edit(); }
            return values.TryGetValue(key, out value);
        }
        public IEnumerator<KeyValuePair<(Guid, int), int>> GetEnumerator() => values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static PatternPiece Piece() => new()
    {
        Points = [new(0, 0), new(6, 0), new(6, 4), new(0, 4)],
        Grainline = new() { Start = new(1, 1), End = new(1, 2) },
        Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 3 }
    };

    [Fact]
    public void ExplicitMixedOrientationsCanFitShorterShelfAndKeepCopyIdentity()
    {
        var piece = Piece(); var source = new ProjectDocument { Pieces = [piece] };
        var angles = new Dictionary<(Guid, int), int> { [(piece.Id, 1)] = 0, [(piece.Id, 2)] = 90, [(piece.Id, 3)] = 0 };
        var result = PerCopyOrthogonalNesting.Arrange(source, [piece.Id], angles, 10, 0, 10, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.Equal(10, result.Plan.Nesting.UsedLength);
        Assert.Equal(3, result.Plan.Placements.Count);
        foreach (var mapping in result.Plan.Placements)
            Assert.Equal(angles[(piece.Id, mapping.CopyNumber)], result.PlacedRotations[mapping.PlacedPieceId]);
        Assert.Equal(new Point2(6, 0), piece.Points[1]);
        Assert.Equal(3, piece.Metadata!.Quantity);
    }

    [Fact]
    public void MissingAngleAndDisallowedGrainFailWithoutChangingSource()
    {
        var piece = Piece(); var source = new ProjectDocument { Pieces = [piece] };
        var angles = new Dictionary<(Guid, int), int> { [(piece.Id, 1)] = 0, [(piece.Id, 2)] = 90 };
        Assert.Throws<ArgumentException>(() => PerCopyOrthogonalNesting.Arrange(source, [piece.Id], angles,
            10, 0, 100, 0, GrainDirectionPolicy.Direction.TwoWay, 90));
        angles[(piece.Id, 3)] = 0;
        var error = Assert.Throws<ArgumentException>(() => PerCopyOrthogonalNesting.Arrange(source, [piece.Id], angles,
            10, 0, 100, 0, GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _));
        Assert.Equal(piece.Id, id);
        Assert.Equal(new Point2(1, 2), piece.Grainline!.End);
    }

    [Fact]
    public void InvalidCopyNameReportsOriginalSourceIdentity()
    {
        var piece = Piece(); piece.Name = new string('x', 1025);
        var angles = new Dictionary<(Guid, int), int> { [(piece.Id, 1)] = 0, [(piece.Id, 2)] = 0, [(piece.Id, 3)] = 0 };
        var error = Assert.Throws<ArgumentException>(() => PerCopyOrthogonalNesting.Arrange(
            new() { Pieces = [piece] }, [piece.Id], angles, 100, 0, 100, 0,
            GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _));
        Assert.Equal(piece.Id, id);
    }

    [Fact]
    public void SourceFingerprintComesFromTheDetachedCopiesActuallyPlaced()
    {
        var piece = Piece(); var source = new ProjectDocument { Pieces = [piece] }; var before = source.Clone();
        var angles = new Dictionary<(Guid, int), int> { [(piece.Id, 1)] = 0, [(piece.Id, 2)] = 90, [(piece.Id, 3)] = 0 };
        var editing = new EditingAngles(angles, () => piece.Points[1] = new Point2(7, 0));
        var result = PerCopyOrthogonalNesting.Arrange(source, [piece.Id], editing, 10, 0, 10, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.Equal(NestingSnapshot.ComputeLayoutFingerprint(before), result.SourceFingerprint);
        Assert.True(PerCopyTraceVerifier.IsValidForConstraints(before, [piece.Id], result, angles,
            GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 10));
        Assert.False(PerCopyTraceVerifier.IsValidForConstraints(source, [piece.Id], result, angles,
            GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 10));
    }
}
