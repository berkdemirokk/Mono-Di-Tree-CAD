using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;
using TekstilCAD.Nesting;
using TekstilCAD.Grading;
using TekstilCAD.MachineGateway;

namespace TekstilCAD.Tests;

public sealed class ALineExperimentalProvenanceTests
{
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
