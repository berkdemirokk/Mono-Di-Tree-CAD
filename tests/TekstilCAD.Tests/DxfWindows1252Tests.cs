using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfWindows1252Tests
{
    [Fact]
    public void ExplicitWesternAnsiPreservesLayerAndScale()
    {
        var source = "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1015\n9\n$DWGCODEPAGE\n3\nANSI_1252\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n0\nLINE\n8\npièceÐ\n6\nCONTINUOUS\n10\n0\n20\n0\n11\n10\n21\n0\n0\nENDSEC\n0\nEOF\n";
        var bytes = Encoding.Latin1.GetBytes(source);
        using var stream = new MemoryStream(bytes);
        var result = DxfFormat.Import(stream, "western", 25.4, DxfTextEncoding.Windows1252);
        var piece = Assert.Single(result.Project.Pieces);
        Assert.Equal("pièceÐ", piece.Name);
        Assert.Equal(254, piece.Points.Max(p => p.X));
        Assert.Contains(result.Warnings, w => w.Contains("kullanıcının seçtiği Windows-1252"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("kod sayfası başlığı"));
        Assert.Equal(bytes, stream.ToArray());
    }
}
