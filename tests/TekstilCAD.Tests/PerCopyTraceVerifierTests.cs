using TekstilCAD.Core;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class PerCopyTraceVerifierTests
{
    private static (ProjectDocument Source, Dictionary<(Guid, int), int> Angles, PerCopyNestingResult Result) Fixture()
    {
        var piece = new PatternPiece { Points = [new(0, 0), new(6, 0), new(6, 4), new(0, 4)],
            Grainline = new() { Start = new(1, 1), End = new(1, 2) },
            Metadata = new() { Role = "Kesim", Material = "Pamuk", Quantity = 3 } };
        var source = new ProjectDocument { Pieces = [piece] };
        var angles = new Dictionary<(Guid, int), int> { [(piece.Id, 1)] = 0, [(piece.Id, 2)] = 90, [(piece.Id, 3)] = 0 };
        return (source, angles, PerCopyOrthogonalNesting.Arrange(source, [piece.Id], angles, 10, 0, 10, 0,
            GrainDirectionPolicy.Direction.TwoWay, 90));
    }

    private static bool Verify(ProjectDocument source, Dictionary<(Guid, int), int> angles, PerCopyNestingResult result,
        CancellationToken token = default) => PerCopyTraceVerifier.IsValidForConstraints(source, [source.Pieces[0].Id], result,
            angles, GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 10, token);

    [Fact]
    public void MixedRotationsVerifyAndChangesFail()
    {
        var (source, angles, result) = Fixture();
        Assert.True(Verify(source, angles, result));
        angles[(source.Pieces[0].Id, 2)] = 0;
        Assert.False(Verify(source, angles, result));
        angles[(source.Pieces[0].Id, 2)] = 90;
        source.Pieces[0].Name = "changed";
        Assert.False(Verify(source, angles, result));
    }

    [Fact]
    public void LayoutAndMappingChangesFail()
    {
        var (source, angles, result) = Fixture();
        var mapping = result.Plan.Placements.ToArray();
        (mapping[0], mapping[1]) = (mapping[1], mapping[0]);
        Assert.False(Verify(source, angles, result with { Plan = result.Plan with { Placements = mapping } }));
        result.Plan.Nesting.Layout.Pieces[0].Name = "changed";
        Assert.False(Verify(source, angles, result));
    }

    [Fact]
    public void MissingOrExtraAnglesAndCancellationFailClosed()
    {
        var (source, angles, result) = Fixture();
        angles.Remove((source.Pieces[0].Id, 1));
        Assert.False(Verify(source, angles, result));
        angles[(Guid.NewGuid(), 1)] = 0;
        Assert.False(Verify(source, angles, result));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Verify(source, angles, result, cancellation.Token));
    }

    [Fact]
    public void OversizedOutputMetadataFailsBeforeFingerprinting()
    {
        var (source, angles, result) = Fixture();
        result.Plan.Nesting.Layout.Pieces[0].Metadata!.Material = new string('x', 129);
        Assert.False(Verify(source, angles, result));
    }

    [Fact]
    public void VerifiesMixedCopyAnglesAgainstEachSourcesAllowedList()
    {
        var (source, _, result) = Fixture();
        var id = source.Pieces[0].Id;
        var allowed = new Dictionary<Guid, IReadOnlyList<int>> { [id] = [0, 90] };
        bool Check() => PerCopyTraceVerifier.IsValidForAllowedAngles(source, [id], result, allowed,
            GrainDirectionPolicy.Direction.TwoWay, 90, 10, 0, 0, 10);
        Assert.True(Check());
        allowed[id] = [0];
        Assert.False(Check());
        allowed[id] = [0, 90, 90];
        Assert.False(Check());
        allowed[id] = [0, 90];
        allowed[Guid.NewGuid()] = [0];
        Assert.False(Check());
    }
}
