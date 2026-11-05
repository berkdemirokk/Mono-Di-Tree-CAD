using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ALineRequestMeasurementsTests
{
    private static string AllFields=>string.Join(";",ALineRequestMeasurements.Labels.Zip(new[]{"36","36","50","50","2","2","2","2","20","70","160"},(label,value)=>label+" "+value+" cm"));

    [Fact] public void SeparateFieldsPreserveExplicitTotalsAndOriginalEvidence()
    {
        var text="bel çevresi 72 cm;kalça çevresi 100 cm;"+AllFields;
        Assert.Equal(new ALineBodyTotals(720m,1000m),ALineBodyTotals.Review(text,""));
        var fields=ALineRequestMeasurements.Review(text,"");
        Assert.True(fields.Complete);Assert.Empty(fields.Issues);
        Assert.Equal(new decimal?[]{360,360,500,500,20,20,20,20,200,700,1600},fields.Millimeters);
        var totals=DressMeasurementParser.Review(text,"");
        Assert.Contains("Ön bel yayı 36 cm",totals.Unrecognized);
        Assert.Equal("bel çevresi 72 cm",totals.Measurements.Single(m=>m.Label=="bel çevresi").SourceExpressions.Single());
    }
    [Fact] public void MissingEaseIsNotInferredAndEquivalentDuplicatesAreAccepted()
    {
        var partial=ALineRequestMeasurements.Review("ön bel yayı 36 cm","ÖN BEL YAYI 360 mm");
        Assert.False(partial.Complete);Assert.Empty(partial.Issues);Assert.Equal(360m,partial.Millimeters[0]);Assert.Null(partial.Millimeters[4]);
        Assert.Equal(0m,ALineRequestMeasurements.Review("ön bel yayı bolluğu 0 mm","").Millimeters[4]);
    }
    [Theory]
    [InlineData("ön bel yayı 36 cm?")]
    [InlineData("ön bel yayı yaklaşık 36 cm")]
    [InlineData("ön bel yayı 36 cm değil")]
    [InlineData("ön bel yayı 36 cm ve arka bel yayı 36 cm")]
    [InlineData("ön bel yayı 36")]
    [InlineData("ön bel yayı 0 cm")]
    [InlineData("ön bel yayı 10001 mm")]
    [InlineData("ön bel yayı 36 cm;ön bel yayı 37 cm")]
    [InlineData("ön bel yayı 36 cm?;ön bel yayı 36 cm")]
    [InlineData("eğer kumaş ipekse;ön bel yayı 36 cm")]
    public void AmbiguousConflictingOrConditionalFieldsCannotBeProposed(string text)
    {
        var result=ALineRequestMeasurements.Review(text,"");Assert.False(result.Complete);Assert.NotEmpty(result.Issues);
    }
    [Fact] public void CrossFieldConditionsAndConflictingTotalsRemainBlocked()
    {
        Assert.NotEmpty(ALineRequestMeasurements.Review(AllFields,"eğer kumaş ipekse").Issues);
        Assert.NotEmpty(ALineRequestMeasurements.Review(AllFields,"bel çevresi 80 cm").Issues);
        Assert.NotEmpty(ALineRequestMeasurements.Review(AllFields,"bel çevresi 72 cm;bel çevresi 80 cm").Issues);
        Assert.NotEmpty(ALineRequestMeasurements.Review(AllFields,"bel çevresi 72 cm?").Issues);
        Assert.NotEmpty(ALineRequestMeasurements.Review("ön bel yayı 36 cm","ön bel yayı 37 cm").Issues);
        Assert.Null(ALineBodyTotals.Review("bel çevresi 72 cm?;ön bel yayı 36 cm", "").WaistMm);
        Assert.Null(ALineBodyTotals.Review("bel çevresi 72 cm;ön bel yayı 36 cm", "eğer kumaş ipekse").WaistMm);
    }
    [Fact] public void TurkishAndDecomposedNamesDoNotChangeFieldMeaning()
    {
        Assert.Equal(360m,ALineRequestMeasurements.Review("ÖN BEL YAYI 36 cm","").Millimeters[0]);
        Assert.Equal(360m,ALineRequestMeasurements.Review("O\u0308n bel yayı 36 cm","").Millimeters[0]);
        Assert.Equal(720m,ALineBodyTotals.Review("bel çevresi 72 cm;ÖN BEL YAYI 36 cm","").WaistMm);
    }
    [Fact] public void ElevenFieldsNeverInventATotalMeasurement()
    {
        Assert.Equal(new ALineBodyTotals(null,null),ALineBodyTotals.Review(AllFields,""));
        Assert.True(ALineRequestMeasurements.Review(AllFields,"").Complete);
    }
}
