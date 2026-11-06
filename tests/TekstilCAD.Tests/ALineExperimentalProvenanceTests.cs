using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;
using TekstilCAD.Nesting;
using TekstilCAD.Grading;

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
