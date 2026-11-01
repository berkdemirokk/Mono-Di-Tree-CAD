using System.Text;
using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class NotchPersistenceTests
{
    private static ProjectDocument Sample()
    {
        var piece=new PatternPiece { Name="Ön",Points=[new(0,0),new(10,0),new(10,10),new(0,10)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(10,0) }] };
        return new ProjectDocument { Pieces=[piece] };
    }
    private static ProjectDocument Read(string value)=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(value));
    private static JsonArray Saved()=>JsonNode.Parse(ProjectStorage.SerializeProject(Sample()))!.AsArray();
    [Fact]
    public void VersionSixRoundTripsAndCloneIsIndependent()
    {
        var original=Sample();ProjectStorage.Validate(original);var text=ProjectStorage.SerializeProject(original);
        Assert.StartsWith("[",text.TrimStart());var root=JsonNode.Parse(text)!.AsArray();Assert.Equal(6,(int)root[0]!);
        var loaded=Read(text);Assert.True(DocumentSession.ContentEquals(original,loaded));
        var clone=loaded.Clone();clone.Pieces[0].Notches![0].VertexIndex=2;
        Assert.Equal(1,loaded.Pieces[0].Notches![0].VertexIndex);Assert.False(DocumentSession.ContentEquals(clone,loaded));
        var old=Sample();old.Pieces[0].Notches=null;Assert.StartsWith("{",ProjectStorage.SerializeProject(old).TrimStart());
    }
    [Fact]
    public void VersionSixRetainsOptionalPriorVersionFeatures()
    {
        var doc=Sample();var piece=doc.Pieces[0];
        piece.Grainline=new Grainline { Start=new(1,1),End=new(1,5),NeedsReview=true };
        doc.ImportProvenance=new DxfImportProvenance { SourceFileName="kaynak.dxf",SourceSha256=new string('A',64),SourceByteLength=53,MillimetersPerUnit=1,Warnings=[],PieceIds=[piece.Id] };
        ProjectStorage.Validate(doc);
        var loaded=Read(ProjectStorage.SerializeProject(doc));
        Assert.True(DocumentSession.ContentEquals(doc,loaded));Assert.NotNull(loaded.ImportProvenance);Assert.NotNull(loaded.Pieces[0].Grainline);
    }
    [Theory]
    [InlineData("index")] [InlineData("position")] [InlineData("x")] [InlineData("null")] [InlineData("empty")]
    public void RejectsMissingOrMalformedSerializedFields(string kind)
    {
        var root=Saved();var piece=root[1]!["Pieces"]![0]!;var mark=piece["Notches"]![0]!;
        switch(kind)
        {
            case "index":mark.AsObject().Remove("VertexIndex");break;
            case "position":mark.AsObject().Remove("Position");break;
            case "x":mark["Position"]!.AsObject().Remove("X");break;
            case "null":piece["Notches"]=null;break;
            case "empty":piece["Notches"]=new JsonArray();break;
        }
        Assert.Throws<InvalidDataException>(()=>Read(root.ToJsonString()));
    }
    [Fact]
    public void OlderVersionsCannotSilentlyDiscardNotches()
    {
        var root=Saved();root[0]=5;
        Assert.Throws<InvalidDataException>(()=>Read(root.ToJsonString()));
        Assert.Throws<InvalidDataException>(()=>Read(root[1]!.ToJsonString()));
    }
    [Fact]
    public void RejectsStaleDuplicateAndRepeatedClosureAlias()
    {
        var d=Sample();var mark=d.Pieces[0].Notches![0];mark.Position=new(9,0);
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(d));
        mark.Position=new(10,0);d.Pieces[0].Notches!.Add(mark.Clone());
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(d));
        d=Sample();d.Pieces[0].Points.Add(d.Pieces[0].Points[0]);d.Pieces[0].Notches=[new NotchMark { VertexIndex=4,Position=new(0,0) }];
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(d));
    }
    [Fact]
    public void SessionTracksMarkEditsAndRejectsStaleGeometryAtomically()
    {
        var original=Sample();var session=new DocumentSession(original);var before=session.Document.Clone();
        Assert.True(session.Execute(d=> { d.Pieces[0].Notches![0].VertexIndex=2;d.Pieces[0].Notches![0].Position=d.Pieces[0].Points[2]; },ProjectStorage.Validate));
        Assert.True(session.IsDirty);Assert.Equal(1,session.UndoCount);
        Assert.True(session.Undo());Assert.True(DocumentSession.ContentEquals(before,session.Document));
        Assert.True(session.Redo());Assert.Equal(2,session.Document.Pieces[0].Notches![0].VertexIndex);
        var changed=session.Document.Clone();
        Assert.Throws<InvalidDataException>(()=>session.Execute(d=>d.Pieces[0].Points[2]=new Point2(11,10),ProjectStorage.Validate));
        Assert.True(DocumentSession.ContentEquals(changed,session.Document));
    }
}
