using TekstilCAD.Core;
using TekstilCAD.FileFormats;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public class OpenPathResultNameTests
{
    [Fact]
    public void SourceHintsDistinguishIdenticallyNamedPiecesAndSurviveProjectSave()
    {
        var first=new PatternPiece { Name="Ön", Closed=false, Points=[new(0,0),new(10,0)] };
        var second=new PatternPiece { Name="Ön", Closed=false, Points=[new(10,0),new(10,10),new(0,10)] };
        var joined=OpenPathJoin.Join(first,second,0).Piece;
        var closed=OpenPathClosure.Close(second,15).Piece;
        Assert.Contains(first.Name,joined.Name); Assert.Contains(first.Id.ToString("D"),joined.Name);
        Assert.Contains(second.Id.ToString("D"),joined.Name); Assert.Contains(second.Id.ToString("D"),closed.Name);
        Assert.NotEqual(joined.Name,closed.Name);
        var path=Path.Combine(AppContext.BaseDirectory,$"tekstil-name-{Guid.NewGuid():N}.tcad");
        try
        {
            ProjectStorage.Save(new ProjectDocument { Name="İsim denetimi",Pieces=[first,second,joined,closed] },path);
            var loaded=ProjectStorage.Load(path);
            Assert.Equal(joined.Name,loaded.Pieces.Single(p=>p.Id==joined.Id).Name);
            Assert.Equal(closed.Name,loaded.Pieces.Single(p=>p.Id==closed.Id).Name);
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }

    [Fact]
    public void LongSourceNamesKeepBothIdentitiesAndDoNotSplitUnicodeCharacters()
    {
        var name=new string('a',199)+"😀"+new string('b',200);
        var first=new PatternPiece { Name=name, Closed=false, Points=[new(0,0),new(10,0)] };
        var second=new PatternPiece { Name=name, Closed=false, Points=[new(10,0),new(10,10),new(0,10)] };
        var joined=OpenPathJoin.Join(first,second,0).Piece;
        var closed=OpenPathClosure.Close(second,15).Piece;
        Assert.True(joined.Name.Length<=1024); Assert.True(closed.Name.Length<=1024);
        Assert.Contains(first.Id.ToString("D"),joined.Name); Assert.Contains(second.Id.ToString("D"),joined.Name);
        Assert.Contains(second.Id.ToString("D"),closed.Name);
        Assert.DoesNotContain('\ud83d',joined.Name); Assert.DoesNotContain('\ude00',joined.Name);
        Assert.DoesNotContain('\ud83d',closed.Name); Assert.DoesNotContain('\ude00',closed.Name);
    }
}
