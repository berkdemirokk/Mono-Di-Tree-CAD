using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ALineProjectEvidenceTests
{
    private static (ProjectDocument Document,ALineProjectEvidence Evidence) Fixture()
    {
        var input=new ALinePanelInput(360,380,500,520,40,40,40,40,200,700,1600);
        var candidate=ALineSideBalance.Propose(input).Candidate!;var preview=ALinePanelPreview.Create(candidate);
        double[] values=[360,380,500,520,40,40,40,40,200,700,1600];
        double[] ease=[candidate.FrontWaistEase,candidate.BackWaistEase,candidate.FrontHipEase,candidate.BackHipEase];
        static double[] Flat(IReadOnlyList<Point2> points)=>points.SelectMany(p=>new[]{p.X,p.Y}).ToArray();
        var frontId=Guid.NewGuid();var backId=Guid.NewGuid();var source=new string('A',64);
        var original=ALineCandidateEvidenceStorage.Create(frontId,backId,source,values,[40,40,40,40],ease,Flat(preview.Front),Flat(preview.Back));
        var evidence=ALineProjectEvidenceStorage.Create(original,[0,0,1000,0]);
        static List<Point2> Points(double[] xy)=>Enumerable.Range(0,5).Select(i=>new Point2(xy[i*2],xy[i*2+1])).ToList();
        var project=new ProjectDocument { ALineEvidence=[evidence],Pieces=[
            new PatternPiece { Id=frontId,Name="Ön",Closed=true,Points=Points(evidence.FrontPlacedCoordinatesMm),ExperimentalALine=new(ALineExperimentalProvenance.CurrentAlgorithm,source,"front") },
            new PatternPiece { Id=backId,Name="Arka",Closed=true,Points=Points(evidence.BackPlacedCoordinatesMm),ExperimentalALine=new(ALineExperimentalProvenance.CurrentAlgorithm,source,"back") }] };
        return(project,evidence);
    }
    [Fact] public void V11RoundtripPreservesEvidenceAndSeparatePlacement()
    {
        var (project,evidence)=Fixture();Assert.True(ALineProjectEvidenceStorage.Matches(evidence,project));
        var text=ProjectStorage.SerializeProject(project);Assert.Equal(11,System.Text.Json.JsonDocument.Parse(text).RootElement[0].GetInt32());
        var restored=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(text));
        Assert.True(DocumentSession.ContentEquals(project,restored));Assert.True(ALineProjectEvidenceStorage.Matches(restored.ALineEvidence![0],restored));
        var legacy=project.Clone();legacy.ALineEvidence=null;Assert.Equal(10,ProjectStorage.GetProjectFormatVersion(legacy));
        Assert.Equal(10,System.Text.Json.JsonDocument.Parse(ProjectStorage.SerializeProject(legacy)).RootElement[0].GetInt32());
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(text.Replace("11,","10,"))));
    }
    [Fact] public void PlacementAndIntegrityTamperingRejectedAndEditingMakesEvidenceStale()
    {
        var (project,evidence)=Fixture();var changed=evidence.Copy();changed.TranslationMm[2]+=1;
        Assert.Throws<InvalidDataException>(()=>ALineProjectEvidenceStorage.Validate(changed));
        var native=ALineCandidateEvidenceStorage.Deserialize(evidence.CandidateEvidenceJson);
        Assert.Throws<InvalidDataException>(()=>ALineProjectEvidenceStorage.Create(native,[0,0,0,0]));
        project.Pieces[0].Points[0]=new Point2(1,0);Assert.False(ALineProjectEvidenceStorage.Matches(evidence,project));ProjectStorage.Validate(project);
        project.Pieces[0].ExperimentalALine=null;Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(project));
    }
    [Fact] public void SnapshotIsDetachedAndMalformedProjectRejected()
    {
        var (project,evidence)=Fixture();var snapshot=project.Clone();evidence.FrontPlacedCoordinatesMm[0]+=1;
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(project));ProjectStorage.Validate(snapshot);
        var json=ProjectStorage.SerializeProject(snapshot);
        Assert.Throws<InvalidDataException>(()=>ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(json.Replace("\"TranslationMm\"","\"BadTranslation\""))));
        snapshot.ALineEvidence!.Add(snapshot.ALineEvidence[0].Copy());Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(snapshot));
    }
    [Fact] public void WholeEvidenceAndTwoPanelsShareOneUndo()
    {
        var (project,_)=Fixture();var session=new DocumentSession();
        Assert.True(session.Execute(d=> { d.Pieces=project.Pieces.Select(p=>p.Clone()).ToList();d.ALineEvidence=project.ALineEvidence!.Select(e=>e.Copy()).ToList(); },ProjectStorage.Validate));
        Assert.Equal(1,session.UndoCount);Assert.True(session.Undo());Assert.Empty(session.Document.Pieces);Assert.Null(session.Document.ALineEvidence);
        Assert.True(session.Redo());Assert.True(DocumentSession.ContentEquals(project,session.Document));
        Assert.Throws<InvalidOperationException>(()=>ExperimentalOutputGuard.EnsureAllowed(session.Document.Pieces));
    }    [Fact] public void AtomicSaveReopensV11AndInvalidEvidenceDoesNotOverwrite()
    {
        var (project,evidence)=Fixture();var path=Path.Combine(Path.GetTempPath(),$"aline-{Guid.NewGuid():N}.tcad");
        try
        {
            ProjectStorage.Save(project,path);var original=File.ReadAllBytes(path);var restored=ProjectStorage.Load(path);
            Assert.True(DocumentSession.ContentEquals(project,restored));
            evidence.TranslationMm[0]+=1;
            Assert.Throws<InvalidDataException>(()=>ProjectStorage.Save(project,path));Assert.Equal(original,File.ReadAllBytes(path));
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }    [Fact] public void UndoBudgetIncludesEvidenceAndChargesAcrossUndoAndRedo()
    {
        var (project,evidence)=Fixture();
        var bytes=256L+2L*evidence.CandidateEvidenceJson.Length+2L*evidence.IntegritySha256.Length+
            8L*(evidence.TranslationMm.LongLength+evidence.FrontPlacedCoordinatesMm.LongLength+evidence.BackPlacedCoordinatesMm.LongLength);
        var units=10+(bytes+15)/16;
        var tooSmall=new DocumentSession(project,maximumPoints:units-1);
        Assert.False(tooSmall.CanRetainCurrentUndoSnapshot);
        var exact=new DocumentSession(project,maximumPoints:units);
        Assert.True(exact.CanRetainCurrentUndoSnapshot);
        Assert.True(exact.Execute(d=>d.Name="edited",ProjectStorage.Validate));Assert.Equal(units,exact.StoredPointCount);
        Assert.True(exact.Undo());Assert.Equal(units,exact.StoredPointCount);
        Assert.True(exact.Redo());Assert.Equal(units,exact.StoredPointCount);
        var history=new DocumentHistory(maximumPoints:units);
        history.Record(project);history.Record(project);Assert.Equal(1,history.UndoCount);Assert.Equal(units,history.StoredPointCount);Assert.Equal(1,history.CapacityDiscardedSnapshots);
    }
    [Fact] public void PlacementRejectsTouchingAndNonFiniteOffsets()
    {
        var (_,evidence)=Fixture();var native=ALineCandidateEvidenceStorage.Deserialize(evidence.CandidateEvidenceJson);
        Assert.Throws<InvalidDataException>(()=>ALineProjectEvidenceStorage.Create(native,[0,0,400,0]));
        Assert.Throws<InvalidDataException>(()=>ALineProjectEvidenceStorage.Create(native,[0,0,double.PositiveInfinity,0]));
        ALineProjectEvidenceStorage.Validate(ALineProjectEvidenceStorage.Create(native,[0,0,401,0]));
    }    [Fact] public void TargetPreflightRejectsBothPanelsTouchingExistingPiecesAndDuplicateIds()
    {
        var (project,evidence)=Fixture();
        static PatternPiece Box(double x)=>new() { Name="Mevcut",Closed=true,Points=[new(x,0),new(x+10,0),new(x+10,10),new(x,10)] };
        var target=new ProjectDocument { Pieces=[Box(2000)] };
        ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target);
        target.Pieces=[Box(400)];Assert.Throws<InvalidOperationException>(()=>ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target));
        target.Pieces=[Box(1000)];Assert.Throws<InvalidOperationException>(()=>ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target));
        target.Pieces=[Box(2000)];target.Pieces[0].Id=project.Pieces[0].Id;
        Assert.Throws<InvalidOperationException>(()=>ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target));
        target.Pieces[0].Id=Guid.NewGuid();target.ALineEvidence=[evidence.Copy()];Assert.Throws<InvalidOperationException>(()=>ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target));
        target.ALineEvidence=null;target.Pieces[0].Grainline=new() { Start=new(2000,1),End=new(400,1) };
        Assert.Throws<InvalidOperationException>(()=>ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target));
    }
    [Fact] public void EditedDeletedPanelsAndHistoryOnlyUndoRemainStaleUntilRestored()
    {
        var (project,_)=Fixture();var session=new DocumentSession(project);
        Assert.True(session.Execute(d=>d.Pieces[0].Points[0]=new Point2(1,0),ProjectStorage.Validate));
        Assert.False(ALineProjectEvidenceStorage.Matches(session.Document.ALineEvidence![0],session.Document));
        Assert.True(session.Execute(d=>d.Pieces.Clear(),ProjectStorage.Validate));
        Assert.False(ALineProjectEvidenceStorage.Matches(session.Document.ALineEvidence![0],session.Document));
        Assert.Equal(11,ProjectStorage.GetProjectFormatVersion(session.Document));
        var restored=ProjectStorage.DeserializeProject(Encoding.UTF8.GetBytes(ProjectStorage.SerializeProject(session.Document)));
        Assert.False(ALineProjectEvidenceStorage.Matches(restored.ALineEvidence![0],restored));
        Assert.True(session.Undo());Assert.False(ALineProjectEvidenceStorage.Matches(session.Document.ALineEvidence![0],session.Document));
        Assert.True(session.Undo());Assert.True(ALineProjectEvidenceStorage.Matches(session.Document.ALineEvidence![0],session.Document));
        Assert.True(session.Redo());Assert.False(ALineProjectEvidenceStorage.Matches(session.Document.ALineEvidence![0],session.Document));
        Assert.True(session.Redo());Assert.Empty(session.Document.Pieces);Assert.NotNull(session.Document.ALineEvidence);
    }
    [Fact] public void MaximumHistoryOnlyEvidenceSurvivesUndoAndCountsTowardBudget()
    {
        var (_,evidence)=Fixture();var candidate=ALineCandidateEvidenceStorage.Deserialize(evidence.CandidateEvidenceJson);
        var entries=Enumerable.Range(0,128).Select(_=>ALineProjectEvidenceStorage.Create(
            ALineCandidateEvidenceStorage.Create(Guid.NewGuid(),Guid.NewGuid(),candidate.SourceSha256,candidate.InputsMm,candidate.OriginalEaseMm,candidate.CandidateEaseMm,candidate.FrontCoordinatesMm,candidate.BackCoordinatesMm),[0,0,1000,0])).ToList();
        var project=new ProjectDocument { ALineEvidence=entries };ProjectStorage.Validate(project);
        var history=new DocumentHistory();history.Record(project);Assert.True(history.StoredPointCount>128*24/2);
        var session=new DocumentSession(project);Assert.True(session.Execute(d=>d.Name="changed",ProjectStorage.Validate));
        Assert.True(session.Undo());Assert.Equal(128,session.Document.ALineEvidence!.Count);
        Assert.True(session.Redo());Assert.Equal(128,session.Document.ALineEvidence!.Count);
        Assert.All(session.Document.ALineEvidence!,entry=>Assert.False(ALineProjectEvidenceStorage.Matches(entry,session.Document)));
        Assert.Throws<InvalidDataException>(()=> { project.ALineEvidence!.Add(entries[0].Copy());ProjectStorage.Validate(project); });
    }    [Fact] public void DeletedPhotoAndDxfHistoryIdsCannotBeReusedForEitherPanel()
    {
        var (_,evidence)=Fixture();var candidate=ALineCandidateEvidenceStorage.Deserialize(evidence.CandidateEvidenceJson);
        static PhotoTraceProvenance Photo(Guid id)=>new() { AlgorithmVersion=1,PieceId=id,SourceSha256=new string('A',64),SourceByteLength=100,SourceOrientation=1,
            SourceWidth=20,SourceHeight=20,PreviewWidth=20,PreviewHeight=20,ReferenceStart=new(0,0),ReferenceEnd=new(10,0),ReferenceLengthMm=10,
            TracePixels=[new(0,0),new(10,0)],GeneratedPoints=[new(0,0),new(10,0)] };
        foreach(var id in new[]{candidate.FrontPieceId,candidate.BackPieceId})
        {
            var target=new ProjectDocument { PhotoTraceHistory=[Photo(id)] };
            Assert.Empty(target.Pieces);ProjectStorage.Validate(target);
            var ex=Assert.Throws<InvalidOperationException>(()=>ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target));
            Assert.Contains("geçmişinde",ex.Message);
            target.PhotoTraceHistory=[Photo(Guid.NewGuid())];ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target);
            target.ImportProvenance=new DxfImportProvenance { SourceFileName="source.dxf",SourceSha256=new string('B',64),SourceByteLength=12,MillimetersPerUnit=1,PieceIds=[id] };
            ProjectStorage.Validate(target);Assert.Throws<InvalidOperationException>(()=>ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target));
            target.ImportProvenance.PieceIds=[Guid.NewGuid()];ALineProjectEvidenceStorage.EnsureTargetClear(evidence,target);
        }
    }}
