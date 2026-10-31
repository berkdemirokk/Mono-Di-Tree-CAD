using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public class FactoryAcceptanceNestingWorkflowTests
{
    // Synthetic dimensions and quantities exercise the workflow; they are not factory data.
    private static PatternPiece CutPiece(string name, int quantity, double width, double height) => new()
    {
        Name = name,
        Points = [new(0, 0), new(width, 0), new(width, height), new(0, height)],
        Metadata = new() { Role = "Kesim", Quantity = quantity, Material = "Pamuk" }
    };

    [Fact]
    public void SelectedProductionCopiesSurviveSaveAndReopenWithTraceableSnapshot()
    {
        var front = CutPiece("Ön", 2, 20, 30);
        var back = CutPiece("Arka", 1, 25, 35);
        var excluded = CutPiece("Cep", 4, 10, 10);
        var source = new ProjectDocument { Name = "Sentetik model", Pieces = [front, back, excluded] };
        var original = source.Clone();

        var plan = ProductionNestingPlan.Arrange(source, [front.Id, back.Id], 80, 3, edgeMargin: 2);
        var layout = plan.Nesting.Layout;
        layout.NestingSnapshot = new NestingSnapshot
        {
            FabricWidth = plan.Nesting.FabricWidth,
            UsedLength = plan.Nesting.UsedLength,
            Gap = plan.Nesting.Gap,
            EdgeMargin = plan.Nesting.EdgeMargin,
            Algorithm = NestingSnapshot.ShelfAlgorithm,
            LayoutFingerprint = NestingSnapshot.ComputeLayoutFingerprint(layout),
            Placements = plan.Placements.Select(p => new NestingPlacementSnapshot(p.SourcePieceId, p.CopyNumber, p.PlacedPieceId)).ToList()
        };

        Assert.True(DocumentSession.ContentEquals(original, source));
        Assert.Equal(3, layout.Pieces.Count);
        Assert.DoesNotContain(plan.Placements, p => p.SourcePieceId == excluded.Id);
        Assert.Equal(new[] { 1, 2 }, plan.Placements.Where(p => p.SourcePieceId == front.Id).Select(p => p.CopyNumber));
        Assert.Equal(1, Assert.Single(plan.Placements, p => p.SourcePieceId == back.Id).CopyNumber);
        Assert.True(layout.NestingSnapshot.IsCurrentFor(layout));

        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root!.FullName, "work", "factory-acceptance-nesting", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "layout.tcad");
        try
        {
            ProjectStorage.Save(layout, path);
            var reopened = ProjectStorage.Load(path);
            Assert.True(DocumentSession.ContentEquals(layout, reopened));
            Assert.True(reopened.NestingSnapshot!.IsCurrentFor(reopened));
            Assert.Equal(layout.NestingSnapshot.Placements, reopened.NestingSnapshot.Placements);
            foreach (var placement in reopened.NestingSnapshot.Placements)
            {
                var sourcePiece = Assert.Single(source.Pieces, p => p.Id == placement.SourcePieceId);
                var placedPiece = Assert.Single(reopened.Pieces, p => p.Id == placement.PlacedPieceId);
                Assert.Equal(sourcePiece.Name, placedPiece.Name);
                Assert.Equal(sourcePiece.Metadata!.Material, placedPiece.Metadata!.Material);
                Assert.Equal(1, placedPiece.Metadata.Quantity);
            }
            Assert.True(DocumentSession.ContentEquals(original, source));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}