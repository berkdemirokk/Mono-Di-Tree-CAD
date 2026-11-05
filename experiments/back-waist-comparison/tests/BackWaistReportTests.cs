using System.Numerics;
using BackWaistComparison;
using TekstilCAD.Patterns;
using Xunit;

public class BackWaistReportTests
{
    static byte[] Evidence()=>File.ReadAllBytes(Environment.GetEnvironmentVariable("BREANNA_WAIST_EVIDENCE") ?? throw new InvalidOperationException("BREANNA_WAIST_EVIDENCE required"));
    static ALinePanelInput Input()=>new(350,370,480,520,10,20,20,20,200,700,1600);
    [Theory]
    [InlineData("synthetic-base")][InlineData("synthetic-scaled")][InlineData("synthetic-distinct")]
    public void RealUpperEvidenceAndActualLowerGeometryProduceClosedDiagnostic(string body)
    {
        var result=BackWaistReport.Compare(Evidence(),body,Input(),"synthetic-lower-1",true);
        var unit=BigInteger.One<<30;Assert.Equal(195*unit,result.LowerHalfMm.Lower);Assert.Equal(195*unit,result.LowerHalfMm.Upper);
        Assert.Equal(result.UpperHalfMm.Lower-result.LowerHalfMm.Upper,result.UpperMinusLowerMm.Lower);
        Assert.Equal(result.UpperHalfMm.Upper-result.LowerHalfMm.Lower,result.UpperMinusLowerMm.Upper);
        Assert.Contains("unresolved",result.Status);Assert.False(result.ProductionAllowed);Assert.False(result.GeometryExportAllowed);Assert.False(result.DeviceAllowed);
    }
    [Fact] public void ChangedEvidenceUnknownCaseAndInvalidInputFailClosed()
    {
        var bytes=Evidence();bytes[0]^=1;
        Assert.Throws<ArgumentException>(()=>BackWaistReport.Compare(bytes,"synthetic-base",Input(),"lower",true));
        Assert.Throws<ArgumentException>(()=>BackWaistReport.Compare(Evidence(),"other",Input(),"lower",true));
        Assert.Throws<ArgumentException>(()=>BackWaistReport.Compare(Evidence(),"synthetic-base",Input(),"lower",false));
        Assert.Throws<ArgumentException>(()=>BackWaistReport.Compare(Evidence(),"synthetic-base",Input(),"",true));
        Assert.Throws<ArgumentException>(()=>BackWaistReport.Compare(Evidence(),"synthetic-base",Input() with { BackWaistArc=double.NaN },"lower",true));
    }
    [Fact] public void ActualBackWidthChangesWithoutCopyingAFormulaIntoTheAdapter()
    {
        var a=BackWaistReport.Compare(Evidence(),"synthetic-base",Input(),"a",true);
        var b=BackWaistReport.Compare(Evidence(),"synthetic-base",Input() with { BackWaistArc=390 },"b",true);
        Assert.Equal(10*(BigInteger.One<<30),b.LowerHalfMm.Lower-a.LowerHalfMm.Lower);
        Assert.Equal(a.UpperHalfMm,b.UpperHalfMm);
    }
}
