using System.Text;
using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfV5RecoveryEnvelopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DraftRecoveryRetainsV5HistoryAndRejectsIncompleteHistory(bool pendingDrawing)
    {
        var piece = new PatternPiece { Name="Ön", Points=[new(0,0),new(10,0),new(0,10)] };
        var document = new ProjectDocument { Pieces=[piece], ImportProvenance=new DxfImportProvenance {
            SourceFileName="kaynak.dxf", SourceSha256=new string('A',64), SourceByteLength=53,
            MillimetersPerUnit=25.4, Warnings=["Birim uyarısı", "Kontur uyarısı"], PieceIds=[piece.Id] } };
        var drafts = new[] { new RecoveryPropertyDraft(piece.Id,"Taslak","?","0","Ön",0,0) };
        var drawing = new RecoveryPendingDrawing("Çizgi", [new Point2(1,2)]);
        var text = pendingDrawing ? RecoveryEnvelope.Serialize(document,drafts,drawing)
                                  : RecoveryEnvelope.Serialize(document,drafts);

        var recovered = RecoveryEnvelope.Deserialize(Encoding.UTF8.GetBytes(text));
        Assert.True(DocumentSession.ContentEquals(document,recovered.Document));
        Assert.Equal(drafts, recovered.Drafts);
        Assert.Equal(pendingDrawing, recovered.PendingDrawing is not null);
        if (pendingDrawing) Assert.Equal(drawing.Points, recovered.PendingDrawing!.Points);

        var root = JsonNode.Parse(text)!.AsObject();
        root["Project"]![1]!["ImportProvenance"]!.AsObject().Remove("SourceSha256");
        Assert.Throws<InvalidDataException>(() => RecoveryEnvelope.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }
}
