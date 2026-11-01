using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DressMeasurementParserTests
{
    [Fact]
    public void TurkishUppercaseMeasurementsRetainOriginalEvidenceAndSplitAtCommas()
    {
        var result=DressMeasurementParser.Review("GÖĞÜS ÇEVRESİ 96 CM, BEL ÇEVRESİ 72 CM", "KALÇA ÇEVRESİ 100 CM; ELBİSE BOYU 110 CM");
        Assert.Empty(result.Questions);
        Assert.Equal(720m,result.Measurements.Single(x=>x.Label=="bel çevresi").Millimeters);
        Assert.Equal("BEL ÇEVRESİ 72 CM",Assert.Single(result.Measurements.Single(x=>x.Label=="bel çevresi").SourceExpressions));
        Assert.Equal(1100m,result.Measurements.Single(x=>x.Label=="elbise boyu").Millimeters);
    }
    [Fact]
    public void UppercaseAmbiguityCannotHideAConflict()
    {
        var result=DressMeasurementParser.Review("bel çevresi 70 cm", "BEL ÇEVRESİ YAKLAŞIK 80 CM");
        Assert.DoesNotContain(result.Measurements,x=>x.Label=="bel çevresi");
        Assert.Contains(result.Questions,x=>x.StartsWith("bel çevresi"));
        Assert.Contains("BEL ÇEVRESİ YAKLAŞIK 80 CM",result.Unrecognized);
    }
    [Fact]
    public void FindsExplicitTurkishLabelsUnitsAndOriginalEvidence()
    {
        var result=DressMeasurementParser.Review("Kadın elbise; göğüs çevresi 96,5 cm, bel çevresi 720 mm", "kalça çevresi 100 cm\nelbise boyu: 110 cm\nkol boyu = 62 cm");
        Assert.Equal(965m,result.Measurements.Single(x=>x.Label=="göğüs çevresi").Millimeters);
        Assert.Equal(720m,result.Measurements.Single(x=>x.Label=="bel çevresi").Millimeters);
        Assert.Equal(1100m,result.Measurements.Single(x=>x.Label=="elbise boyu").Millimeters);
        Assert.Equal("göğüs çevresi 96,5 cm",Assert.Single(result.Measurements.Single(x=>x.Label=="göğüs çevresi").SourceExpressions));
        Assert.Empty(result.Questions);
    }
    [Fact]
    public void ConflictsAndUnclearQualifiersNeverProduceAValue()
    {
        var result=DressMeasurementParser.Review("bel çevresi 70 cm; kalça çevresi yaklaşık 100 cm; göğüs çevresi 90 cm veya 95 cm", "bel çevresi 720 mm\nelbise boyu 110 cm");
        Assert.DoesNotContain(result.Measurements,x=>x.Label=="bel çevresi" || x.Label=="kalça çevresi" || x.Label=="göğüs çevresi");
        Assert.Equal(3,result.Questions.Count);Assert.Equal(1100m,Assert.Single(result.Measurements).Millimeters);
    }
    [Fact]
    public void EqualUnitConversionsAgreeButMissingAndUnsupportedValuesAreAsked()
    {
        var result=DressMeasurementParser.Review("göğüs çevresi 96 cm; bel çevresi 700 mm", "göğüs çevresi 960 mm\nkalça çevresi 0 cm\nelbise boyu 110");
        var chest=result.Measurements.Single(x=>x.Label=="göğüs çevresi");Assert.Equal(960m,chest.Millimeters);Assert.Equal(2,chest.SourceExpressions.Count);
        Assert.Contains(result.Questions,x=>x.StartsWith("kalça çevresi"));Assert.Contains(result.Questions,x=>x.StartsWith("elbise boyu"));
        Assert.DoesNotContain(result.Measurements,x=>x.Label=="kol boyu");
    }
    [Theory]
    [InlineData("bel 70 cm")]
    [InlineData("bel çevresi 70 cm veya 80 cm")]
    [InlineData("bel çevresi yaklaşık 70 cm")]
    [InlineData("bel çevresi 70-80 cm")]
    [InlineData("bel çevresi 70 cm ve 80 cm")]
    [InlineData("bel çevresi -70 cm")]
    [InlineData("bel çevresi 1e2 cm")]
    public void AmbiguousStatementsCannotOverrideAnExplicitValue(string conflicting)
    {
        var result=DressMeasurementParser.Review("bel çevresi 70 cm",conflicting);
        Assert.DoesNotContain(result.Measurements,x=>x.Label=="bel çevresi");
        Assert.Contains(result.Questions,x=>x.StartsWith("bel çevresi"));
    }
}
