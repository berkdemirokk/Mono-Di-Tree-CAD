using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Grading;
using TekstilCAD.Nesting;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class MetadataCrossWorkflowTests
{
    private static PatternPiece Piece() => new() { Name="Ön", Points=[new(0,0),new(100,0),new(100,100),new(0,100)], Metadata=new() { Role="Kesim", Size="M", Material="Pamuk", Quantity=3 } };
    [Fact]
    public void NestingClonesMetadataAndGeneratedDerivativesDoNotAssumeProductionFields()
    {
        var source=Piece(); var placed=Assert.Single(NestingEngine.Arrange([source],500,5).Layout.Pieces);
        Assert.Equal(source.Id,placed.Id); Assert.NotSame(source.Metadata,placed.Metadata);
        Assert.Equal("Pamuk",placed.Metadata!.Material); Assert.Equal(3,placed.Metadata.Quantity);
        placed.Metadata.Quantity=7; Assert.Equal(3,source.Metadata!.Quantity);
        var grade=GradingEngine.Generate(source,[new(0,0),new(1,0),new(1,1),new(0,1)],1,"L");
        Assert.Null(grade.Metadata); Assert.NotEqual(source.Id,grade.Id);
        var offset=PatternTools.OffsetConvex(source,5); Assert.Null(offset.Metadata); Assert.NotEqual(source.Id,offset.Id);
    }
    [Fact]
    public void MetadataChangesHaveIndependentUndoRedoSnapshots()
    {
        var doc=new ProjectDocument { Pieces=[Piece()] }; var session=new DocumentSession(doc);
        session.Execute(d=>d.Pieces[0].Metadata!.Material="Yün",ProjectStorage.Validate);
        Assert.Equal("Yün",session.Document.Pieces[0].Metadata!.Material); Assert.True(session.IsDirty);
        Assert.True(session.Undo()); Assert.Equal("Pamuk",session.Document.Pieces[0].Metadata!.Material); Assert.False(session.IsDirty);
        Assert.True(session.Redo()); Assert.Equal("Yün",session.Document.Pieces[0].Metadata!.Material);
        Assert.Equal("Pamuk",doc.Pieces[0].Metadata!.Material);
    }
}
