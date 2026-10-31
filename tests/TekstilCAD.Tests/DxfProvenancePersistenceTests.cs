using System.Text;
using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfProvenancePersistenceTests
{
    private static ProjectDocument Sample()
    {
        var piece=new PatternPiece { Name="Ön",Points=[new(0,0),new(10,0),new(0,10)] };
        return new ProjectDocument { Pieces=[piece],ImportProvenance=new DxfImportProvenance {
            SourceFileName="örnek.dxf", SourceSha256=new string('A',64), SourceByteLength=53,
            MillimetersPerUnit=25.4, Warnings=["Birim kontrolü", "Kontur kontrolü"], PieceIds=[piece.Id] } };
    }
    private static ProjectDocument Read(string text)=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(text));
    private static JsonArray Saved()=>JsonNode.Parse(ProjectStorage.SerializeProject(Sample()))!.AsArray();
    private static JsonObject Provenance(JsonArray root)=>root[1]!["ImportProvenance"]!.AsObject();

    [Fact]
    public void V5RoundTripsAllFieldsAndOwnsCollections()
    {
        var original=Sample();var json=ProjectStorage.SerializeProject(original);
        Assert.Equal(5,JsonNode.Parse(json)![0]!.GetValue<int>());
        var loaded=Read(json);Assert.True(DocumentSession.ContentEquals(original,loaded));
        Assert.NotSame(original.ImportProvenance,loaded.ImportProvenance);
        Assert.NotSame(original.ImportProvenance!.Warnings,loaded.ImportProvenance!.Warnings);
        Assert.NotSame(original.ImportProvenance.PieceIds,loaded.ImportProvenance.PieceIds);
        loaded.ImportProvenance.Warnings[0]="değişti";loaded.ImportProvenance.PieceIds.Clear();
        Assert.Equal("Birim kontrolü",original.ImportProvenance.Warnings[0]);Assert.Single(original.ImportProvenance.PieceIds);
    }

    [Theory]
    [InlineData(0,false)] [InlineData(0,true)] [InlineData(1,false)] [InlineData(1,true)]
    [InlineData(2,false)] [InlineData(2,true)] [InlineData(3,false)] [InlineData(3,true)]
    [InlineData(4,false)] [InlineData(4,true)]
    public void OlderVersionsRejectInjectedProvenanceEvenNull(int version,bool useNull)
    {
        var root=Saved();if(useNull)root[1]!["ImportProvenance"]=null;
        var body=root[1]!.ToJsonString();var json=version==0 ? body : $"[{version},{body}]";
        Assert.Throws<InvalidDataException>(()=>Read(json));
    }

    [Theory]
    [InlineData("record-missing")] [InlineData("record-null")] [InlineData("name-missing")]
    [InlineData("hash-missing")] [InlineData("length-missing")] [InlineData("scale-missing")]
    [InlineData("warnings-missing")] [InlineData("ids-missing")] [InlineData("unknown")]
    [InlineData("warning-null")] [InlineData("id-invalid")]
    public void V5RejectsIncompleteUnknownOrMalformedFields(string mutation)
    {
        var root=Saved();var body=root[1]!.AsObject();var p=Provenance(root);
        switch(mutation)
        {
            case "record-missing":body.Remove("ImportProvenance");break;
            case "record-null":body["ImportProvenance"]=null;break;
            case "name-missing":p.Remove("SourceFileName");break;
            case "hash-missing":p.Remove("SourceSha256");break;
            case "length-missing":p.Remove("SourceByteLength");break;
            case "scale-missing":p.Remove("MillimetersPerUnit");break;
            case "warnings-missing":p.Remove("Warnings");break;
            case "ids-missing":p.Remove("PieceIds");break;
            case "unknown":p["extra"]=true;break;
            case "warning-null":p["Warnings"]![0]=null;break;
            case "id-invalid":p["PieceIds"]![0]="bad";break;
        }
        Assert.Throws<InvalidDataException>(()=>Read(root.ToJsonString()));
    }

    [Fact]
    public void DuplicateFieldIsRejectedAndLegacyWithoutHistoryRemainsLegacy()
    {
        var json=ProjectStorage.SerializeProject(Sample());
        Assert.Contains("\"SourceByteLength\": 53",json);
        Assert.Throws<InvalidDataException>(()=>Read(json.Replace("\"SourceByteLength\": 53","\"SourceByteLength\": 53, \"SourceByteLength\": 54",StringComparison.Ordinal)));
        var doc=Sample();doc.ImportProvenance=null;
        var legacy=ProjectStorage.SerializeProject(doc);Assert.StartsWith("{",legacy.TrimStart());
        Assert.Null(Read(legacy).ImportProvenance);
    }

    [Theory]
    [InlineData("path")] [InlineData("hash")] [InlineData("negative-length")] [InlineData("oversize-length")]
    [InlineData("zero-scale")] [InlineData("nonfinite-scale")] [InlineData("warning-size")]
    [InlineData("warning-count")] [InlineData("warning-total")] [InlineData("empty-id")] [InlineData("duplicate-id")]
    public void ValidationRejectsUntrustedOrExcessiveValues(string mutation)
    {
        var doc=Sample();var p=doc.ImportProvenance!;
        switch(mutation)
        {
            case "path":p.SourceFileName=@"C:\private\source.dxf";break;
            case "hash":p.SourceSha256=new string('Z',64);break;
            case "negative-length":p.SourceByteLength=-1;break;
            case "oversize-length":p.SourceByteLength=64L*1024*1024+1;break;
            case "zero-scale":p.MillimetersPerUnit=0;break;
            case "nonfinite-scale":p.MillimetersPerUnit=double.NaN;break;
            case "warning-size":p.Warnings=[new string('x',4097)];break;
            case "warning-count":p.Warnings=Enumerable.Repeat("x",257).ToList();break;
            case "warning-total":p.Warnings=Enumerable.Repeat(new string('x',4096),17).ToList();break;
            case "empty-id":p.PieceIds=[Guid.Empty];break;
            case "duplicate-id":p.PieceIds.Add(p.PieceIds[0]);break;
        }
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(doc));
    }

    [Fact]
    public void ProvenanceParticipatesInDirtyUndoRedoAndHistoricalIdsMayBeAbsent()
    {
        var original=Sample();var s=new DocumentSession(original);Assert.False(s.IsDirty);
        Assert.True(s.Execute(d=>d.ImportProvenance!.Warnings.Add("yeni"),ProjectStorage.Validate));
        Assert.True(s.IsDirty);Assert.True(s.Undo());Assert.False(s.IsDirty);Assert.True(s.Redo());
        Assert.Equal("yeni",s.Document.ImportProvenance!.Warnings[^1]);
        Assert.True(s.Execute(d=>d.Pieces.Clear(),ProjectStorage.Validate));
        Assert.Single(s.Document.ImportProvenance!.PieceIds);
        Assert.True(s.Undo());Assert.Single(s.Document.Pieces);
    }

    [Fact]
    public void RecoveryRoundTripPreservesProvenance()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root);
        var dir=Path.Combine(root!.FullName,"work","dxf-provenance-recovery",Guid.NewGuid().ToString("N"));
        var doc=Sample();string recoveryPath;
        using(var writer=new RecoveryStore(dir)) { writer.Save(doc);recoveryPath=writer.RecoveryPath; }
        using var reader=new RecoveryStore(dir);var recovered=reader.LoadCandidate(recoveryPath);
        Assert.True(DocumentSession.ContentEquals(doc,recovered.Document));
    }
}
