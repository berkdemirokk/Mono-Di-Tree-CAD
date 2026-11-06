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
    }}

