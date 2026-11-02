using System.Text;
using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Nesting;

namespace TekstilCAD.Tests;

public sealed class PhotoTraceProvenanceTests
{
    private static PhotoTraceProvenance Evidence(Guid id)=>new() { AlgorithmVersion=1,PieceId=id,SourceSha256=new string('A',64),SourceByteLength=100,SourceOrientation=1,
        SourceWidth=20,SourceHeight=20,PreviewWidth=20,PreviewHeight=20,ReferenceStart=new(0,0),ReferenceEnd=new(10,0),ReferenceLengthMm=10,
        TracePixels=[new(0,0),new(10,0)],GeneratedPoints=[new(0,0),new(10,0)] };
    private static ProjectDocument Fixture()
    {
        var piece=new PatternPiece { Closed=false,Points=[new(0,0),new(10,0)] };
        return new ProjectDocument { Pieces=[piece],PhotoTraceHistory=[Evidence(piece.Id)] };
    }
    private static ProjectDocument Parse(JsonNode node)=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(node.ToJsonString()));

    [Fact]
    public void VersionEightRoundTripRetainsDetachedHistoricalEvidence()
    {
        var original=Fixture();var json=ProjectStorage.SerializeProject(original);var root=JsonNode.Parse(json)!;
        Assert.Equal(8,root[0]!.GetValue<int>());
        var restored=Parse(root);Assert.True(DocumentSession.ContentEquals(original,restored));
        var clone=restored.Clone();clone.PhotoTraceHistory![0].TracePixels[0]=new(2,0);
        Assert.False(DocumentSession.ContentEquals(restored,clone));Assert.Equal(new Point2(0,0),restored.PhotoTraceHistory![0].TracePixels[0]);
        restored.Pieces.Clear();ProjectStorage.Validate(restored);
        Assert.True(DocumentSession.ContentEquals(restored,ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(restored)))));
    }

    [Fact]
    public void VersionEightRoundTripPreservesSignedZeroAndRepresentableSubnormalCoordinates()
    {
        var minusZero=BitConverter.Int64BitsToDouble(long.MinValue);var tiny=double.Epsilon;var id=Guid.NewGuid();
        var evidence=Evidence(id);evidence.ReferenceStart=new Point2(minusZero,minusZero);
        evidence.TracePixels=[new Point2(minusZero,tiny),new Point2(10,0)];
        evidence.GeneratedPoints=PhotoTraceCalibration.Convert(evidence.PreviewWidth,evidence.PreviewHeight,evidence.SourceWidth,evidence.SourceHeight,
            evidence.ReferenceStart,evidence.ReferenceEnd,evidence.ReferenceLengthMm,evidence.TracePixels).ToList();
        var document=new ProjectDocument { Pieces=[new PatternPiece { Id=id,Closed=false,Points=[..evidence.GeneratedPoints] }],PhotoTraceHistory=[evidence] };
        ProjectStorage.Validate(document);var json=ProjectStorage.SerializeProject(document);
        var restored=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(json));
        Assert.True(DocumentSession.ContentEquals(document,restored));
        Assert.Equal(long.MinValue,BitConverter.DoubleToInt64Bits(restored.PhotoTraceHistory![0].ReferenceStart.X));
        Assert.Equal(1,BitConverter.DoubleToInt64Bits(restored.PhotoTraceHistory[0].TracePixels[0].Y));
        Assert.Equal(1,BitConverter.DoubleToInt64Bits(restored.PhotoTraceHistory[0].GeneratedPoints[0].Y));
    }

    [Fact]
    public void SessionPreventsSilentHistoryRemovalOrMutationButAllowsAppendAndUndo()
    {
        var source=Fixture();var session=new DocumentSession(source);
        Assert.Throws<InvalidOperationException>(()=>session.Execute(d=>d.PhotoTraceHistory=null,ProjectStorage.Validate));
        Assert.Throws<InvalidOperationException>(()=>session.Execute(d=>d.PhotoTraceHistory![0].SourceSha256=new string('B',64),ProjectStorage.Validate));
        Assert.True(DocumentSession.ContentEquals(source,session.Document));
        Assert.True(session.Execute(d=>d.PhotoTraceHistory!.Add(Evidence(Guid.NewGuid())),ProjectStorage.Validate));
        Assert.Equal(2,session.Document.PhotoTraceHistory!.Count);
        Assert.True(session.Undo());Assert.True(DocumentSession.ContentEquals(source,session.Document));
        Assert.True(session.Redo());Assert.Equal(2,session.Document.PhotoTraceHistory!.Count);
        Assert.True(session.Execute(d=>d.Pieces.Clear(),ProjectStorage.Validate));Assert.Equal(2,session.Document.PhotoTraceHistory!.Count);
    }

    [Fact]
    public void HistoricalPointsAreCountedInUndoSnapshotBudget()
    {
        var source=Fixture();var seven=new DocumentHistory(maximumPoints:7);var eight=new DocumentHistory(maximumPoints:8);
        Assert.False(seven.CanRetainSnapshot(source));Assert.True(eight.CanRetainSnapshot(source));
        eight.Record(source);Assert.Equal(8,eight.StoredPointCount);
    }

    [Fact]
    public void StrictVersionEightRejectsMissingNullUnknownAndMismatchedData()
    {
        var valid=ProjectStorage.SerializeProject(Fixture());
        var missing=JsonNode.Parse(valid)!;missing[1]!.AsObject().Remove("PhotoTraceHistory");Assert.Throws<InvalidDataException>(()=>Parse(missing));
        var empty=JsonNode.Parse(valid)!;empty[1]!["PhotoTraceHistory"]=new JsonArray();Assert.Throws<InvalidDataException>(()=>Parse(empty));
        var nul=JsonNode.Parse(valid)!;nul[1]!["PhotoTraceHistory"]=null;Assert.Throws<InvalidDataException>(()=>Parse(nul));
        var old=JsonNode.Parse(valid)!;old[0]=1;Assert.Throws<InvalidDataException>(()=>Parse(old));
        var absentField=JsonNode.Parse(valid)!;absentField[1]!["PhotoTraceHistory"]![0]!.AsObject().Remove("AlgorithmVersion");Assert.Throws<InvalidDataException>(()=>Parse(absentField));
        var unknown=JsonNode.Parse(valid)!;unknown[1]!["PhotoTraceHistory"]![0]!["Extra"]=1;Assert.Throws<InvalidDataException>(()=>Parse(unknown));
        var invalidVersion=JsonNode.Parse(valid)!;invalidVersion[1]!["PhotoTraceHistory"]![0]!["AlgorithmVersion"]=2;Assert.Throws<InvalidDataException>(()=>Parse(invalidVersion));
        var noOrientation=JsonNode.Parse(valid)!;noOrientation[1]!["PhotoTraceHistory"]![0]!.AsObject().Remove("SourceOrientation");Assert.Throws<InvalidDataException>(()=>Parse(noOrientation));
        var invalidOrientation=JsonNode.Parse(valid)!;invalidOrientation[1]!["PhotoTraceHistory"]![0]!["SourceOrientation"]=9;Assert.Throws<InvalidDataException>(()=>Parse(invalidOrientation));
        var invalidHash=JsonNode.Parse(valid)!;invalidHash[1]!["PhotoTraceHistory"]![0]!["SourceSha256"]=new string('a',64);Assert.Throws<InvalidDataException>(()=>Parse(invalidHash));
        var invalidBytes=JsonNode.Parse(valid)!;invalidBytes[1]!["PhotoTraceHistory"]![0]!["SourceByteLength"]=0;Assert.Throws<InvalidDataException>(()=>Parse(invalidBytes));
        var invalidSize=JsonNode.Parse(valid)!;invalidSize[1]!["PhotoTraceHistory"]![0]!["SourceWidth"]=1;Assert.Throws<InvalidDataException>(()=>Parse(invalidSize));
        var invalidPoint=JsonNode.Parse(valid)!;invalidPoint[1]!["PhotoTraceHistory"]![0]!["ReferenceStart"]!.AsObject().Remove("X");Assert.Throws<InvalidDataException>(()=>Parse(invalidPoint));
        var changed=JsonNode.Parse(valid)!;changed[1]!["PhotoTraceHistory"]![0]!["GeneratedPoints"]![1]!["X"]=11;Assert.Throws<InvalidDataException>(()=>Parse(changed));
        var duplicate=valid.Replace("\"AlgorithmVersion\": 1", "\"AlgorithmVersion\": 1, \"AlgorithmVersion\": 1",StringComparison.Ordinal);
        Assert.NotEqual(valid,duplicate);Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(duplicate)));
    }

    [Fact]
    public void VersionEightCanContainExistingVersionSevenRotationEvidence()
    {
        var piece=new PatternPiece { Points=[new(0,0),new(6,0),new(6,4),new(0,4)],Metadata=new PieceMetadata { Role="Kesim",Material="Pamuk",Quantity=2 },
            Grainline=new Grainline { Start=new(1,1),End=new(1,2) },Notches=[new NotchMark { VertexIndex=1,Position=new(6,0) }] };
        var source=new ProjectDocument { Pieces=[piece] };var angles=new Dictionary<(Guid,int),int> { [(piece.Id,1)]=0,[(piece.Id,2)]=90 };
        var result=PerCopyOrthogonalNesting.Arrange(source,[piece.Id],angles,100,1,100,0,GrainDirectionPolicy.Direction.TwoWay,90);
        var record=RotatedNestingRecord.Capture(source,[piece.Id],angles,result,GrainDirectionPolicy.Direction.TwoWay,90,100,1,0,100);
        var trace=record.ToTrace();var layout=result.Plan.Nesting.Layout.Clone();
        layout.NestingSnapshot=new NestingSnapshot { FabricWidth=100,Gap=1,EdgeMargin=0,UsedLength=result.Plan.Nesting.UsedLength,
            LayoutFingerprint=trace.LayoutFingerprint,Placements=result.Plan.Placements.Select(p=>new NestingPlacementSnapshot(p.SourcePieceId,p.CopyNumber,p.PlacedPieceId)).ToList(),RotationTrace=trace };
        layout.PhotoTraceHistory=[Evidence(Guid.NewGuid())];
        var json=ProjectStorage.SerializeProject(layout);Assert.Equal(8,JsonNode.Parse(json)![0]!.GetValue<int>());
        Assert.True(DocumentSession.ContentEquals(layout,ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(json))));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingVersionEightPhysicalFeaturesAreRejectedWithoutChangingFile(bool withPhotoHistory)
    {
        // The other experimental schema stores PhysicalFeatures on a piece in
        // version 8. Keep the fixture independent of that live worktree.
        var root=JsonNode.Parse(ProjectStorage.SerializeProject(Fixture()))!;
        var piece=root[1]!["Pieces"]![0]!;
        piece["PhysicalFeatures"]=new JsonObject {
            ["OwnerPieceId"]=piece["Id"]!.GetValue<string>(),
            ["SourcePoints"]=piece["Points"]!.DeepClone(),
            ["EdgeIds"]=new JsonArray(Guid.NewGuid().ToString(),Guid.NewGuid().ToString()),
            ["Notches"]=new JsonArray() };
        if(!withPhotoHistory)root[1]!.AsObject().Remove("PhotoTraceHistory");
        var path=Path.Combine(AppContext.BaseDirectory,"foreign-v8-"+Guid.NewGuid().ToString("N")+".tcad");
        try
        {
            var bytes=Encoding.UTF8.GetBytes(root.ToJsonString());File.WriteAllBytes(path,bytes);
            var error=Assert.Throws<InvalidDataException>(()=>ProjectStorage.LoadWithVersion(path));
            Assert.Contains("fiziksel kontur",error.Message,StringComparison.OrdinalIgnoreCase);
            Assert.Equal(bytes,File.ReadAllBytes(path));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Fact]
    public void UnsupportedPhysicalFeaturePresenceIsRejectedEvenWhenNullOrUnversioned()
    {
        var root=JsonNode.Parse(ProjectStorage.SerializeProject(Fixture()))!;
        root[1]!["Pieces"]![0]!["PhysicalFeatures"]=null;
        var error=Assert.Throws<InvalidDataException>(()=>Parse(root));
        Assert.Contains("fiziksel kontur",error.Message,StringComparison.OrdinalIgnoreCase);
        var unversioned=root[1]!.DeepClone();unversioned.AsObject().Remove("PhotoTraceHistory");
        error=Assert.Throws<InvalidDataException>(()=>Parse(unversioned));
        Assert.Contains("fiziksel kontur",error.Message,StringComparison.OrdinalIgnoreCase);
    }
}
