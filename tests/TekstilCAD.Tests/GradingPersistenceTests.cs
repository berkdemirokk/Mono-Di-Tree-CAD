using System.Text.Json;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public class GradingPersistenceTests
{
    private static ProjectDocument Sample()
    {
        var p = new PatternPiece { Points = [new(0,0),new(10,0),new(0,10)] };
        return new ProjectDocument { Pieces = [p], GradingRules = [new() { SourcePieceId=p.Id, SourceClosed=true, SourcePoints=[..p.Points], Deltas=[new(1,2),new(0,0),new(-1,3)] }] };
    }
    [Fact] public void JsonRoundTripAndOldDocumentRemainCompatible()
    {
        var original=Sample();
        var round=JsonSerializer.Deserialize<ProjectDocument>(JsonSerializer.Serialize(original))!;
        ProjectStorage.Validate(round);
        Assert.True(DocumentSession.ContentEquals(original,round));
        var old=JsonSerializer.Deserialize<ProjectDocument>("{\"Name\":\"old\",\"Pieces\":[]}")!;
        ProjectStorage.Validate(old); Assert.Empty(old.GradingRules);
    }
    [Fact] public void RuleEditsParticipateInHistoryAndClones()
    {
        var input=Sample(); var session=new DocumentSession(input);
        input.GradingRules[0].Deltas[0]=new(9,9);
        Assert.Equal(new Point2(1,2),session.Document.GradingRules[0].Deltas[0]);
        session.Execute(d=>d.GradingRules[0].Deltas[0]=new(3,4),ProjectStorage.Validate);
        Assert.True(session.IsDirty); session.Undo(); Assert.False(session.IsDirty);
        session.Redo(); Assert.Equal(new Point2(3,4),session.Document.GradingRules[0].Deltas[0]);
    }
    [Fact] public void InvalidRulesAreRejected()
    {
        var p=Sample(); p.GradingRules.Add(p.GradingRules[0].Clone());
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(p));
        p.GradingRules.RemoveAt(1); p.GradingRules[0].Deltas[0]=new(double.NaN,0);
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(p));
        p=Sample(); p.GradingRules[0].SourcePieceId=Guid.NewGuid();
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(p));
        p=Sample(); p.GradingRules[0].Deltas.RemoveAt(0);
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(p));
    }
    [Fact] public void PersistedRulesRejectLegacyObjectReaderAndRoundTrip()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName,"work","grading-format-tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir,Guid.NewGuid().ToString("N")+".tcad");
        try
        {
            var original=Sample(); ProjectStorage.Save(original,path);
            var json=File.ReadAllText(path);
            Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ProjectDocument>(json));
            Assert.True(DocumentSession.ContentEquals(original,ProjectStorage.Load(path)));
            var token=ProjectStorage.CaptureVersion(path);
            ProjectStorage.SaveIfUnchanged(original,path,token);
            Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ProjectDocument>(File.ReadAllText(path)));
            File.WriteAllText(path,"[3,{}]");
            Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(path));
        }
        finally { if(File.Exists(path)) File.Delete(path); }
    }
}
