using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Nesting;
using System.Text;
using System.Text.Json.Nodes;

namespace TekstilCAD.Tests;

public sealed class RotatedProjectValidationTests
{
    private static ProjectDocument Fixture(bool experimental=false)
    {
        var piece=new PatternPiece { Points=[new(0,0),new(6,0),new(6,4),new(0,4)],
            Notches=[new NotchMark { VertexIndex=1,Position=new(6,0) }],
            Grainline=new Grainline { Start=new(1,1),End=new(1,2) },
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 } };
        if(experimental)piece.ExperimentalALine=new(ALineExperimentalProvenance.CurrentAlgorithm,new string('A',64),"front");
        var source=new ProjectDocument { Pieces=[piece] };var angles=new Dictionary<(Guid,int),int> { [(piece.Id,1)]=0,[(piece.Id,2)]=90 };
        var result=PerCopyOrthogonalNesting.Arrange(source,[piece.Id],angles,100,1,100,0,GrainDirectionPolicy.Direction.TwoWay,90);
        var record=RotatedNestingRecord.Capture(source,[piece.Id],angles,result,GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100);
        var trace=record.ToTrace();var layout=result.Plan.Nesting.Layout.Clone();
        layout.NestingSnapshot=new NestingSnapshot { FabricWidth=100,Gap=1,EdgeMargin=0,UsedLength=result.Plan.Nesting.UsedLength,
            LayoutFingerprint=trace.LayoutFingerprint,Placements=result.Plan.Placements.Select(p=>new NestingPlacementSnapshot(p.SourcePieceId,p.CopyNumber,p.PlacedPieceId)).ToList(),RotationTrace=trace };
        return layout;
    }

    [Fact]
    public void ExperimentalRotatedHistoryRoundTripsAsVersion10AndRejectsLegacyEnvelope()
    {
        var layout=Fixture(true);var trace=layout.NestingSnapshot!.RotationTrace!;
        Assert.All(trace.SourcePieces,p=>Assert.NotNull(p.ExperimentalALine));Assert.All(trace.LayoutPieces,p=>Assert.NotNull(p.ExperimentalALine));
        Assert.All(layout.Pieces,p=>Assert.NotNull(p.ExperimentalALine));Assert.Equal(10,ProjectStorage.GetProjectFormatVersion(layout));
        var folder=Path.Combine(Path.GetTempPath(),"rotated-experimental-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"saved.tcad");
        try
        {
            ProjectStorage.Save(layout,path);var loaded=ProjectStorage.Load(path);Assert.True(DocumentSession.ContentEquals(layout,loaded));
            Assert.All(loaded.NestingSnapshot!.RotationTrace!.SourcePieces,p=>Assert.NotNull(p.ExperimentalALine));
            var json=File.ReadAllText(path);
            foreach(var oldVersion in new[]{7,8,9})
            {
                var node=JsonNode.Parse(json)!.AsArray();node[0]=oldVersion;
                foreach(var visible in node[1]!["Pieces"]!.AsArray())visible!.AsObject().Remove("ExperimentalALine");
                var oldPath=Path.Combine(folder,"old-"+oldVersion+".tcad");File.WriteAllText(oldPath,node.ToJsonString());
                var error=Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(oldPath));Assert.Contains("sürüm 10",error.Message);
            }
            foreach(var piece in layout.Pieces)piece.ExperimentalALine=null;
            Assert.Equal(10,ProjectStorage.GetProjectFormatVersion(layout));
            ProjectStorage.Save(layout,Path.Combine(folder,"historical.tcad"));
            Assert.NotNull(ProjectStorage.Load(Path.Combine(folder,"historical.tcad")).NestingSnapshot!.RotationTrace!.SourcePieces[0].ExperimentalALine);
        }
        finally { Directory.Delete(folder,true); }
    }
    [Fact]
    public void RecoveryCancellationBeforeEmbeddedV7ValidationPreservesBytes()
    {
        var document=Fixture();var bytes=Encoding.UTF8.GetBytes(RecoveryEnvelope.Serialize(document,[]));
        var original=bytes.ToArray();using var cancelled=new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(()=>RecoveryEnvelope.Deserialize(bytes,cancelled.Token,()=>cancelled.Cancel()));
        Assert.Equal(original,bytes);
        Assert.True(DocumentSession.ContentEquals(document,RecoveryEnvelope.Deserialize(bytes).Document));
    }

    [Fact]
    public void HistoricalEvidenceRemainsVerifiableAfterVisibleLayoutEdit()
    {
        var document=Fixture();RotatedProjectValidation.Verify(document);
        Assert.True(document.NestingSnapshot!.IsCurrentFor(document));
        document.Pieces[0].Name="Düzenlenmiş";
        Assert.False(document.NestingSnapshot.IsCurrentFor(document));
        RotatedProjectValidation.Verify(document);
    }

    [Fact]
    public void MismatchedConstraintsOrForgedHistoricalMarkerAreRejected()
    {
        var document=Fixture();document.NestingSnapshot!.Gap=2;
        Assert.Throws<InvalidDataException>(()=>RotatedProjectValidation.Verify(document));
        document=Fixture();var trace=document.NestingSnapshot!.RotationTrace!;
        trace.LayoutPieces[0].Notches=null;
        trace.LayoutFingerprint=NestingSnapshot.ComputeLayoutFingerprint(new ProjectDocument { Pieces=trace.LayoutPieces });
        document.NestingSnapshot.LayoutFingerprint=trace.LayoutFingerprint;
        Assert.Throws<InvalidDataException>(()=>RotatedProjectValidation.Verify(document));
    }

    [Fact]
    public void VersionSevenProjectAndRecoveryRoundTripKeepHistoricalAndStaleState()
    {
        var document=Fixture();var json=ProjectStorage.SerializeProject(document);
        Assert.Equal(7,JsonNode.Parse(json)![0]!.GetValue<int>());
        var loaded=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(json));
        Assert.True(DocumentSession.ContentEquals(document,loaded));Assert.True(loaded.NestingSnapshot!.IsCurrentFor(loaded));
        loaded.Pieces[0].Name="Düzenlenmiş";Assert.False(loaded.NestingSnapshot.IsCurrentFor(loaded));
        var changed=ProjectStorage.SerializeProject(loaded);
        var reread=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(changed));
        Assert.True(DocumentSession.ContentEquals(loaded,reread));Assert.False(reread.NestingSnapshot!.IsCurrentFor(reread));
        var recovery=RecoveryEnvelope.Serialize(loaded,[]);
        var recovered=RecoveryEnvelope.Deserialize(Encoding.UTF8.GetBytes(recovery));
        Assert.True(DocumentSession.ContentEquals(loaded,recovered.Document));
        var forged=JsonNode.Parse(recovery)!.AsObject();
        forged["Project"]![1]!["NestingSnapshot"]!["RotationTrace"]!["Copies"]![0]!["Degrees"]=180;
        Assert.Throws<InvalidDataException>(()=>RecoveryEnvelope.Deserialize(Encoding.UTF8.GetBytes(forged.ToJsonString())));
    }

    [Fact]
    public void RawVersionAndTraceFieldsCannotBeOmittedOrDowngraded()
    {
        var json=ProjectStorage.SerializeProject(Fixture());
        byte[] Edit(Action<JsonArray> edit) { var root=JsonNode.Parse(json)!.AsArray();edit(root);return Encoding.UTF8.GetBytes(root.ToJsonString()); }
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Edit(root=>root[0]=6)));
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Edit(root=>root[1]!["NestingSnapshot"]!.AsObject().Remove("RotationTrace"))));
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Edit(root=>root[1]!["NestingSnapshot"]!["RotationTrace"]!.AsObject().Remove("MaxLength"))));
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Edit(root=>root[1]!["NestingSnapshot"]!["RotationTrace"]!["LayoutPieces"]![0]!["Points"]![0]!.AsObject().Remove("X"))));
    }

    [Fact]
    public void ConflictPreservesExistingFileWithVersionSeven()
    {
        var document=Fixture();var path=Path.Combine(AppContext.BaseDirectory,$"rotation-{Guid.NewGuid():N}.tcad");
        try
        {
            var version=ProjectStorage.CaptureVersion(path);File.WriteAllText(path,"korunan");
            Assert.Throws<ProjectConflictException>(()=>ProjectStorage.SaveIfUnchanged(document,path,version));
            Assert.Equal("korunan",File.ReadAllText(path));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }

    [Fact]
    public void VersionSevenDiskRoundTripPreservesTraceAndStaleEditedLayout()
    {
        var source=Fixture();var path=Path.Combine(AppContext.BaseDirectory,$"rotation-roundtrip-{Guid.NewGuid():N}.tcad");
        try
        {
            ProjectStorage.Save(source,path);var loaded=ProjectStorage.LoadWithVersion(path);
            Assert.True(DocumentSession.ContentEquals(source,loaded.Project));
            loaded.Project.Pieces[0].Name="Düzenlenmiş";
            Assert.False(loaded.Project.NestingSnapshot!.IsCurrentFor(loaded.Project));
            var version=ProjectStorage.SaveIfUnchanged(loaded.Project,path,loaded.Version);
            var reopened=ProjectStorage.LoadWithVersion(path);
            Assert.Equal(version.Sha256,reopened.Version.Sha256);
            Assert.True(DocumentSession.ContentEquals(loaded.Project,reopened.Project));
            Assert.False(reopened.Project.NestingSnapshot!.IsCurrentFor(reopened.Project));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }

    [Fact]
    public void SmallVisibleLayoutDoesNotDoubleCountLargeHistoricalLayoutOnReopen()
    {
        const int count=8_400;
        var piece=new PatternPiece { Name="Kaynak",Points=[new(0,0),new(2,0),new(4,0),new(6,0),new(6,1),new(6,2),
            new(6,4),new(4,4),new(2,4),new(0,4),new(0,2),new(0,1)],
            Grainline=new Grainline { Start=new(1,1),End=new(1,2) },
            Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=count } };
        var source=new ProjectDocument { Pieces=[piece] };
        var angles=Enumerable.Range(1,count).ToDictionary(i=>(piece.Id,i),_=>0);
        var plan=PerCopyOrthogonalNesting.Arrange(source,[piece.Id],angles,100_000,0,100,0,GrainDirectionPolicy.Direction.TwoWay,0);
        var record=RotatedNestingRecord.Capture(source,[piece.Id],angles,plan,GrainDirectionPolicy.Direction.TwoWay,0,100_000,0,0,100);
        var trace=record.ToTrace();Assert.True(trace.LayoutPieces.Sum(p=>p.Points.Count)>100_000);
        var visible=new PatternPiece { Name="Düzenlenmiş",Points=[new(0,0),new(10,0),new(0,10)] };
        var document=new ProjectDocument { Pieces=[visible],NestingSnapshot=new NestingSnapshot {
            FabricWidth=100_000,Gap=0,EdgeMargin=0,UsedLength=plan.Plan.Nesting.UsedLength,LayoutFingerprint=trace.LayoutFingerprint,
            Placements=plan.Plan.Placements.Select(p=>new NestingPlacementSnapshot(p.SourcePieceId,p.CopyNumber,p.PlacedPieceId)).ToList(),RotationTrace=trace } };
        var path=Path.Combine(AppContext.BaseDirectory,$"rotation-large-history-{Guid.NewGuid():N}.tcad");
        try
        {
            ProjectStorage.Save(document,path);var loaded=ProjectStorage.Load(path);
            Assert.True(DocumentSession.ContentEquals(document,loaded));
            Assert.False(loaded.NestingSnapshot!.IsCurrentFor(loaded));
            var overLimit=document.Clone();overLimit.Pieces=trace.LayoutPieces.Select(p=>p.Clone()).ToList();
            Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(overLimit));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
}
