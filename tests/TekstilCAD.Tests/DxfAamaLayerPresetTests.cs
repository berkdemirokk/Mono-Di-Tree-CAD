using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfAamaLayerPresetTests
{
    private static string Triangle(string handle, string layer, int x) =>
        $"0\nLWPOLYLINE\n5\n{handle}\n100\nAcDbPolyline\n8\n{layer}\n90\n3\n70\n1\n" +
        $"10\n{x}\n20\n0\n10\n{x + 20}\n20\n0\n10\n{x}\n20\n20\n";

    private static DxfImportResult Import(string entities)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n"));
        return DxfFormat.Import(stream, "aama preset");
    }

    [Fact]
    public void ProposesRolesOnlyForExactNumericStandardLayers()
    {
        var import = Import(Triangle("A1", "7", 0) + Triangle("A2", "1", 100) + Triangle("A3", "1", 200) +
            Triangle("A4", "01", 300) + Triangle("A5", "CUT", 400) + Triangle("A6", "9", 500));
        var suggestion = DxfAamaLayerPreset.Suggest(import);
        Assert.Equal(new[] { 1, 7 }, suggestion.Proposals.Select(p => p.Number));
        Assert.Equal(new[] { "1", "7" }, suggestion.Proposals.Select(p => p.Layer));
        Assert.Equal(DxfTextileRole.Cutting, suggestion.Proposals[0].Role);
        Assert.Equal(2, suggestion.Proposals[0].PieceCount);
        Assert.Equal(DxfTextileRole.Auxiliary, suggestion.Proposals[1].Role);
        Assert.Equal(1, suggestion.Proposals[1].PieceCount);
        Assert.Equal(new[] { "01", "9", "CUT" }, suggestion.UnrecognizedLayers);
    }

    [Fact]
    public void SuggestionFeedsTheExplicitLayerPlanWithoutChangingTheProject()
    {
        var import = Import(Triangle("A1", "1", 0) + Triangle("A2", "14", 100) + Triangle("A3", "OTHER", 200));
        var token = DxfTextileGrainlineMapping.CaptureFingerprint(import);
        var suggestion = DxfAamaLayerPreset.Suggest(import);
        var plan = DxfTextileLayerRolePlan.Create(import, suggestion.ToSelections(), token);
        Assert.Equal(2, plan.Candidates.Length);
        Assert.All(plan.Candidates, candidate => Assert.Equal(DxfLayerRoleEligibility.Eligible, candidate.Eligibility));
        Assert.Equal(token, DxfTextileGrainlineMapping.CaptureFingerprint(import));
        Assert.All(import.Project.Pieces, piece => Assert.Null(piece.Metadata?.Role));
    }

    [Fact]
    public void NoStandardLayersYieldsEmptyProposal()
    {
        var suggestion = DxfAamaLayerPreset.Suggest(Import(Triangle("A1", "CUT", 0)));
        Assert.Empty(suggestion.Proposals);
        Assert.Equal(new[] { "CUT" }, suggestion.UnrecognizedLayers);
    }

    [Fact]
    public void MissingPieceSourcesAreRejected()
    {
        var import = Import(Triangle("A1", "1", 0)) with { PieceSources = null };
        Assert.Throws<InvalidDataException>(() => DxfAamaLayerPreset.Suggest(import));
    }
}
