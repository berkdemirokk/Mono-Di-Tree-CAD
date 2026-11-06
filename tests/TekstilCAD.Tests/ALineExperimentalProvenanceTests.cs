using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;
using TekstilCAD.Nesting;
using TekstilCAD.Grading;
using TekstilCAD.MachineGateway;
using System.Text;
using System.Text.Json.Nodes;

namespace TekstilCAD.Tests;

public sealed class ALineExperimentalProvenanceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void ExistingMarkerFreeProjectVersionsStillRoundTrip(int version)
    {
        var document=Document();document.Pieces[0].ExperimentalALine=null;
        var payload=ProjectStorage.SerializeProject(document);
        var old=version==0 ? payload : $"[{version},{payload}]";
        var loaded=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(old));
        Assert.True(DocumentSession.ContentEquals(document,loaded));
        Assert.True(DocumentSession.ContentEquals(loaded,ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(loaded)))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionTenRetainsBothKindsOfPhotoEvidence(bool plane)
    {
        Point2[] corners=[new(10,10),new(90,10),new(80,90),new(20,90)];
        Point2[] pixels=[new(25,25),new(70,25),new(70,70)];
        var points=(plane ? PhotoPlaneRectification.Convert(100,100,corners,200,150,pixels) :
            PhotoTraceCalibration.Convert(100,100,200,200,corners[0],corners[1],200,pixels)).ToList();
        var photo=new PatternPiece { Closed=false,Points=points.ToList() };
        var history=new PhotoTraceProvenance { AlgorithmVersion=plane?2:1,PieceId=photo.Id,SourceSha256=new string('B',64),SourceByteLength=500,SourceOrientation=1,
            SourceWidth=200,SourceHeight=200,PreviewWidth=100,PreviewHeight=100,ReferenceStart=corners[0],ReferenceEnd=corners[1],ReferenceLengthMm=200,
            PlaneCalibration=plane ? new PhotoPlaneCalibration { Corners=corners.ToList(),WidthMm=200,HeightMm=150 } : null,
            TracePixels=pixels.ToList(),GeneratedPoints=points.ToList() };
        var project=Document();project.Pieces.Add(photo);project.PhotoTraceHistory=[history];
        var json=ProjectStorage.SerializeProject(project);var parsed=JsonNode.Parse(json)!;
        Assert.Equal(10,parsed[0]!.GetValue<int>());
        var restored=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(json));
        Assert.True(DocumentSession.ContentEquals(project,restored));
        Assert.Equal(plane?2:1,Assert.Single(restored.PhotoTraceHistory!).AlgorithmVersion);
        restored.PhotoTraceHistory![0].GeneratedPoints[0]=new Point2(999,999);
        Assert.False(DocumentSession.ContentEquals(project,restored));
    }

    [Fact]
    public void MarkerShapeIsStrictIncludingNullAndDuplicateKeys()
    {
        var json=ProjectStorage.SerializeProject(Document());
        var root=JsonNode.Parse(json)!;
        var marker=root[1]!["Pieces"]![0]!["ExperimentalALine"]!.AsObject();
        var compact=root.ToJsonString();var prefix=marker.ToJsonString();
        foreach(var invalid in new[] { "null",prefix.Replace("\"Side\":", "\"Extra\":1,\"Side\":"),prefix.Replace("\"Side\":", "\"Side\":\"front\",\"Side\":") })
        {
            var changed=compact.Replace(prefix,invalid,StringComparison.Ordinal);
            Assert.NotEqual(compact,changed);
            Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(changed)));
        }
        var versionWithoutMarker=JsonNode.Parse(json)!;versionWithoutMarker[1]!["Pieces"]![0]!.AsObject().Remove("ExperimentalALine");
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(versionWithoutMarker.ToJsonString())));
    }

    private static ProjectDocument Document()=>new() { Name="Deneysel",Pieces=[new PatternPiece { Name="Ön taslak",Closed=true,
        Points=[new(0,0),new(100,0),new(100,200),new(0,200)],
        ExperimentalALine=new(ALineExperimentalProvenance.CurrentAlgorithm,new string('A',64),"front") }] };
    [Fact] public void MarkerSurvivesCloneTransactionAndUndoRedo()
    {
        var original=Document();var clone=original.Clone();Assert.Equal(original.Pieces[0].ExperimentalALine,clone.Pieces[0].ExperimentalALine);
        Assert.True(DocumentSession.ContentEquals(original,clone));clone.Pieces[0].ExperimentalALine=null;Assert.False(DocumentSession.ContentEquals(original,clone));
        var session=new DocumentSession(original);
        Assert.True(session.Execute(d=> { var p=d.Pieces[0];p.Points=p.Points.Select(x=>new Point2(x.X+5,x.Y)).ToList(); },ProjectStorage.Validate));
        Assert.NotNull(session.Document.Pieces[0].ExperimentalALine);Assert.True(session.Undo());Assert.NotNull(session.Document.Pieces[0].ExperimentalALine);
        Assert.True(session.Redo());Assert.NotNull(session.Document.Pieces[0].ExperimentalALine);
        Assert.Throws<InvalidOperationException>(()=>ExperimentalOutputGuard.EnsureAllowed(session.Document.Pieces));
    }
    [Fact] public void DerivationsRetainRestrictionAndIncompatibleSourcesCannotMerge()
    {
        var closed=Document().Pieces[0];var provenance=closed.ExperimentalALine;
        Assert.Equal(provenance,OrthogonalRotation.Rotate(closed,90).ExperimentalALine);
        Assert.Equal(provenance,GradingEngine.Generate(closed,Enumerable.Repeat(new Point2(0,0),closed.Points.Count).ToArray(),1,"M").ExperimentalALine);
        var path=ContourPathExtraction.Extract(closed,0,3).Piece;Assert.Equal(provenance,path.ExperimentalALine);
        var split=OpenPathSplit.Split(path,1);Assert.Equal(provenance,split.First.ExperimentalALine);Assert.Equal(provenance,split.Second.ExperimentalALine);
        var joined=OpenPathJoin.Join(split.First,split.Second,0).Piece;Assert.Equal(provenance,joined.ExperimentalALine);
        Assert.Equal(provenance,OpenPathClosure.Close(joined,200).Piece.ExperimentalALine);
        Assert.Equal(provenance,ALineExperimentalProvenance.Merge(null,provenance));
        Assert.Throws<InvalidOperationException>(()=>ALineExperimentalProvenance.Merge(provenance,provenance! with { Side="back" }));
    }
    [Fact] public void DerivedPatternToolCannotLoseMarkerBeforeSelectedExport()
    {
        var source=Document().Pieces[0];var changed=PatternTools.InsertEdgeMidpoint(source,0);
        Assert.Equal(source.ExperimentalALine,changed.ExperimentalALine);
        var folder=Path.Combine(Path.GetTempPath(),"aline-derived-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try { var path=Path.Combine(folder,"selected.dxf");Assert.Throws<InvalidOperationException>(()=>DxfFormat.Export(new ProjectDocument { Pieces=[changed] },path));Assert.False(File.Exists(path)); }
        finally { Directory.Delete(folder,true); }
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    public void NestingFingerprintTracksMarkerAndKeepsUnmarkedLegacyDomain(bool grain,bool notch)
    {
        var doc=Document();var piece=doc.Pieces[0];var marker=piece.ExperimentalALine!;piece.ExperimentalALine=null;
        if(grain)piece.Grainline=new Grainline { Start=new(10,10),End=new(10,50) };
        if(notch)piece.Notches=[new NotchMark { VertexIndex=0,Position=piece.Points[0] }];
        var legacy=NestingSnapshot.ComputeLayoutFingerprint(doc);
        var snapshot=new NestingSnapshot { LayoutFingerprint=legacy,Placements=[new NestingPlacementSnapshot(Guid.NewGuid(),1,piece.Id)] };
        Assert.True(snapshot.IsCurrentFor(doc));piece.ExperimentalALine=marker;
        Assert.False(snapshot.IsCurrentFor(doc));var marked=NestingSnapshot.ComputeLayoutFingerprint(doc);Assert.NotEqual(legacy,marked);
        snapshot.LayoutFingerprint=marked;Assert.True(snapshot.IsCurrentFor(doc));
        piece.ExperimentalALine=marker with { Side="back" };Assert.False(snapshot.IsCurrentFor(doc));
        piece.ExperimentalALine=marker with { SourceSha256=new string('B',64) };Assert.False(snapshot.IsCurrentFor(doc));
        piece.ExperimentalALine=null;Assert.Equal(legacy,NestingSnapshot.ComputeLayoutFingerprint(doc));
    }
    [Fact] public void DirectOutputAndSimulationRejectRestrictedGeometryBeforeWriting()
    {
        var folder=Path.Combine(Path.GetTempPath(),"aline-output-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var doc=Document();var before=doc.Clone();var target=Path.Combine(folder,"out");
            Assert.Throws<InvalidOperationException>(()=>DxfFormat.Export(doc,target+".dxf"));
            Assert.Throws<InvalidOperationException>(()=>SvgFormat.Export(doc,target+".svg"));
            Assert.Throws<InvalidOperationException>(()=>HpglFormat.Prepare(doc));
            Assert.Throws<InvalidOperationException>(()=>HpglFormat.Export(doc,target+".plt"));
            Assert.Throws<InvalidOperationException>(()=>PdfFormat.Export(doc,target+".pdf",new ProjectFileVersion(target+".pdf",null)));
            Assert.Throws<InvalidOperationException>(()=>PdfTiledFormat.Export(doc,target+"-tiles.pdf",new ProjectFileVersion(target+"-tiles.pdf",null)));
            Assert.Throws<InvalidOperationException>(()=>DxfGrainlineReviewExport.Prepare(doc));
            Assert.Throws<InvalidOperationException>(()=>DxfNotchReviewExport.Prepare(doc,false));
            var queue=new SimulationQueue(Path.Combine(folder,"spool"));var profile=new MachineProfile("local");
            Assert.Throws<InvalidOperationException>(()=>queue.PrepareHpglReview(doc,profile));
            Assert.Throws<InvalidOperationException>(()=>queue.Prepare(doc,profile,JobFormat.Dxf));
            Assert.Throws<InvalidOperationException>(()=>queue.Prepare(doc,profile,JobFormat.Hpgl));
            Assert.True(DocumentSession.ContentEquals(before,doc));Assert.False(File.Exists(target+".dxf"));Assert.False(File.Exists(target+".plt"));
            Assert.False(File.Exists(target+".svg"));Assert.False(File.Exists(target+".pdf"));Assert.Empty(queue.Jobs);
        }
        finally { Directory.Delete(folder,true); }
    }
    [Fact] public void DxfExportGuardsAndWritesOneDetachedSnapshot()
    {
        var folder=Path.Combine(Path.GetTempPath(),"aline-dxf-snapshot-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var source=Document();var marker=source.Pieces[0].ExperimentalALine;source.Pieces[0].ExperimentalALine=null;
            var initial=source.Pieces[0].Points.ToArray();var safePath=Path.Combine(folder,"captured.dxf");
            DxfFormat.ExportForVerification(source,safePath,()=> { source.Pieces[0].ExperimentalALine=marker;source.Pieces[0].Points[1]=new Point2(90,0); });
            var output=DxfFormat.Import(safePath,1).Project;Assert.Single(output.Pieces);Assert.Equal(initial,output.Pieces[0].Points);
            var blocked=Path.Combine(folder,"blocked.dxf");
            Assert.Throws<InvalidOperationException>(()=>DxfFormat.ExportForVerification(source,blocked,()=>source.Pieces[0].ExperimentalALine=null));
            Assert.False(File.Exists(blocked));
        }
        finally { Directory.Delete(folder,true); }
    }
    [Fact] public void SelectedDxfReviewChecksIncludedPiecesWithoutBlockingUnselectedDraft()
    {
        var doc=Document();var restricted=doc.Pieces[0];restricted.Notches=[new NotchMark { VertexIndex=0,Position=restricted.Points[0] }];
        var normal=restricted.Clone();normal.Id=Guid.NewGuid();normal.Name="Normal";normal.ExperimentalALine=null;doc.Pieces.Add(normal);
        Assert.Throws<InvalidOperationException>(()=>DxfNotchReviewExport.PrepareSelected(doc,[restricted.Id],false));
        var safe=DxfNotchReviewExport.PrepareSelected(doc,[normal.Id],false);
        Assert.Single(safe.Contours);Assert.Equal(normal.Id,safe.Contours[0].PieceId);
    }
    [Fact] public void PreparedSimulationCannotCommitAfterSourceGainsRestriction()
    {
        var folder=Path.Combine(Path.GetTempPath(),"aline-job-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var doc=Document();var marker=doc.Pieces[0].ExperimentalALine;doc.Pieces[0].ExperimentalALine=null;
            var queue=new SimulationQueue(folder);var profile=new MachineProfile("local");var prepared=queue.PrepareHpglReview(doc,profile);
            doc.Pieces[0].ExperimentalALine=marker;
            Assert.Throws<InvalidOperationException>(()=>queue.CommitPreparedHpgl(prepared,doc,profile));Assert.Empty(queue.Jobs);
        }
        finally { Directory.Delete(folder,true); }
    }
    [Fact] public void VersionedRoundTripRejectsDowngradeAndIncompleteOrUnknownMarker()
    {
        var folder=Path.Combine(Path.GetTempPath(),"aline-provenance-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,"project.tcad");
        try
        {
            var original=Document();ProjectStorage.Save(original,path);var json=File.ReadAllText(path);
            Assert.Equal(10,ProjectStorage.GetProjectFormatVersion(original));Assert.True(DocumentSession.ContentEquals(original,ProjectStorage.Load(path)));
            var bad=Path.Combine(folder,"bad.tcad");
            var versionOffset=json.IndexOf("10,",StringComparison.Ordinal);Assert.True(versionOffset>=0);var downgraded=json.Remove(versionOffset,2).Insert(versionOffset,"9");
            foreach(var text in new[]{downgraded,json.Replace("\"front\"","\"unknown\""),json.Replace("\"SourceSha256\"","\"Unknown\"")})
            { Assert.NotEqual(json,text);File.WriteAllText(bad,text);Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(bad)); }
            original.Pieces[0].ExperimentalALine=null;Assert.Equal(0,ProjectStorage.GetProjectFormatVersion(original));
            ExperimentalOutputGuard.EnsureAllowed(original.Pieces);
        }
        finally { Directory.Delete(folder,true); }
    }
}
