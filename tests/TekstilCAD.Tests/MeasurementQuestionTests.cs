using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;
public class MeasurementQuestionTests
{
    [Theory]
    [InlineData("bel çevresi 72 cm?")]
    [InlineData("BEL ÇEVRESİ 720 MM ?")]
    [InlineData("göğüs çevresi 96 cm ve bel çevresi 72 cm?")]
    [InlineData("göğüs çevresi 96 cm? ve bel çevresi 72 cm")]
    [InlineData("göğüs çevresi 96 cm ve\r\nbel çevresi 72 cm?")]
    public void Question_is_not_a_measurement_and_independent_sentinel_survives(string question)
    {
        var review=DressMeasurementParser.Review(question,"kalça çevresi 100 cm");
        Assert.DoesNotContain(review.Measurements,m=>m.Label is "bel çevresi" or "göğüs çevresi");
        Assert.Equal(1000m,Assert.Single(review.Measurements).Millimeters);
        Assert.Contains(question,review.Unrecognized);Assert.Contains(review.Questions,q=>q.StartsWith("bel çevresi: ifade belirsiz"));
        var request=new DesignRequest(question,"kalça çevresi 100 cm",null);
        var loaded=DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(request));
        Assert.Equal(question,loaded.Prompt);Assert.Equal(request.Measurements,loaded.Measurements);
        Assert.Null(ALineBodyTotals.Review(loaded.Prompt,loaded.Measurements).WaistMm);
    }
    [Theory]
    [InlineData("bel çevresi 72 cm")]
    [InlineData("bel çevresi 72 cm.")]
    [InlineData("bel çevresi 72 cm!")]
    [InlineData("bel çevresi 72 cm olsun.")]
    public void Affirmative_punctuation_still_works(string text)
    { Assert.Equal(720m,Assert.Single(DressMeasurementParser.Review(text,"").Measurements).Millimeters); }
    [Theory]
    [InlineData("bel çevresi 72 cm?")]
    [InlineData("bel çevresi 73 cm?")]
    public void Question_invalidates_prior_equal_or_different_value_and_coupled_group(string question)
    {
        var review=DressMeasurementParser.Review("göğüs çevresi 96 cm ve bel çevresi 72 cm",question+"; kalça çevresi 100 cm");
        Assert.DoesNotContain(review.Measurements,m=>m.Label is "bel çevresi" or "göğüs çevresi");
        Assert.Equal(1000m,Assert.Single(review.Measurements).Millimeters);
    }
    [Fact] public void Question_clause_cannot_make_a_long_model_request_unambiguous()
    {
        Assert.Null(ChatModelChoice.Review("A kesim elbise istiyorum; bel çevresi 72 cm?").ModelId);
        Assert.Equal("aline",ChatModelChoice.Review("A kesim elbise istiyorum; bel çevresi 72 cm.").ModelId);
    }
}
