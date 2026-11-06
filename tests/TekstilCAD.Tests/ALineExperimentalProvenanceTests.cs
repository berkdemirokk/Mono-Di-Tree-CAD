using TekstilCAD.Core;
using TekstilCAD.FileFormats;

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
