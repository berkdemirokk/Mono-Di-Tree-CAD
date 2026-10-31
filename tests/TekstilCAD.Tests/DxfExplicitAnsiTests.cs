using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfExplicitAnsiTests
{
    private static string Drawing(string first,string second) =>
        $"0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n{first}\n10\n0\n20\n0\n11\n10\n21\n0\n"+
        $"0\nLINE\n8\n{second}\n10\n20\n20\n0\n11\n30\n21\n0\n0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void ExplicitWindows1254KeepsTwoDifferentTurkishLayersAndInputBytes()
    {
        // Ö and Ü have bytes D6 and DC in both Latin-1 and Windows-1254.
        var bytes=Encoding.Latin1.GetBytes(Drawing("AÖ","AÜ"));
        using var defaultInput=new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(defaultInput,"legacy"));
        using var input=new MemoryStream(bytes);
        var result=DxfFormat.Import(input,"legacy",1,DxfTextEncoding.Windows1254);
        Assert.Equal(2,result.Project.Pieces.Count);
        Assert.Contains(result.Project.Pieces,p=>p.Name=="AÖ");Assert.Contains(result.Project.Pieces,p=>p.Name=="AÜ");
        Assert.Contains(result.Warnings,w=>w.Contains("kullanıcının seçtiği Windows-1254"));
        Assert.Equal(bytes,input.ToArray());
    }

    [Fact]
    public void ExplicitAnsiWithAlsoValidUtf8WarnsOfAmbiguityAndDoesNotGuess()
    {
        var bytes=Encoding.UTF8.GetBytes(Drawing("AÖ","AÜ"));
        using var input=new MemoryStream(bytes);
        var result=DxfFormat.Import(input,"ambiguous",1,DxfTextEncoding.Windows1254);
        Assert.Contains(result.Warnings,w=>w.Contains("UTF-8 olarak da geçerli"));
        Assert.DoesNotContain(result.Project.Pieces,p=>p.Name=="AÖ");
        Assert.Equal(bytes,input.ToArray());
    }

    [Fact]
    public void BomConflictAndUnsupportedWideEncodingsAreRejected()
    {
        var utf8=new byte[]{0xEF,0xBB,0xBF}.Concat(Encoding.UTF8.GetBytes(Drawing("A","B"))).ToArray();
        using var input=new MemoryStream(utf8);
        Assert.Contains("BOM",Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(input,"bom",1,DxfTextEncoding.Windows1254)).Message);
        var utf16=new byte[]{0xFF,0xFE}.Concat(Encoding.Unicode.GetBytes(Drawing("A","B"))).ToArray();
        using var wide=new MemoryStream(utf16);
        Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(wide,"wide",1,DxfTextEncoding.Utf8));
    }

    [Fact]
    public void InvalidAnsiLayerByteAndUnknownEncodingDoNotImport()
    {
        var bytes=Encoding.Latin1.GetBytes(Drawing("A\u0081","B"));
        using var input=new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(()=>DxfFormat.Import(input,"invalid",1,DxfTextEncoding.Windows1254));
        using var other=new MemoryStream(bytes);
        Assert.Throws<ArgumentOutOfRangeException>(()=>DxfFormat.Import(other,"unknown",1,(DxfTextEncoding)999));
    }

    [Fact]
    public void ExplicitSelectionPreservesCancellation()
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(Drawing("A","B")));
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>DxfFormat.Import(input,"cancel",1,DxfTextEncoding.Windows1254,cancellation.Token));
    }
}
