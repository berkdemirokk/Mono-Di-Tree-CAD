using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ALineCandidateEvidenceStorageTests
{
    private static (ALineCandidateEvidence Evidence,ProjectDocument Document,double[] Inputs,double[] NewEase) Fixture()
    {
        var input=new ALinePanelInput(360,380,500,520,40,40,40,40,200,700,1600);
        var plan=ALineSideBalance.Propose(input);var proposed=plan.Candidate!;var preview=ALinePanelPreview.Create(proposed);
        double[] values=[input.FrontWaistArc,input.BackWaistArc,input.FrontHipArc,input.BackHipArc,input.FrontWaistEase,input.BackWaistEase,input.FrontHipEase,input.BackHipEase,input.HipDepth,input.WaistToHem,input.FinishedHemCircumference];
        double[] oldEase=[input.FrontWaistEase,input.BackWaistEase,input.FrontHipEase,input.BackHipEase];
        double[] newEase=[proposed.FrontWaistEase,proposed.BackWaistEase,proposed.FrontHipEase,proposed.BackHipEase];
        double[] Flatten(IReadOnlyList<Point2> points)=>points.SelectMany(p=>new[]{p.X,p.Y}).ToArray();
        var frontId=Guid.NewGuid();var backId=Guid.NewGuid();var source=new string('A',64);
        var evidence=ALineCandidateEvidenceStorage.Create(frontId,backId,source,values,oldEase,newEase,Flatten(preview.Front),Flatten(preview.Back));
        var document=new ProjectDocument { Pieces=[
            new PatternPiece { Id=frontId,Name="Deneysel ön",Closed=true,Points=preview.Front.ToList(),ExperimentalALine=new(ALineExperimentalProvenance.CurrentAlgorithm,source,"front") },
            new PatternPiece { Id=backId,Name="Deneysel arka",Closed=true,Points=preview.Back.ToList(),ExperimentalALine=new(ALineExperimentalProvenance.CurrentAlgorithm,source,"back") }] };
        return(evidence,document,values,newEase);
    }
    private sealed class OversizedList : IReadOnlyList<double>
    {
        public bool Accessed { get; private set; }
        public int Count=>int.MaxValue;
        public double this[int index] { get { Accessed=true;throw new InvalidOperationException(); } }
        public IEnumerator<double> GetEnumerator() { Accessed=true;throw new InvalidOperationException(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    [Fact] public void OversizedInputIsRejectedBeforeEnumerationOrIndexing()
    {
        var f=Fixture();var oversized=new OversizedList();
        Assert.Throws<InvalidDataException>(()=>ALineCandidateEvidenceStorage.Create(f.Evidence.FrontPieceId,f.Evidence.BackPieceId,f.Evidence.SourceSha256,oversized,f.Evidence.OriginalEaseMm,f.Evidence.CandidateEaseMm,f.Evidence.FrontCoordinatesMm,f.Evidence.BackCoordinatesMm));
        Assert.False(oversized.Accessed);
    }
    [Fact] public void MutationsAfterCaptureCannotChangeSerializedOrComparedSnapshot()
    {
        var f=Fixture();var source=f.Evidence.SourceSha256;var expected=ALineCandidateEvidenceStorage.Serialize(f.Evidence);
        var json=ALineCandidateEvidenceStorage.SerializeForVerification(f.Evidence,()=>f.Evidence.InputsMm[0]+=1);
        Assert.Equal(expected,json);Assert.Throws<InvalidDataException>(()=>ALineCandidateEvidenceStorage.Validate(f.Evidence));
        var g=Fixture();var match=ALineCandidateEvidenceStorage.MatchesForVerification(g.Evidence,g.Document,g.Evidence.SourceSha256,g.Inputs,g.NewEase,()=> {
            g.Evidence.CandidateEaseMm[0]+=1;g.Inputs[0]+=1;g.NewEase[0]+=1;g.Document.Pieces[0].Points[0]=new(3,3);g.Document.Pieces[1].ExperimentalALine=null;
        });
        Assert.True(match);Assert.Throws<InvalidDataException>(()=>ALineCandidateEvidenceStorage.Validate(g.Evidence));
        Assert.Equal(source,f.Evidence.SourceSha256);
    }
    [Fact] public void CandidateEvidenceAndSideBalanceAgreeAcrossBoundaryCases()
    {
        var cases=new[] {
            new ALinePanelInput(360,380,500,520,40,40,40,40,200,700,1600),
            new ALinePanelInput(300,400,500,500,100,0,0,0,200,700,1600),
            new ALinePanelInput(360.0000001,360,500,500,20,20,20,20,200,700,1600),
            new ALinePanelInput(300,500,500,600,0,0,0,0,200,700,1600),
            new ALinePanelInput(360,360,500,500,20,20,20,20,700,700,1600),
            new ALinePanelInput(600,360,500,500,20,20,20,20,200,700,1600),
            new ALinePanelInput(360,360,500,500,20,20,20,20,200,700,1040),
            new ALinePanelInput(double.NaN,360,500,500,20,20,20,20,200,700,1600)
        };
        foreach(var input in cases)
        {
            var plan=ALineSideBalance.Propose(input);double[] values=[input.FrontWaistArc,input.BackWaistArc,input.FrontHipArc,input.BackHipArc,input.FrontWaistEase,input.BackWaistEase,input.FrontHipEase,input.BackHipEase,input.HipDepth,input.WaistToHem,input.FinishedHemCircumference];
            double[] old=[input.FrontWaistEase,input.BackWaistEase,input.FrontHipEase,input.BackHipEase];
            static (double F,double B) Balance(double a,double b,double x,double y) { var total=x+y;var f=(b-a+total)/2;return(f,total-f); }
            var w=Balance(values[0],values[1],values[4],values[5]);var h=Balance(values[2],values[3],values[6],values[7]);
            double[] next=[w.F,w.B,h.F,h.B];double[] Panel(double waist,double hip)=>[0,0,waist/2,0,hip/2,values[8],values[10]/4,values[9],0,values[9]];
            var error=Record.Exception(()=>ALineCandidateEvidenceStorage.Create(Guid.NewGuid(),Guid.NewGuid(),new string('A',64),values,old,next,Panel(values[0]+w.F,values[2]+h.F),Panel(values[1]+w.B,values[3]+h.B)));
            if(plan.Available)Assert.Null(error);else Assert.True(error is InvalidDataException or ArgumentException);
        }
    }
    [Fact] public void ExactEvidenceRoundTripsAndRejectsEveryChangedAssociation()
    {
        var f=Fixture();var restored=ALineCandidateEvidenceStorage.Deserialize(ALineCandidateEvidenceStorage.Serialize(f.Evidence));
        Assert.True(ALineCandidateEvidenceStorage.Matches(restored,f.Document,f.Evidence.SourceSha256,f.Inputs,f.NewEase));
        var changedInput=(double[])f.Inputs.Clone();changedInput[0]+=1;
        Assert.False(ALineCandidateEvidenceStorage.Matches(restored,f.Document,f.Evidence.SourceSha256,changedInput,f.NewEase));
        var changedEase=(double[])f.NewEase.Clone();changedEase[0]+=1;
        Assert.False(ALineCandidateEvidenceStorage.Matches(restored,f.Document,f.Evidence.SourceSha256,f.Inputs,changedEase));
        Assert.False(ALineCandidateEvidenceStorage.Matches(restored,f.Document,new string('B',64),f.Inputs,f.NewEase));
        var moved=f.Document.Clone();moved.Pieces[0].Points[0]=new(1,0);Assert.False(ALineCandidateEvidenceStorage.Matches(restored,moved,f.Evidence.SourceSha256,f.Inputs,f.NewEase));
        var reversed=f.Document.Clone();reversed.Pieces[1].Points.Reverse();Assert.False(ALineCandidateEvidenceStorage.Matches(restored,reversed,f.Evidence.SourceSha256,f.Inputs,f.NewEase));
        var missing=f.Document.Clone();missing.Pieces.RemoveAt(1);Assert.False(ALineCandidateEvidenceStorage.Matches(restored,missing,f.Evidence.SourceSha256,f.Inputs,f.NewEase));
        var wrongSide=f.Document.Clone();wrongSide.Pieces[0].ExperimentalALine=wrongSide.Pieces[0].ExperimentalALine! with { Side="back" };Assert.False(ALineCandidateEvidenceStorage.Matches(restored,wrongSide,f.Evidence.SourceSha256,f.Inputs,f.NewEase));
        Assert.Throws<InvalidOperationException>(()=>ExperimentalOutputGuard.EnsureAllowed(f.Document.Pieces));
        Assert.Equal(10,ProjectStorage.GetProjectFormatVersion(f.Document));
    }
    [Fact] public void HashAndSemanticChecksRejectTamperingAndNewFileDoesNotOverwrite()
    {
        var f=Fixture();var json=ALineCandidateEvidenceStorage.Serialize(f.Evidence);
        Assert.Throws<InvalidDataException>(()=>ALineCandidateEvidenceStorage.Deserialize(json.Replace("\"version\":1","\"version\":2")));
        Assert.Throws<InvalidDataException>(()=>ALineCandidateEvidenceStorage.Deserialize(json.Replace("\"version\":1","\"version\":1,\"version\":1")));
        var tampered=f.Evidence with { CandidateEaseMm=[51,29,50,30] };
        Assert.Throws<InvalidDataException>(()=>ALineCandidateEvidenceStorage.Serialize(tampered));
        var changedSource=f.Evidence with { SourceSha256=new string('B',64) };Assert.Throws<InvalidDataException>(()=>ALineCandidateEvidenceStorage.Serialize(changedSource));
        var changedGeometry=f.Evidence.FrontCoordinatesMm.ToArray();changedGeometry[2]+=1;
        Assert.Throws<InvalidDataException>(()=>ALineCandidateEvidenceStorage.Serialize(f.Evidence with { FrontCoordinatesMm=changedGeometry }));
        var path=Path.Combine(Path.GetTempPath(),"aline-evidence-"+Guid.NewGuid().ToString("N")+ALineCandidateEvidenceStorage.Extension);
        var projectPath=Path.Combine(Path.GetTempPath(),"aline-v10-"+Guid.NewGuid().ToString("N")+".tcad");
        try { ProjectStorage.Save(f.Document,projectPath);var loadedProject=ProjectStorage.Load(projectPath);
            Assert.Equal(10,ProjectStorage.GetProjectFormatVersion(loadedProject));Assert.True(DocumentSession.ContentEquals(f.Document,loadedProject));
            ALineCandidateEvidenceStorage.SaveNew(path,f.Evidence);var before=File.ReadAllBytes(path);
            Assert.Throws<ProjectConflictException>(()=>ALineCandidateEvidenceStorage.SaveNew(path,f.Evidence));Assert.Equal(before,File.ReadAllBytes(path));
            Assert.True(ALineCandidateEvidenceStorage.Matches(ALineCandidateEvidenceStorage.Load(path),loadedProject,f.Evidence.SourceSha256,f.Inputs,f.NewEase)); }
        finally { if(File.Exists(path))File.Delete(path);if(File.Exists(projectPath))File.Delete(projectPath); }
    }
    [Fact] public void SameSourceDifferentInputsOrPanelIdentitiesProduceDifferentDigests()
    {
        var first=Fixture();var second=Fixture();Assert.Equal(first.Evidence.SourceSha256,second.Evidence.SourceSha256);
        Assert.NotEqual(first.Evidence.IntegritySha256,second.Evidence.IntegritySha256);
        var changed=first.Evidence.InputsMm.ToArray();changed[8]+=1;
        var candidate=ALineSideBalance.Propose(new(changed[0],changed[1],changed[2],changed[3],changed[4],changed[5],changed[6],changed[7],changed[8],changed[9],changed[10])).Candidate!;
        var preview=ALinePanelPreview.Create(candidate);double[] Flat(IReadOnlyList<Point2> points)=>points.SelectMany(p=>new[]{p.X,p.Y}).ToArray();
        var altered=ALineCandidateEvidenceStorage.Create(first.Evidence.FrontPieceId,first.Evidence.BackPieceId,first.Evidence.SourceSha256,changed,first.Evidence.OriginalEaseMm,first.Evidence.CandidateEaseMm,Flat(preview.Front),Flat(preview.Back));
        Assert.NotEqual(first.Evidence.IntegritySha256,altered.IntegritySha256);
    }
}
