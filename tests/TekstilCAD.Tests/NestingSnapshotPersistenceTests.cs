using System.Text.Json;
using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class NestingSnapshotPersistenceTests
{
    private static string NewPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var dir = Path.Combine(root!.FullName, "work", "nesting-snapshot-tests");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tcad");
    }

    private static ProjectDocument Sample()
    {
        var placed = new PatternPiece { Name = "Ön", Points = [new(10,10),new(30,10),new(30,40),new(10,40)],
            Metadata = new PieceMetadata { Role="Kesim", Material="Pamuk", Quantity=1 } };
        var doc = new ProjectDocument { Name = "Pastal", Pieces = [placed] };
        doc.NestingSnapshot = new NestingSnapshot { FabricWidth=100, UsedLength=50, Gap=2, EdgeMargin=10,
            LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(doc),
            Placements=[new NestingPlacementSnapshot(Guid.NewGuid(),1,placed.Id)] };
        return doc;
    }

    [Fact]
    public void V3RoundTripPreservesHistoricalInputsAndOldReaderRejects()
    {
        var path=NewPath(); var original=Sample();
        ProjectStorage.Save(original,path);
        var raw=File.ReadAllText(path);
        using var json=JsonDocument.Parse(raw);
        Assert.Equal(3,json.RootElement[0].GetInt32());
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ProjectDocument>(raw));
        var loaded=ProjectStorage.Load(path);
        Assert.True(DocumentSession.ContentEquals(original,loaded));
        Assert.NotNull(loaded.NestingSnapshot);
        Assert.True(loaded.NestingSnapshot!.IsCurrentFor(loaded));
        Assert.Equal(original.NestingSnapshot!.Placements,loaded.NestingSnapshot.Placements);
        Assert.NotEqual(loaded.Pieces[0].Id,loaded.NestingSnapshot.Placements[0].SourcePieceId);
    }

    [Fact]
    public void LegacyShapeDoesNotAcquireSnapshotFieldAndLegacyVersionsRejectItEvenNull()
    {
        var path=NewPath();
        var doc=Sample(); doc.NestingSnapshot=null; doc.Pieces[0].Metadata=null;
        ProjectStorage.Save(doc,path);
        var raw=File.ReadAllText(path);
        using var legacy=JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Object,legacy.RootElement.ValueKind);
        Assert.False(legacy.RootElement.TryGetProperty("NestingSnapshot",out _));
        var node=JsonNode.Parse(raw)!.AsObject(); node["NestingSnapshot"]=null;
        foreach(var version in new int?[] { null, 1, 2 })
        {
            var project=node.DeepClone();
            File.WriteAllText(path, version is null ? project.ToJsonString() : new JsonArray(JsonValue.Create(version.Value),project).ToJsonString());
            Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(path));
        }
    }

    [Fact]
    public void EditingLayoutMarksSnapshotHistoricalAndUndoRestoresCurrentState()
    {
        var session=new DocumentSession(Sample());
        Assert.True(session.Document.NestingSnapshot!.IsCurrentFor(session.Document));
        session.Execute(d=>d.Pieces[0].Points[1]=new Point2(31,10),ProjectStorage.Validate);
        Assert.True(session.IsDirty);
        Assert.False(session.Document.NestingSnapshot!.IsCurrentFor(session.Document));
        var path=NewPath(); ProjectStorage.Save(session.Document,path);
        var reopened=ProjectStorage.Load(path);
        Assert.False(reopened.NestingSnapshot!.IsCurrentFor(reopened));
        Assert.True(session.Undo());
        Assert.True(session.Document.NestingSnapshot!.IsCurrentFor(session.Document));
        Assert.False(session.IsDirty);
        Assert.True(session.Redo());
        Assert.False(session.Document.NestingSnapshot!.IsCurrentFor(session.Document));
    }

    [Fact]
    public void CloneAndSnapshotMetadataParticipateInDirtyState()
    {
        var input=Sample(); var session=new DocumentSession(input);
        input.NestingSnapshot!.Placements.Clear();
        Assert.Single(session.Document.NestingSnapshot!.Placements);
        Assert.True(session.Execute(d=>d.NestingSnapshot!.Gap=3,ProjectStorage.Validate));
        Assert.True(session.IsDirty);
        Assert.True(session.Undo());
        Assert.Equal(2,session.Document.NestingSnapshot!.Gap);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void DeletingPlacedPieceKeepsHistoricalMappingAndDoesNotClaimItIsCurrent()
    {
        var session=new DocumentSession(Sample());
        var mapping=session.Document.NestingSnapshot!.Placements.Single();
        Assert.True(session.Execute(d=>d.Pieces.Clear(),ProjectStorage.Validate));
        Assert.Equal(mapping,Assert.Single(session.Document.NestingSnapshot!.Placements));
        Assert.False(session.Document.NestingSnapshot.IsCurrentFor(session.Document));
        var path=NewPath(); ProjectStorage.Save(session.Document,path);
        var reopened=ProjectStorage.Load(path);
        Assert.Empty(reopened.Pieces);
        Assert.Equal(mapping,Assert.Single(reopened.NestingSnapshot!.Placements));
        Assert.False(reopened.NestingSnapshot.IsCurrentFor(reopened));
        Assert.True(session.Undo());
        Assert.True(session.Document.NestingSnapshot!.IsCurrentFor(session.Document));
    }

    [Fact]
    public void MissingFieldsDuplicateMappingsAndFutureVersionsFailClosed()
    {
        var path=NewPath(); ProjectStorage.Save(Sample(),path);
        var raw=File.ReadAllText(path);
        var missing=JsonNode.Parse(raw)!.AsArray();
        missing[1]!["NestingSnapshot"]!.AsObject().Remove("Gap");
        File.WriteAllText(path,missing.ToJsonString());
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(path));

        var duplicated=JsonNode.Parse(raw)!.AsArray();
        var placements=duplicated[1]!["NestingSnapshot"]!["Placements"]!.AsArray();
        placements.Add(placements[0]!.DeepClone());
        File.WriteAllText(path,duplicated.ToJsonString());
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(path));

        var future=JsonNode.Parse(raw)!.AsArray(); future[0]=5;
        File.WriteAllText(path,future.ToJsonString());
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(path));
    }

    [Fact]
    public void CurrentSnapshotCannotClaimMappingForDifferentPlacedPiece()
    {
        var doc=Sample();
        var existing=doc.NestingSnapshot!.Placements[0];
        doc.NestingSnapshot.Placements[0]=existing with { PlacedPieceId=Guid.NewGuid() };
        // The fingerprint still matches geometry, but the claimed mapping does not.
        Assert.Equal(NestingSnapshot.ComputeLayoutFingerprint(doc),doc.NestingSnapshot.LayoutFingerprint);
        Assert.False(doc.NestingSnapshot.IsCurrentFor(doc));
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(doc));
    }

    [Fact]
    public void RecoveryAndBackupPreserveSnapshotWithoutApplyingDrafts()
    {
        var root=Path.Combine(Path.GetDirectoryName(NewPath())!,Guid.NewGuid().ToString("N"));
        var recoveryDir=Path.Combine(root,"recovery");
        var doc=Sample();
        var draft=new RecoveryPropertyDraft(doc.Pieces[0].Id,"Henüz uygulanmadı","?","0",doc.Pieces[0].Name,10,10);
        string recoveryPath;
        using(var writer=new RecoveryStore(recoveryDir))
        {
            writer.Save(doc,[draft],new RecoveryPendingDrawing("Çoklu çizgi",[new(1,2),new(3,4)]));
            recoveryPath=writer.DraftRecoveryPath;
        }
        using(var reader=new RecoveryStore(recoveryDir))
        {
            var restored=reader.LoadCandidate(recoveryPath);
            Assert.True(DocumentSession.ContentEquals(doc,restored.Document));
            Assert.True(restored.Document.NestingSnapshot!.IsCurrentFor(restored.Document));
            Assert.Equal(draft,Assert.Single(restored.Drafts));
            Assert.Equal(2,restored.PendingDrawing!.Points.Count);
        }
        var source=Path.Combine(root,"projects"); Directory.CreateDirectory(source);
        ProjectStorage.Save(doc,Path.Combine(source,"layout.tcad"));
        var archive=Path.Combine(root,"backup.zip"); ProjectBackup.Create(source,archive);
        var target=Path.Combine(root,"restored"); ProjectBackup.Restore(archive,target);
        var roundTrip=ProjectStorage.Load(Path.Combine(target,"layout.tcad"));
        Assert.True(DocumentSession.ContentEquals(doc,roundTrip));
        Assert.True(roundTrip.NestingSnapshot!.IsCurrentFor(roundTrip));
    }
}
