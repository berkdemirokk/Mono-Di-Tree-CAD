using TekstilCAD.Core;
using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public class PieceMetadataTests
{
    private static ProjectDocument Sample() => new() { Pieces = [new() { Points=[new(0,0),new(10,0),new(0,10)] }] };
    [Fact] public void MetadataParticipatesInCloneDirtyUndoAndRedo()
    {
        var s=new DocumentSession(Sample()); s.Execute(d=>d.Pieces[0].Metadata=new() { Role="Kesim", Size="M", Material="Pamuk", Quantity=2 },ProjectStorage.Validate);
        Assert.True(s.IsDirty); var clone=s.Document.Clone(); clone.Pieces[0].Metadata!.Size="L"; Assert.Equal("M",s.Document.Pieces[0].Metadata!.Size);
        Assert.True(s.Undo()); Assert.False(s.IsDirty); Assert.True(s.Redo()); Assert.Equal(2,s.Document.Pieces[0].Metadata!.Quantity);
    }
    [Fact] public void Version2RoundTripAndLegacyShape()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory); while(root is not null&&!File.Exists(Path.Combine(root.FullName,"AGENTS.md")))root=root.Parent;
        Assert.NotNull(root); var dir=Path.Combine(root!.FullName,"work","metadata-tests"); Directory.CreateDirectory(dir);var path=Path.Combine(dir,Guid.NewGuid().ToString("N")+".tcad");
        try { var d=Sample(); ProjectStorage.Save(d,path); Assert.DoesNotContain("Metadata",File.ReadAllText(path)); d.Pieces[0].Metadata=new(){Size="M"}; ProjectStorage.Save(d,path); Assert.StartsWith("[",File.ReadAllText(path)); Assert.Contains("2,",File.ReadAllText(path)); Assert.Equal("M",ProjectStorage.Load(path).Pieces[0].Metadata!.Size); var legacy=File.ReadAllText(path).Replace("[\n  2,", "[\n  1,").Replace("[\r\n  2,", "[\r\n  1,"); File.WriteAllText(path,legacy); Assert.Throws<InvalidDataException>(()=>ProjectStorage.Load(path)); }
        finally { if(File.Exists(path))File.Delete(path); }
    }
    [Fact] public void InvalidValuesRejected() { var d=Sample(); d.Pieces[0].Metadata=new(){Quantity=0}; Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(d)); d.Pieces[0].Metadata=new(){Role="other"}; Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(d)); d.Pieces[0].Metadata=new(){Size=" M"}; Assert.Throws<InvalidDataException>(()=>ProjectStorage.Validate(d)); }
}
