using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;
public class ConditionalMeasurementTests
{
    [Theory]
    [InlineData("eğer kumaş ipekse; bel çevresi 72 cm","")]
    [InlineData("eğer kumaş ipekse; A kesim elbise istiyorum bel çevresi 72 cm","")]
    [InlineData("A kesim elbise istiyorum bel çevresi 72 cm","şayet kumaş ipekse")]
    [InlineData("bel çevresi 72 cm; eğer kumaş ipekse","")]
    [InlineData("eğer kumaş ipekse\nbel çevresi 72 cm","")]
    [InlineData("EĞER kumaş ipekse\u2028BEL ÇEVRESİ 720 MM","")]
    [InlineData("eğer kumaş ipekse","bel çevresi 72 cm")]
    [InlineData("bel çevresi 72 cm","eğer kumaş ipekse")]
    [InlineData("eğer kumaş ipekse; bel çevresi 72 cm?; bel çevresi 72 cm","")]
    [InlineData("bel çevresi 72 cm?; bel çevresi 72 cm","eğer kumaş ipekse")]
    [InlineData("bel çevresi 72 cm?","bel çevresi 72 cm olursa;kalça çevresi 100 cm")]
    [InlineData("eğer kumaş ipekse; göğüs çevresi 96 cm ve bel çevresi 72 cm","kalça çevresi 100 cm;elbise boyu 110 cm;kol boyu 62 cm")]
    public void Unknown_condition_scope_never_produces_definite_measurements(string prompt,string notes)
    {
        var review=DressMeasurementParser.Review(prompt,notes);Assert.Empty(review.Measurements);
        Assert.Contains(review.Questions,q=>q.Contains("koşullu ifadenin"));Assert.Contains(review.Questions,q=>q.StartsWith("bel çevresi: ifade belirsiz"));
        var loaded=DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(new DesignRequest(prompt,notes,null)));
        Assert.Equal(prompt,loaded.Prompt);Assert.Equal(notes,loaded.Measurements);
        Assert.Empty(DressMeasurementParser.Review(loaded.Prompt,loaded.Measurements).Measurements);
        Assert.Null(ALineBodyTotals.Review(prompt,notes).WaistMm);Assert.Null(ALineBodyTotals.Review(prompt,notes).HipMm);
    }
    [Theory]
    [InlineData("şayet")]
    [InlineData("gerekirse")]
    [InlineData("koşuluyla")]
    [InlineData("şartıyla")]
    [InlineData("takdirde")]
    [InlineData("olursa")]
    [InlineData("olmazsa")]
    [InlineData("değilse")]
    [InlineData("İSE")]
    public void Explicit_whole_word_triggers_are_conservative(string token)
    { Assert.Empty(DressMeasurementParser.Review(token+";bel çevresi 72 cm","").Measurements); }
    [Theory]
    [InlineData("elbise")]
    [InlineData("gömlek elbise istiyorum")]
    [InlineData("değerlendirme")]
    [InlineData("ise123")]
    [InlineData("xise")]
    [InlineData("ise_etiket")]
    public void Ordinary_words_or_embedded_substrings_do_not_trigger(string description)
    { Assert.Equal(720m,Assert.Single(DressMeasurementParser.Review(description,"bel çevresi 72 cm.").Measurements).Millimeters); }
    [Fact] public void Condition_and_conflict_reasons_both_survive_question_resolution()
    {
        var review=DressMeasurementParser.Review("eğer kumaş ipekse;bel çevresi 72 cm?;bel çevresi 72 cm","bel çevresi 73 cm");
        Assert.Empty(review.Measurements);Assert.Contains(review.Questions,q=>q.StartsWith("bel çevresi")&&q.Contains("koşullu")&&q.Contains("çelişiyor"));
    }
    [Fact] public void Ordinary_positive_question_clarification_still_works_without_a_condition()
    {
        var review=DressMeasurementParser.Review("elbise;bel çevresi 72 cm?;bel çevresi 720 mm!","kalça çevresi 100 cm.");
        Assert.Equal(2,review.Measurements.Count);Assert.Equal(720m,review.Measurements.Single(m=>m.Label=="bel çevresi").Millimeters);
    }
}
