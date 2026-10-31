using System.Text;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfCompanionGradingNoticeTests
{
    [Fact]
    public void InspectionAndImportStateThatSeparateGradeTablesAreNotAssessed()
    {
        var source="0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nCUT\n10\n0\n20\n0\n11\n1\n21\n1\n0\nENDSEC\n0\nEOF\n";
        using var stream=new MemoryStream(Encoding.UTF8.GetBytes(source));
        var inspection=DxfFormat.InspectBlockPlacements(stream,DxfTextEncoding.Utf8);
        var import=DxfFormat.Import(stream,"grading notice");
        Assert.Contains(inspection.Warnings,w=>w.Contains("serileme tablosu") && w.Contains("ayrı") && w.Contains("aramaz"));
        Assert.Contains(import.Warnings,w=>w.Contains("serileme tablosu") && w.Contains("ayrı") && w.Contains("aramaz"));
        Assert.Empty(import.Project.GradingRules);
    }
}
