using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public class DressMeasurementParserTests
{
    [Theory]
    [InlineData("ön göğüs 42 cm")]
    [InlineData("göğüs noktaları arası 20 cm")]
    [InlineData("üst göğüs çevresi 90 cm")]
    [InlineData("ön üst göğüs 40 cm")]
    [InlineData("ön göğüs 420 mm")]
    [InlineData("ön göğüs: 42 cm")]
    [InlineData("ön göğüs 42 cm.")]
    [InlineData("ÖN GÖĞÜS = 42,5 CM!")]
    public void DistinctExperimentalMeasurementsDoNotObscureChestCircumference(string note)
    {
        var result=DressMeasurementParser.Review("göğüs çevresi 96 cm",note);
        Assert.Equal(960m,Assert.Single(result.Measurements).Millimeters);
        Assert.Contains(note,result.Unrecognized);
        var ambiguous=DressMeasurementParser.Review("göğüs çevresi 96 cm",note+" veya göğüs çevresi 100 cm");
        Assert.DoesNotContain(ambiguous.Measurements,x=>x.Label=="göğüs çevresi");
    }
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
    [Theory]
    [InlineData("göğüs çevresi 92 cm ve bel çevresi 72 cm.", 920, 720)]
    [InlineData("GÖĞÜS ÇEVRESİ 92 CM VE BEL ÇEVRESİ 720 MM!", 920, 720)]
    [InlineData("göğüs çevresi: 92,5 cm ve bel çevresi = 72 cm", 925, 720)]
    public void PhoneCompatibleConjunctionAcceptsOnlyExplicitMeasurementsAndPreservesEvidence(string message,int chest,int waist)
    {
        var original=message;
        var result=DressMeasurementParser.Review("",message);
        Assert.Equal(original,message);
        Assert.Equal(chest,result.Measurements.Single(m=>m.Label=="göğüs çevresi").Millimeters);
        Assert.Equal(waist,result.Measurements.Single(m=>m.Label=="bel çevresi").Millimeters);
        Assert.Contains(result.Measurements.Single(m=>m.Label=="göğüs çevresi").SourceExpressions,source=>original.Contains(source));
        Assert.Contains(result.Measurements.Single(m=>m.Label=="bel çevresi").SourceExpressions,source=>original.Contains(source));
        Assert.Empty(result.Unrecognized);
    }
    [Theory]
    [InlineData("göğüs çevresi 92 cm ve bel çevresi 72")]
    [InlineData("göğüs çevresi 92 cm ve bel çevresi yaklaşık 72 cm")]
    [InlineData("göğüs çevresi 92 cm ve bel çevresi 0 cm")]
    [InlineData("göğüs çevresi 92 cm ve bilinmeyen ölçü 72 cm")]
    [InlineData("göğüs çevresi 92 cm ve bel çevresi 72 cm ve göğüs çevresi 93 cm")]
    [InlineData("göğüs çevresi 92 cm ve bel çevresi 72 cm ve")]
    public void InvalidJoinedStatementDoesNotPartiallyAcceptValues(string message)
    {
        var result=DressMeasurementParser.Review("",message);
        Assert.Empty(result.Measurements);
        Assert.Contains(message,result.Unrecognized);
    }
    [Fact]
    public void ConflictOutsideJoinedStatementInvalidatesAllItsMeasurements()
    {
        var result=DressMeasurementParser.Review("göğüs çevresi 93 cm", "göğüs çevresi 92 cm ve bel çevresi 72 cm.");
        Assert.DoesNotContain(result.Measurements,m=>m.Label=="göğüs çevresi" || m.Label=="bel çevresi");
        Assert.Contains(result.Questions,q=>q.StartsWith("göğüs çevresi"));
        Assert.Contains(result.Questions,q=>q.StartsWith("bel çevresi"));
    }
    [Fact]
    public void IosFixtureLoadsRoundTripsAndReviewsJoinedMessage()
    {
        // Exact content of the independent iOS ios-message-ve fixture; no iOS
        // worktree dependency and no writes to its source directory.
        const string json="""{"format":"tekstilcad.design-request","version":1,"prompt":"A kesim 👗","measurements":"göğüs çevresi: 92 cm\nbel çevresi: 72 cm\ngöğüs çevresi 92 cm ve bel çevresi 72 cm","reference":null}""";
        var loaded=DesignRequestStorage.Deserialize(json);
        var roundTrip=DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(loaded));
        Assert.Equal(loaded.Prompt,roundTrip.Prompt);Assert.Equal(loaded.Measurements,roundTrip.Measurements);
        Assert.Equal("göğüs çevresi: 92 cm\nbel çevresi: 72 cm\ngöğüs çevresi 92 cm ve bel çevresi 72 cm",roundTrip.Measurements);
        var result=DressMeasurementParser.Review(roundTrip.Prompt,roundTrip.Measurements);
        Assert.Equal(920m,result.Measurements.Single(m=>m.Label=="göğüs çevresi").Millimeters);
        Assert.Equal(720m,result.Measurements.Single(m=>m.Label=="bel çevresi").Millimeters);
        Assert.DoesNotContain(result.Questions,q=>q.StartsWith("göğüs çevresi") || q.StartsWith("bel çevresi"));
    }
    [Theory]
    [InlineData("göğüs çevresi 92 cm ve\nbel çevresi 72 cm")]
    [InlineData("göğüs çevresi 92 cm ve\r\nbel çevresi 72 cm")]
    [InlineData("göğüs çevresi 92 cm ve\n \nbel çevresi 72 cm")]
    [InlineData("göğüs çevresi 92 cm ve\u00a0\n\u00a0\nbel çevresi 72 cm")]
    [InlineData("göğüs çevresi 92 cm ve\u2028\u2003\u2029bel çevresi 72 cm")]
    [InlineData("göğüs çevresi 92 cm\u0085\u00a0\u0085ve bel çevresi 72 cm")]
    [InlineData("göğüs çevresi 92 cm ve\r\n\t\r\n  \r\nbel çevresi 72 cm")]
    [InlineData("göğüs çevresi 92 cm\r\n \r\nve bel çevresi 72 cm")]
    [InlineData("göğüs çevresi 92 cm\r\nve bel çevresi 72 cm")]
    [InlineData("GÖĞÜS ÇEVRESİ 92 CM VE  \r\n  BEL ÇEVRESİ 72 CM.")]
    public void JoinedMeasurementsAcrossLineBreaksAreOneCompleteExpression(string message)
    {
        var review=DressMeasurementParser.Review("",message);
        Assert.Equal(920m,review.Measurements.Single(x=>x.Label=="göğüs çevresi").Millimeters);
        Assert.Equal(720m,review.Measurements.Single(x=>x.Label=="bel çevresi").Millimeters);
        Assert.Empty(review.Unrecognized);
        Assert.All(review.Measurements,x=>Assert.All(x.SourceExpressions,source=>Assert.Contains(source,message)));
    }
    [Theory]
    [InlineData("göğüs çevresi 92 cm ve\nbel çevresi 72")]
    [InlineData("göğüs çevresi 92 cm ve\r\nbel çevresi yaklaşık 72 cm")]
    [InlineData("göğüs çevresi 92 cm ve\r\nbel çevresi 0 cm")]
    [InlineData("göğüs çevresi 92 cm ve\r\nbel çevresi 72 cm ve göğüs çevresi 93 cm")]
    [InlineData("göğüs çevresi 92 cm ve\r\nbel çevresi 72 cm değil")]
    [InlineData("göğüs çevresi 92 cm ve\n \nbel çevresi 72")]
    [InlineData("göğüs çevresi 92 cm ve\u00a0\n\u00a0\nbel çevresi 72")]
    [InlineData("göğüs çevresi 92 cm ve\u2028\u2003\u2029bel çevresi yaklaşık 72 cm")]
    [InlineData("göğüs çevresi 92 cm\u0085\u00a0\u0085ve bel çevresi 72 cm değil")]
    [InlineData("göğüs çevresi 92 cm ve\r\n\t\r\n  \r\nbel çevresi yaklaşık 72 cm")]
    [InlineData("göğüs çevresi 92 cm\r\n \r\nve bel çevresi 72 cm değil")]
    [InlineData("göğüs çevresi 92 cm\r\nve başka ölçü 72 cm")]
    public void InvalidJoinedLinesCannotPartiallyAcceptEitherMeasurement(string message)
    {
        var review=DressMeasurementParser.Review("",message+"\nkalça çevresi 100 cm");
        Assert.DoesNotContain(review.Measurements,x=>x.Label=="göğüs çevresi" || x.Label=="bel çevresi");
        Assert.Equal(1000m,review.Measurements.Single(x=>x.Label=="kalça çevresi").Millimeters);
        Assert.Contains(message,review.Unrecognized);
    }
    [Fact]
    public void SemicolonStillSeparatesExpressionsEvenAfterConjunction()
    {
        var review=DressMeasurementParser.Review("","göğüs çevresi 92 cm ve;\r\n\t\r\nbel çevresi 72 cm");
        Assert.DoesNotContain(review.Measurements,x=>x.Label=="göğüs çevresi");
        Assert.Equal(720m,review.Measurements.Single(x=>x.Label=="bel çevresi").Millimeters);
    }
    [Theory]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    [InlineData("\u0085")]
    [InlineData("\v")]
    [InlineData("\f")]
    public void UnicodeLineSeparatorsKeepIndependentMeasurementsIndependent(string separator)
    {
        var review=DressMeasurementParser.Review("","göğüs çevresi 92 cm"+separator+"bel çevresi 72 cm");
        Assert.Equal(920m,review.Measurements.Single(x=>x.Label=="göğüs çevresi").Millimeters);
        Assert.Equal(720m,review.Measurements.Single(x=>x.Label=="bel çevresi").Millimeters);
    }
}
