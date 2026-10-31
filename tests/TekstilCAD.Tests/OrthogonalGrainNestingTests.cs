using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class OrthogonalGrainNestingTests
{
    private sealed class IndexedAngleList : IReadOnlyList<int>
    {
        public int Count => 1;
        public int this[int index] => index == 0 ? 90 : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<int> GetEnumerator() => throw new InvalidOperationException("Enumeration is not bounded by Count.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SelectionWithSideEffect : IReadOnlyCollection<Guid>
    {
        private readonly Guid id;
        private readonly Action edit;
        public int Enumerations { get; private set; }
        public SelectionWithSideEffect(Guid id, Action edit) { this.id = id; this.edit = edit; }
        public int Count => 1;
        public IEnumerator<Guid> GetEnumerator()
        {
            if (++Enumerations == 2) edit();
            yield return id;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void AngleListsUseBoundedIndexedSnapshot()
    {
        var piece = Piece(); var source = new ProjectDocument { Pieces = [piece] };
        var rotations = new Dictionary<Guid, IReadOnlyList<int>> { [piece.Id] = new IndexedAngleList() };
        var result = OrthogonalGrainNesting.ArrangeWithPieceRotations(source, [piece.Id], 20, 0, 100, 0,
            rotations, GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.True(OrthogonalTraceVerifier.IsValidForConstraints(source, [piece.Id], result, rotations,
            GrainDirectionPolicy.Direction.OneWay, 0, 20, 0, 0, 100));
    }

    [Fact]
    public void SelectedIdsAreCapturedOnceBeforePlanning()
    {
        var piece = Piece(); var source = new ProjectDocument { Pieces = [piece] };
        var selected = new SelectionWithSideEffect(piece.Id, () => piece.Points[1] = new Point2(99, 0));
        var result = OrthogonalGrainNesting.Arrange(source, selected, 20, 0, 100, 0,
            [90], GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.Equal(1, selected.Enumerations);
        Assert.Equal(new Point2(40, 0), piece.Points[1]);
        Assert.Equal(NestingSnapshot.ComputeLayoutFingerprint(source), result.SourceFingerprint);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidLengthIsRejectedBeforeContourProcessing(double maxLength)
    {
        var piece = Piece();
        piece.Points[0] = new Point2(double.NaN, 0);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => OrthogonalGrainNesting.Arrange(
            new() { Pieces = [piece] }, [piece.Id], 100, 0, maxLength, 0, [90],
            GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.Equal("maxLength", error.ParamName);
    }

    private static PatternPiece Piece() => new()
    {
        Points = [new(0, 0), new(40, 0), new(40, 10), new(0, 10)],
        Grainline = new() { Start = new(0, 0), End = new(10, 0) },
        Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 2 }
    };

    [Fact]
    public void FindsAllowedRotationThatFitsAndPreservesGrainForEveryCopy()
    {
        var piece = Piece(); var document = new ProjectDocument { Pieces = [piece] };
        var result = OrthogonalGrainNesting.Arrange(document, [piece.Id], 20, 0, 100, 0,
            [0, 270, 90], GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.Equal(90, result.SourceRotations[piece.Id]);
        Assert.Equal(2, result.Plan.Nesting.Layout.Pieces.Count);
        Assert.All(result.Plan.Nesting.Layout.Pieces, p =>
        {
            var bounds = Geometry.Bounds(p.Points);
            Assert.True(bounds.MinX >= 0 && bounds.MaxX <= 20);
            GrainDirectionPolicy.Validate(p, GrainDirectionPolicy.Direction.OneWay, 0);
        });
        Assert.Equal(new Point2(40, 0), piece.Points[1]);
        Assert.Equal(new Point2(10, 0), piece.Grainline!.End);
    }

    [Fact]
    public void FailsWithSourceIdentityWhenNoAllowedRotationSatisfiesBothConstraints()
    {
        var piece = Piece(); var document = new ProjectDocument { Pieces = [piece] };
        var error = Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.Arrange(document,
            [piece.Id], 20, 0, 100, 0, [0, 270], GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.True(NestingPieceError.TryGet(error, out var id, out _));
        Assert.Equal(piece.Id, id);
    }

    [Fact]
    public void ReviewFlagCannotBeBypassedByTryingOtherRotations()
    {
        var piece = Piece(); piece.Grainline!.NeedsReview = true;
        Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.Arrange(new() { Pieces = [piece] },
            [piece.Id], 20, 0, 100, 0, [90, 270], GrainDirectionPolicy.Direction.TwoWay, 0));
    }

    [Fact]
    public void ChoosesShorterAdmissibleOrientationToFitLength()
    {
        var piece = new PatternPiece
        {
            Points = [new(0, 0), new(10, 0), new(10, 40), new(0, 40)],
            Grainline = new() { Start = new(0, 0), End = new(0, 10) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var result = OrthogonalGrainNesting.Arrange(new() { Pieces = [piece] }, [piece.Id],
            50, 0, 15, 0, [0, 90, 270], GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.Equal(90, result.SourceRotations[piece.Id]);
        Assert.Equal(10, result.Plan.Nesting.UsedLength);
        Assert.Equal(new Point2(10, 40), piece.Points[2]);
    }

    [Fact]
    public void UsesQuantityAndWidthWhenATallerOrientationNeedsFewerRows()
    {
        var piece = new PatternPiece
        {
            Points = [new(0, 0), new(50, 0), new(50, 30), new(0, 30)],
            Grainline = new() { Start = new(0, 0), End = new(0, 10) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 3 }
        };
        var result = OrthogonalGrainNesting.Arrange(new() { Pieces = [piece] }, [piece.Id],
            100, 0, 55, 0, [0, 90], GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.Equal(90, result.SourceRotations[piece.Id]);
        Assert.Equal(50, result.Plan.Nesting.UsedLength);
        Assert.Equal(3, result.Plan.Nesting.Layout.Pieces.Count);
    }

    [Fact]
    public void MixedSourcesCanUseNarrowerOrientationsToShareOneShelf()
    {
        PatternPiece Rectangle(double w, double h) => new()
        {
            Points = [new(0, 0), new(w, 0), new(w, h), new(0, h)],
            Grainline = new() { Start = new(0, 0), End = new(0, 1) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var a = Rectangle(6, 4); var b = Rectangle(4, 6);
        var result = OrthogonalGrainNesting.Arrange(new() { Pieces = [a, b] }, [a.Id, b.Id],
            10, 0, 7, 0, [0, 90], GrainDirectionPolicy.Direction.TwoWay, 90);
        Assert.Equal(6, result.Plan.Nesting.UsedLength);
        Assert.Equal(90, result.SourceRotations[a.Id]);
        Assert.Equal(0, result.SourceRotations[b.Id]);
        Assert.Equal(2, result.Plan.Nesting.Layout.Pieces.Count);
    }

    [Fact]
    public void LengthFailureNeverReturnsAnOverlengthAlternate()
    {
        var piece = new PatternPiece
        {
            Points = [new(0, 0), new(6, 0), new(6, 4), new(0, 4)],
            Grainline = new() { Start = new(0, 0), End = new(0, 1) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 3 }
        };
        Assert.Throws<InvalidOperationException>(() => OrthogonalGrainNesting.Arrange(
            new() { Pieces = [piece] }, [piece.Id], 10, 0, 3, 0, [0, 90],
            GrainDirectionPolicy.Direction.TwoWay, 90));
        Assert.Equal(new Point2(6, 0), piece.Points[1]);
    }

    [Fact]
    public void RejectsOversizedSourceBeforeExaminingSelectedGeometry()
    {
        var piece = Piece();
        var document = new ProjectDocument { Pieces = Enumerable.Repeat(piece, NestingEngine.MaximumTotalVertices + 1).ToList() };
        var error = Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.Arrange(document,
            [piece.Id], 20, 0, 100, 0, [90], GrainDirectionPolicy.Direction.OneWay, 0));
        Assert.Equal("source", error.ParamName);
    }

    [Fact]
    public void RotatedCopiesFitExactUsableWidthWithMarginsAndFailBelowIt()
    {
        var piece = Piece();
        var document = new ProjectDocument { Pieces = [piece] };
        var result = OrthogonalGrainNesting.Arrange(document, [piece.Id], 12, 0, 100, 1,
            [90], GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.All(result.Plan.Nesting.Layout.Pieces, p =>
        {
            var b = Geometry.Bounds(p.Points);
            Assert.Equal(1, b.MinX);
            Assert.Equal(11, b.MaxX);
        });
        Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.Arrange(document, [piece.Id],
            Math.BitDecrement(12), 0, 100, 1, [90], GrainDirectionPolicy.Direction.OneWay, 0));
    }

    [Fact]
    public void MultipleSourcesKeepTheirOwnCopyMappingAndRotatedVectors()
    {
        var first = Piece();
        var second = new PatternPiece
        {
            Points = [new(0, 0), new(10, 0), new(10, 20), new(0, 20)],
            Grainline = new() { Start = new(0, 0), End = new(0, 10) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var result = OrthogonalGrainNesting.Arrange(new() { Pieces = [first, second] },
            [first.Id, second.Id], 60, 0, 200, 0, [0, 90], GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.Equal(90, result.SourceRotations[first.Id]);
        Assert.Equal(0, result.SourceRotations[second.Id]);
        Assert.Equal(3, result.Plan.Placements.Count);
        Assert.Equal(new[] { 1, 2 }, result.Plan.Placements.Where(p => p.SourcePieceId == first.Id).Select(p => p.CopyNumber));
        Assert.Single(result.Plan.Placements, p => p.SourcePieceId == second.Id && p.CopyNumber == 1);
        foreach (var mapping in result.Plan.Placements)
        {
            var placed = Assert.Single(result.Plan.Nesting.Layout.Pieces, p => p.Id == mapping.PlacedPieceId);
            var expectedEdge = mapping.SourcePieceId == first.Id ? new Point2(0, 40) : new Point2(10, 0);
            var expectedGrain = new Point2(0, 10);
            Assert.Equal(expectedEdge, placed.Points[1] - placed.Points[0]);
            Assert.Equal(expectedGrain, placed.Grainline!.End - placed.Grainline.Start);
        }
    }

    [Fact]
    public void PieceSpecificAnglesAreRequiredAndEnforced()
    {
        var first = Piece();
        var second = new PatternPiece
        {
            Points = [new(0, 0), new(10, 0), new(10, 20), new(0, 20)],
            Grainline = new() { Start = new(0, 0), End = new(0, 1) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 1 }
        };
        var source = new ProjectDocument { Pieces = [first, second] };
        var limits = new Dictionary<Guid, IReadOnlyList<int>> { [first.Id] = [90], [second.Id] = [0] };
        var result = OrthogonalGrainNesting.ArrangeWithPieceRotations(source, [first.Id, second.Id],
            60, 0, 200, 0, limits, GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.Equal(90, result.SourceRotations[first.Id]);
        Assert.Equal(0, result.SourceRotations[second.Id]);
        limits[second.Id] = [90];
        Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.ArrangeWithPieceRotations(source,
            [first.Id, second.Id], 60, 0, 200, 0, limits, GrainDirectionPolicy.Direction.OneWay, 0));
        limits.Remove(second.Id);
        Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.ArrangeWithPieceRotations(source,
            [first.Id, second.Id], 60, 0, 200, 0, limits, GrainDirectionPolicy.Direction.OneWay, 0));
        limits[second.Id] = [0, 0];
        Assert.Throws<ArgumentException>(() => OrthogonalGrainNesting.ArrangeWithPieceRotations(source,
            [first.Id, second.Id], 60, 0, 200, 0, limits, GrainDirectionPolicy.Direction.OneWay, 0));
    }

    [Fact]
    public void LayoutFingerprintDetectsGeometryAndGrainEdits()
    {
        var piece = Piece();
        var result = OrthogonalGrainNesting.Arrange(new() { Pieces = [piece] }, [piece.Id],
            20, 0, 100, 0, [90], GrainDirectionPolicy.Direction.OneWay, 0);
        Assert.True(result.IsLayoutUnchanged());
        var placed = result.Plan.Nesting.Layout.Pieces[0];
        var start = placed.Grainline!.Start;
        placed.Grainline.Start = new Point2(start.X + 1, start.Y);
        Assert.False(result.IsLayoutUnchanged());
        placed.Grainline.Start = start;
        Assert.True(result.IsLayoutUnchanged());
        placed.Points[0] = new Point2(placed.Points[0].X + 1, placed.Points[0].Y);
        Assert.False(result.IsLayoutUnchanged());
    }
}
