using System.Text;
using System.Text.Json.Nodes;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class PhotoPlaneProvenanceTests
{
    private static ProjectDocument Fixture()
    {
        Point2[] corners=[new(10,10),new(90,10),new(80,90),new(20,90)];
        Point2[] trace=[new(25,25),new(70,25),new(70,70)];
        var points=PhotoPlaneRectification.Convert(100,100,corners,200,150,trace).ToList();
        var piece=new PatternPiece { Closed=false,Points=points.ToList() };
        var history=new PhotoTraceProvenance { AlgorithmVersion=2,PieceId=piece.Id,SourceSha256=new string('A',64),SourceByteLength=500,SourceOrientation=1,
            SourceWidth=200,SourceHeight=200,PreviewWidth=100,PreviewHeight=100,ReferenceStart=corners[0],ReferenceEnd=corners[1],ReferenceLengthMm=200,
            PlaneCalibration=new PhotoPlaneCalibration { Corners=corners.ToList(),WidthMm=200,HeightMm=150 },TracePixels=trace.ToList(),GeneratedPoints=points.ToList() };
        return new ProjectDocument { Pieces=[piece],PhotoTraceHistory=[history] };
    }
    private static ProjectDocument Parse(JsonNode node)=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(node.ToJsonString()));

    [Fact]
    public void VersionNineRoundTripsPlaneAndDetachedClone()
    {
        var original=Fixture();var json=ProjectStorage.SerializeProject(original);var node=JsonNode.Parse(json)!;
        Assert.Equal(9,node[0]!.GetValue<int>());var restored=Parse(node);
        Assert.True(DocumentSession.ContentEquals(original,restored));
        var clone=restored.Clone();clone.PhotoTraceHistory![0].PlaneCalibration!.Corners[0]=new(11,10);
        Assert.False(DocumentSession.ContentEquals(restored,clone));Assert.Equal(new Point2(10,10),restored.PhotoTraceHistory![0].PlaneCalibration!.Corners[0]);
    }
    [Fact]
    public void VersionEightCannotSilentlyContainPlaneHistory()
    {
        var node=JsonNode.Parse(ProjectStorage.SerializeProject(Fixture()))!;node[0]=8;
        Assert.Throws<InvalidDataException>(()=>Parse(node));
    }
    [Fact]
    public void MixedOldAndPlaneRecordsRoundTripWithoutChangingTheOldAlgorithm()
    {
        var document=Fixture();var legacy=new PhotoTraceProvenance { AlgorithmVersion=1,PieceId=Guid.NewGuid(),SourceSha256=new string('B',64),SourceByteLength=50,SourceOrientation=1,
            SourceWidth=20,SourceHeight=20,PreviewWidth=20,PreviewHeight=20,ReferenceStart=new(0,0),ReferenceEnd=new(10,0),ReferenceLengthMm=10,
            TracePixels=[new(0,0),new(10,0)],GeneratedPoints=[new(0,0),new(10,0)] };
        document.PhotoTraceHistory!.Insert(0,legacy);var node=JsonNode.Parse(ProjectStorage.SerializeProject(document))!;
        Assert.Equal(9,node[0]!.GetValue<int>());Assert.False(node[1]!["PhotoTraceHistory"]![0]!.AsObject().ContainsKey("PlaneCalibration"));
        var restored=Parse(node);Assert.True(DocumentSession.ContentEquals(document,restored));Assert.Equal(1,restored.PhotoTraceHistory![0].AlgorithmVersion);
        document.PhotoTraceHistory.RemoveAt(1);var old=JsonNode.Parse(ProjectStorage.SerializeProject(document))!;Assert.Equal(8,old[0]!.GetValue<int>());
        old[0]=9;Assert.Throws<InvalidDataException>(()=>Parse(old));
    }
    [Fact]
    public void VersionNineRejectsMissingUnknownTamperedAndMismatchedPlaneData()
    {
        var valid=ProjectStorage.SerializeProject(Fixture());
        var missing=JsonNode.Parse(valid)!;missing[1]!["PhotoTraceHistory"]![0]!.AsObject().Remove("PlaneCalibration");Assert.Throws<InvalidDataException>(()=>Parse(missing));
        var unknown=JsonNode.Parse(valid)!;unknown[1]!["PhotoTraceHistory"]![0]!["PlaneCalibration"]!["Extra"]=1;Assert.Throws<InvalidDataException>(()=>Parse(unknown));
        var shortCorners=JsonNode.Parse(valid)!;shortCorners[1]!["PhotoTraceHistory"]![0]!["PlaneCalibration"]!["Corners"]!.AsArray().RemoveAt(3);Assert.Throws<InvalidDataException>(()=>Parse(shortCorners));
        var changed=JsonNode.Parse(valid)!;changed[1]!["PhotoTraceHistory"]![0]!["GeneratedPoints"]![1]!["X"]=123;Assert.Throws<InvalidDataException>(()=>Parse(changed));
        var changedWidth=JsonNode.Parse(valid)!;changedWidth[1]!["PhotoTraceHistory"]![0]!["PlaneCalibration"]!["WidthMm"]=201;Assert.Throws<InvalidDataException>(()=>Parse(changedWidth));
        var changedReference=JsonNode.Parse(valid)!;changedReference[1]!["PhotoTraceHistory"]![0]!["ReferenceStart"]!["X"]=11;Assert.Throws<InvalidDataException>(()=>Parse(changedReference));
        var oldAlgorithm=JsonNode.Parse(valid)!;oldAlgorithm[1]!["PhotoTraceHistory"]![0]!["AlgorithmVersion"]=1;Assert.Throws<InvalidDataException>(()=>Parse(oldAlgorithm));
    }
    [Fact]
    public void PlaneHistoryCannotBeSilentlyChangedAndCountsInUndoBudget()
    {
        var original=Fixture();var session=new DocumentSession(original);
        Assert.Throws<InvalidOperationException>(()=>session.Execute(d=>
        {
            var record=d.PhotoTraceHistory![0];record.PlaneCalibration!.HeightMm=151;
            record.GeneratedPoints=PhotoPlaneRectification.Convert(record.PreviewWidth,record.PreviewHeight,record.PlaneCalibration.Corners,
                record.PlaneCalibration.WidthMm,record.PlaneCalibration.HeightMm,record.TracePixels).ToList();
        },ProjectStorage.Validate));
        Assert.True(DocumentSession.ContentEquals(original,session.Document));
        // Three geometry points, three trace points, three output points, two reference points and four plane corners.
        Assert.False(new DocumentHistory(maximumPoints:14).CanRetainSnapshot(original));
        Assert.True(new DocumentHistory(maximumPoints:15).CanRetainSnapshot(original));
    }
}
