using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class OrthogonalTraceVerifierTests
{
    private static (ProjectDocument Source, Guid[] Selected, OrthogonalNestingResult Result) Create()
    {
        PatternPiece Make(int width, int quantity) => new()
        {
            Name = $"P{width}", Points = [new(0, 0), new(width, 0), new(width, 10), new(0, 10)],
            Grainline = new() { Start = new(0, 0), End = new(0, 5) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = quantity }
        };
        var a = Make(20, 2); var b = Make(30, 1);
        var source = new ProjectDocument { Pieces = [a, b] };
        Guid[] selected = [a.Id, b.Id];
        var result = OrthogonalGrainNesting.Arrange(source, selected, 100, 0, 1000, 0,
            [0], GrainDirectionPolicy.Direction.TwoWay, 0);
        return (source, selected, result);
    }

    [Fact]
    public void AcceptsMultipleSourcesAndCopies()
    {
        var (source, selected, result) = Create();
        Assert.True(OrthogonalTraceVerifier.IsValid(source, selected, result));
    }

    [Fact]
    public void ConstraintVerificationChecksAnglesGrainAndFabricInputs()
    {
        var (source, selected, result) = Create();
        var allowed = selected.ToDictionary(id => id, _ => (IReadOnlyList<int>)[0]);
        bool Check(double width = 100, GrainDirectionPolicy.Direction direction = GrainDirectionPolicy.Direction.OneWay,
            double maxLength = 1000) => OrthogonalTraceVerifier.IsValidForConstraints(source, selected, result,
                allowed, direction, 0, width, 0, 0, maxLength);
        Assert.True(Check());
        Assert.False(Check(width: 99));
        Assert.False(Check(maxLength: 1));
        allowed[selected[0]] = [90];
        Assert.False(Check());
        allowed[selected[0]] = [0, 0];
        Assert.False(Check());
        allowed.Remove(selected[0]);
        Assert.False(Check());
    }

    [Fact]
    public void TwoWayPlanCannotBeReusedAsOneWayWhenGrainIsReversed()
    {
        var (source, selected, _) = Create();
        var allowed = selected.ToDictionary(id => id, _ => (IReadOnlyList<int>)[180]);
        var result = OrthogonalGrainNesting.ArrangeWithPieceRotations(source, selected, 100, 0, 1000, 0,
            allowed, GrainDirectionPolicy.Direction.TwoWay, 0);
        Assert.True(OrthogonalTraceVerifier.IsValidForConstraints(source, selected, result, allowed,
            GrainDirectionPolicy.Direction.TwoWay, 0, 100, 0, 0, 1000));
        Assert.False(OrthogonalTraceVerifier.IsValidForConstraints(source, selected, result, allowed,
            GrainDirectionPolicy.Direction.OneWay, 0, 100, 0, 0, 1000));
    }

    [Fact]
    public void RejectsChangedMappingAndRotation()
    {
        var (source, selected, result) = Create();
        var mappings = result.Plan.Placements.ToArray();
        mappings[1] = mappings[1] with { CopyNumber = mappings[0].CopyNumber };
        Assert.False(OrthogonalTraceVerifier.IsValid(source, selected, result with { Plan = result.Plan with { Placements = mappings } }));
        var angles = new Dictionary<Guid, int>(result.SourceRotations) { [selected[0]] = 90 };
        Assert.False(OrthogonalTraceVerifier.IsValid(source, selected, result with { SourceRotations = angles }));
        angles.Remove(selected[1]);
        Assert.False(OrthogonalTraceVerifier.IsValid(source, selected, result with { SourceRotations = angles }));
    }

    [Fact]
    public void RejectsSwappedCopyNumbersEvenWhenCopiesHaveIdenticalGeometry()
    {
        var (source, selected, result) = Create();
        var mappings = result.Plan.Placements.ToArray();
        var first = Array.FindIndex(mappings, p => p.SourcePieceId == selected[0] && p.CopyNumber == 1);
        var second = Array.FindIndex(mappings, p => p.SourcePieceId == selected[0] && p.CopyNumber == 2);
        Assert.True(first >= 0 && second >= 0);
        mappings[first] = mappings[first] with { CopyNumber = 2 };
        mappings[second] = mappings[second] with { CopyNumber = 1 };
        Assert.False(OrthogonalTraceVerifier.IsValid(source, selected, result with { Plan = result.Plan with { Placements = mappings } }));
    }

    [Fact]
    public void RejectsEditedLayoutEvenWithRecomputedFingerprintAndChangedSource()
    {
        var (source, selected, result) = Create();
        result.Plan.Nesting.Layout.Pieces[0].Points[1] = new(99, 0);
        Assert.False(OrthogonalTraceVerifier.IsValid(source, selected, result));
        var changed = result with { LayoutFingerprint = NestingSnapshot.ComputeLayoutFingerprint(result.Plan.Nesting.Layout) };
        Assert.False(OrthogonalTraceVerifier.IsValid(source, selected, changed));
        var fresh = Create();
        fresh.Source.Pieces[0].Grainline!.End = new(0, 4);
        Assert.False(OrthogonalTraceVerifier.IsValid(fresh.Source, fresh.Selected, fresh.Result));
    }

    [Fact]
    public void RejectsSingleBitSourceChangeEvenInsideTranslationBudget()
    {
        var (source, selected, result) = Create();
        Assert.True(OrthogonalTraceVerifier.IsValid(source, selected, result));
        var point = source.Pieces[0].Points[1];
        source.Pieces[0].Points[1] = new Point2(Math.BitIncrement(point.X), point.Y);
        Assert.False(OrthogonalTraceVerifier.IsValid(source, selected, result));
    }

    [Fact]
    public void SourceFingerprintUsesSelectedDocumentOrderNotSelectionOrder()
    {
        var (source, selected, result) = Create();
        Assert.True(OrthogonalTraceVerifier.IsValid(source, selected.Reverse().ToArray(), result));
        source.Pieces.Add(new PatternPiece { Name = "Seçilmeyen" });
        Assert.True(OrthogonalTraceVerifier.IsValid(source, selected, result));
        (source.Pieces[0], source.Pieces[1]) = (source.Pieces[1], source.Pieces[0]);
        Assert.False(OrthogonalTraceVerifier.IsValid(source, selected, result));
    }

    [Fact]
    public void PropagatesCancellation()
    {
        var (source, selected, result) = Create();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => OrthogonalTraceVerifier.IsValid(source, selected, result, cts.Token));
    }

    [Fact]
    public void RejectsOversizedMetadataBeforeHashing()
    {
        var (source, selected, result) = Create();
        result.Plan.Nesting.Layout.Pieces[0].Metadata!.Material = new string('x', 129);
        Assert.False(OrthogonalTraceVerifier.IsValid(source, selected, result));
        var fresh = Create();
        fresh.Source.Pieces[0].Name = new string('x', 1025);
        Assert.False(OrthogonalTraceVerifier.IsValid(fresh.Source, fresh.Selected, fresh.Result));
    }

    [Fact]
    public void ConstraintVerificationPropagatesCancellation()
    {
        var (source, selected, result) = Create();
        var allowed = selected.ToDictionary(id => id, _ => (IReadOnlyList<int>)[0]);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => OrthogonalTraceVerifier.IsValidForConstraints(
            source, selected, result, allowed, GrainDirectionPolicy.Direction.OneWay, 0, 100, 0, 0, 1000, cts.Token));
    }

    [Fact]
    public void ConstraintVerificationRejectsFabricWidthBeyondEngineDomain()
    {
        var (source, selected, result) = Create();
        var allowed = selected.ToDictionary(id => id, _ => (IReadOnlyList<int>)[0]);
        double width = NestingEngine.MaximumDimension + 1;
        var edited = result with { Plan = result.Plan with { Nesting = result.Plan.Nesting with { FabricWidth = width } } };
        Assert.False(OrthogonalTraceVerifier.IsValidForConstraints(source, selected, edited, allowed,
            GrainDirectionPolicy.Direction.OneWay, 0, width, 0, 0, 1000));
    }
}
