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
    [Fact] public void FractionalAndAdjacentBinary64LengthsAreEnclosedAndDifferenceKeepsItsSign()
    {
        // Independent exact rational representation of each actual endpoint.
        static (BigInteger N,BigInteger D) Fraction(double x)
        {
            var bits=BitConverter.DoubleToUInt64Bits(x);int exponent=(int)((bits>>52)&2047);
            BigInteger numerator=bits&((1UL<<52)-1);
            if(exponent!=0)numerator+=(BigInteger.One<<52);
            int shift=exponent==0 ? -1074 : exponent-1023-52;
            if((bits>>63)!=0)numerator=-numerator;
            return shift>=0 ? (numerator<<shift,BigInteger.One) : (numerator,BigInteger.One<<-shift);
        }
        foreach(double arc in new[]{370.1,Math.BitIncrement(370.1),Math.BitDecrement(370.1),600.1})
        {
            var input=Input() with { BackWaistArc=arc, BackHipArc=700, FinishedHemCircumference=2000 };
            var panel=ALinePanelPreview.Create(input).Back;
            var a=Fraction(panel[0].X);var b=Fraction(panel[1].X);
            var numerator=BigInteger.Abs(b.N*a.D-a.N*b.D);var denominator=a.D*b.D;
            var report=BackWaistReport.Compare(Evidence(),"synthetic-base",input,"synthetic",true);
            var unit=BigInteger.One<<30;
            Assert.True(report.LowerHalfMm.Lower*denominator<=numerator*unit);
            Assert.True(numerator*unit<=report.LowerHalfMm.Upper*denominator);
            Assert.True(report.LowerHalfMm.Upper-report.LowerHalfMm.Lower<=BigInteger.One);
            Assert.False(report.ProductionAllowed);Assert.False(report.GeometryExportAllowed);Assert.False(report.DeviceAllowed);
            if(arc<400)Assert.True(report.UpperMinusLowerMm.Lower>0);
            else Assert.True(report.UpperMinusLowerMm.Upper<0);
        }
    }
}
