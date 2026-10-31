using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfPreparedTextTests
{
    private static string DirectoryForTest()
    {
        var path = Path.Combine(@"C:\Projects\TekstilCAD\work", "dxf-prepared-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); return path;
    }
    private static ProjectDocument Document() => new()
    {
        Pieces = [new PatternPiece { Name="Ön ŞİĞ", Closed=true, Points=[new(0,0),new(2.5,0),new(0,3)] },
                  new PatternPiece { Name="Ön ŞİĞ", Closed=false, Points=[new(4,1),new(7,8)] }]
    };

    [Fact]
    public void PreparedTextMatchesBothExistingExportPathsByteForByte()
    {
        var directory=DirectoryForTest();
        try
        {
            var document=Document();
            var prepared=DxfFormat.PrepareText(document);
            var expected=new UTF8Encoding(false, true).GetBytes(prepared);
            var normal=Path.Combine(directory,"normal.dxf");
            var conditional=Path.Combine(directory,"conditional.dxf");
            DxfFormat.Export(document,normal);
            DxfFormat.Export(document,conditional,ProjectStorage.CaptureVersion(conditional));
            Assert.Equal(expected,File.ReadAllBytes(normal));
            Assert.Equal(expected,File.ReadAllBytes(conditional));
            Assert.False(expected.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.DoesNotContain('\r',prepared);
            Assert.Contains("Ön ŞİĞ",prepared);
            Assert.EndsWith("0\nEOF\n",prepared);
            var reopened=DxfFormat.Import(normal);
            Assert.Equal(document.Pieces.Count,reopened.Project.Pieces.Count);
        }
        finally { Directory.Delete(directory,true); }
    }

    [Fact]
    public void PreparationRejectsInvalidGeometryAndConditionalConflictPreservesDestination()
    {
        var directory=DirectoryForTest();
        try
        {
            var path=Path.Combine(directory,"target.dxf");
            File.WriteAllText(path,"original");
            var approved=ProjectStorage.CaptureVersion(path);
            File.WriteAllText(path,"external change");
            Assert.Throws<ProjectConflictException>(()=>DxfFormat.Export(Document(),path,approved));
            Assert.Equal("external change",File.ReadAllText(path));
            Assert.Throws<InvalidDataException>(()=>DxfFormat.PrepareText(new ProjectDocument()));
            var invalid=new ProjectDocument { Pieces=[new PatternPiece { Name="Eksik",Points=[new(0,0)] }] };
            Assert.Throws<InvalidDataException>(()=>DxfFormat.PrepareText(invalid));
            Assert.Equal("external change",File.ReadAllText(path));
        }
        finally { Directory.Delete(directory,true); }
    }
}
