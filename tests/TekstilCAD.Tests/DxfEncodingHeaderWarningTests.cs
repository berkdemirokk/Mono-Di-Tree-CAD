using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DxfEncodingHeaderWarningTests
{
    private static string Dxf(string header,string layer="Ö") =>
        "0\nSECTION\n2\nHEADER\n"+header+"0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n"+layer+
        "\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";
    private static DxfImportResult Read(byte[] bytes,DxfTextEncoding encoding)
    { using var input=new MemoryStream(bytes);return DxfFormat.Import(input,"fixture",1,encoding); }

    [Fact]
    public void ModernHeaderAndExplicit1254WarnWithoutSwitching()
    {
        var bytes=Encoding.Latin1.GetBytes(Dxf("9\n$ACADVER\n1\nAC1021\n"));
        var result=Read(bytes,DxfTextEncoding.Windows1254);
        Assert.Equal("Ö",Assert.Single(result.Project.Pieces).Name);
        Assert.Contains(result.Warnings,w=>w.Contains("2007 veya sonrası") && w.Contains("Windows-1254"));
    }

    [Fact]
    public void LegacyHeaderAndUtf8NonAsciiWarnWithoutChangingText()
    {
        var result=Read(Encoding.UTF8.GetBytes(Dxf("9\n$ACADVER\n1\nAC1015\n")),DxfTextEncoding.Utf8);
        Assert.Equal("Ö",Assert.Single(result.Project.Pieces).Name);
        Assert.Contains(result.Warnings,w=>w.Contains("2004 veya öncesi") && w.Contains("UTF-8"));
    }

    [Fact]
    public void LegacyAsciiAndUnicodeEscapesDoNotProduceFalseConflict()
    {
        var result=Read(Encoding.ASCII.GetBytes(Dxf("9\n$ACADVER\n1\nAC1015\n",@"A\U+015F")),DxfTextEncoding.Utf8);
        Assert.Equal("Aş",Assert.Single(result.Project.Pieces).Name);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("2004 veya öncesi"));
    }

    [Fact]
    public void DuplicatesAndUnknownVersionsAreWarningsNotGuessedEncodings()
    {
        var duplicated=Read(Encoding.UTF8.GetBytes(Dxf("9\n$ACADVER\n1\nAC1015\n9\n$ACADVER\n1\nAC1021\n")),DxfTextEncoding.Utf8);
        Assert.Contains(duplicated.Warnings,w=>w.Contains("yinelenmiş") && w.Contains("kodlamayı"));
        Assert.DoesNotContain(duplicated.Warnings,w=>w.Contains("2004 veya öncesi"));
        var unknown=Read(Encoding.UTF8.GetBytes(Dxf("9\n$ACADVER\n1\nAC9999\n")),DxfTextEncoding.Utf8);
        Assert.Contains(unknown.Warnings,w=>w.Contains("sürüm başlığı bilinmiyor"));
    }

    [Fact]
    public void CodePageMismatchWarnsForExplicitLegacyChoice()
    {
        var result=Read(Encoding.Latin1.GetBytes(Dxf("9\n$ACADVER\n1\nAC1015\n9\n$DWGCODEPAGE\n3\nANSI_1252\n")),DxfTextEncoding.Windows1254);
        Assert.Contains(result.Warnings,w=>w.Contains("kod sayfası başlığı") && w.Contains("1254"));
    }

    [Fact]
    public void EntityAndApplicationDataCannotMasqueradeAsHeader()
    {
        var header="9\n$OTHER\n2\nHEADER\n102\n{APP\n9\n$ACADVER\n1\nAC1015\n102\n}\n";
        var result=Read(Encoding.UTF8.GetBytes(Dxf(header)),DxfTextEncoding.Utf8);
        Assert.DoesNotContain(result.Warnings,w=>w.Contains("2004 veya öncesi") || w.Contains("sürüm başlığı bilinmiyor"));
    }
}
