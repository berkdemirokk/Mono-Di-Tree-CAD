using System.Text;
using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfV5CombinedHistoryTests
{
    [Fact]
    public void V5RoundTripRetainsImportNestingAndGrainlineTogether()
    {
        var piece = new PatternPiece { Name="Ön", Points=[new(10,10),new(30,10),new(30,40),new(10,40)],
            Grainline=new Grainline { Start=new(15,15), End=new(15,35), NeedsReview=true } };
        var doc = new ProjectDocument { Pieces=[piece], ImportProvenance=new DxfImportProvenance {
            SourceFileName="kaynak.dxf", SourceSha256=new string('A',64), SourceByteLength=53,
            MillimetersPerUnit=1, Warnings=["Kaynak ölçüsünü doğrulayın"], PieceIds=[piece.Id] } };
        doc.NestingSnapshot=new NestingSnapshot { FabricWidth=100, UsedLength=50, Gap=2, EdgeMargin=10,
            LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(doc),
            Placements=[new NestingPlacementSnapshot(Guid.NewGuid(),1,piece.Id)] };

        ProjectStorage.Validate(doc);
        var json=ProjectStorage.SerializeProject(doc);
        using var parsed=JsonDocument.Parse(json);
        Assert.Equal(5,parsed.RootElement[0].GetInt32());
        var loaded=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(json));
        Assert.True(DocumentSession.ContentEquals(doc,loaded));
        Assert.True(loaded.NestingSnapshot!.IsCurrentFor(loaded));
        Assert.True(loaded.Pieces[0].Grainline!.NeedsReview);
        Assert.Equal(doc.ImportProvenance!.Warnings,loaded.ImportProvenance!.Warnings);
    }
}
