using System.Text;
using TekstilCAD.Core;
using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DxfImportParametersTests
{
    private static readonly string Drawing="0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n"+
        "0\nLAYER\n2\n0\n70\n0\n62\n7\n290\n1\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n"+
        "0\nSECTION\n2\nENTITIES\n0\nLWPOLYLINE\n8\n0\n90\n3\n70\n1\n"+
        "10\n0\n20\n0\n10\n10\n20\n0\n10\n0\n20\n10\n0\nENDSEC\n0\nEOF\n";

    [Fact]
    public void GeneralImportReportsExplicitEncodingAndScale()
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(Drawing));
        var result=DxfFormat.Import(input,"settings",2.54,DxfTextEncoding.Utf8);
        var options=Assert.IsType<DxfImportParameters>(result.ImportParameters);
        Assert.Equal(DxfImportMode.General,options.Mode);
        Assert.Equal(DxfTextEncoding.Utf8,options.Encoding);
        Assert.Equal(2.54,options.MillimetersPerUnit);
        Assert.Null(options.ComponentToleranceMm);Assert.Empty(options.ApprovedLayers);
    }

    [Fact]
    public void ControlledImportReportsToleranceAndApprovedLayers()
    {
        using var input=new MemoryStream(Encoding.UTF8.GetBytes(Drawing));
        var result=DxfFormat.ImportWithBlockPlacements(input,"settings",1,DxfTextEncoding.Utf8,0.1,["0"]);
        var options=Assert.IsType<DxfImportParameters>(result.ImportParameters);
        Assert.Equal(DxfImportMode.Controlled,options.Mode);
        Assert.Equal(0.1,options.ComponentToleranceMm);
        Assert.Equal(new[] {"0"},options.ApprovedLayers.ToArray());
    }

    [Fact]
    public void ManualResultDoesNotClaimImportOptions()
        => Assert.Null(new DxfImportResult(new ProjectDocument(),[]).ImportParameters);

    [Fact]
    public void ReviewTokenDistinguishesDifferentUnpairedApprovalCodeUnits()
    {
        using var firstInput=new MemoryStream(Encoding.UTF8.GetBytes(Drawing));
        using var secondInput=new MemoryStream(Encoding.UTF8.GetBytes(Drawing));
        var first=DxfFormat.ImportWithBlockPlacements(firstInput,"settings",1,DxfTextEncoding.Utf8,0,["0","\uD800"]);
        var second=DxfFormat.ImportWithBlockPlacements(secondInput,"settings",1,DxfTextEncoding.Utf8,0,["0","\uD801"])
            with { Project=first.Project.Clone() };
        Assert.NotEqual(DxfTextileGrainlineMapping.CaptureFingerprint(first),DxfTextileGrainlineMapping.CaptureFingerprint(second));
    }
}
