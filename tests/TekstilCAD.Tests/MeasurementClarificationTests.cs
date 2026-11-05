using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;
public class MeasurementClarificationTests
{
    [Theory]
    [InlineData("bel çevresi 72 cm?\nbel çevresi 72 cm")]
    [InlineData("BEL ÇEVRESİ 72 CM ?\r\nbel çevresi 720 mm.")]
    [InlineData("bel çevresi 72,5 cm?;bel çevresi 725 mm!")]
    public void Exact_later_equivalent_reply_resolves_only_that_question(string text)
    {
        var review=DressMeasurementParser.Review(text,"kalça çevresi 100 cm");
        Assert.Contains(review.Measurements,m=>m.Label=="bel çevresi");Assert.Contains(review.Measurements,m=>m.Label=="kalça çevresi"&&m.Millimeters==1000);
        Assert.DoesNotContain(review.Questions,q=>q.StartsWith("bel çevresi"));
        var loaded=DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(new DesignRequest(text,"kalça çevresi 100 cm",null)));
        Assert.Equal(text,loaded.Prompt);Assert.Equal(review.Measurements.First(m=>m.Label=="bel çevresi").Millimeters,ALineBodyTotals.Review(loaded.Prompt,loaded.Measurements).WaistMm);
        Assert.DoesNotContain(review.Measurements.Single(m=>m.Label=="bel çevresi").SourceExpressions,s=>s.Contains('?'));
    }
    [Theory]
    [InlineData("bel çevresi 73 cm")]
    [InlineData("bel çevresi yaklaşık 72 cm")]
    [InlineData("bel çevresi 72 cm değil")]
    [InlineData("bel çevresi 72 cm olsun")]
    [InlineData("bel çevresi 72 cm olursa")]
    [InlineData("bel çevresi 72 cm?")]
    [InlineData("bel çevresi 72 cm ve göğüs çevresi 96 cm")]
    [InlineData("bel çevresi 72 cm;bel çevresi 72 cm")]
    [InlineData("bel çevresi 72 cm;bel çevresi 70 cm")]
    public void Ambiguous_conditional_conflicting_or_extra_reply_never_resolves(string reply)
    {
        var review=DressMeasurementParser.Review("bel çevresi 72 cm?\n"+reply,"kalça çevresi 100 cm");
        Assert.DoesNotContain(review.Measurements,m=>m.Label=="bel çevresi");Assert.Contains(review.Questions,q=>q.StartsWith("bel çevresi: ifade belirsiz"));
        Assert.Contains(review.Measurements,m=>m.Label=="kalça çevresi"&&m.Millimeters==1000);
    }
    [Theory]
    [InlineData("bel çevresi 72 cm\nbel çevresi 72 cm?")]
    [InlineData("bel çevresi yaklaşık 72 cm;bel çevresi 72 cm?;bel çevresi 72 cm")]
    [InlineData("göğüs çevresi 96 cm ve bel çevresi 72 cm?;bel çevresi 72 cm")]
    [InlineData("bel çevresi 72 cm?;göğüs çevresi 96 cm ve bel çevresi 72 cm")]
    [InlineData("bel çevresi 72 cm değil?;bel çevresi 72 cm")]
    [InlineData("bel çevresi 72 cm olursa?;bel çevresi 72 cm")]
    [InlineData("bel çevresi 72 cm?;bel çevresi 72 cm?;bel çevresi 72 cm")]
    [InlineData("bel çevresi 72 cm?;emin değilim;bel çevresi 72 cm")]
    public void Non_exact_question_prior_answer_or_connected_group_stays_unclear(string text)
    { Assert.DoesNotContain(DressMeasurementParser.Review(text,"").Measurements,m=>m.Label=="bel çevresi"); }
    [Theory]
    [InlineData("bel çevresi 72 cm?","bel çevresi 72 cm")]
    [InlineData("bel çevresi 72 cm","bel çevresi 72 cm?")]
    [InlineData("bel çevresi 72 cm?\nbel çevresi 72 cm","bel çevresi 72 cm")]
    public void No_order_is_invented_between_description_and_notes(string prompt,string notes)
    { Assert.DoesNotContain(DressMeasurementParser.Review(prompt,notes).Measurements,m=>m.Label=="bel çevresi"); }
    [Fact] public void Same_field_notes_can_resolve_without_modifying_originals()
    {
        const string notes="bel çevresi 72 cm?\nbel çevresi 720 mm";
        var request=new DesignRequest("A kesim",notes,null);var loaded=DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(request));
        Assert.Equal(notes,loaded.Measurements);Assert.Equal(720m,Assert.Single(DressMeasurementParser.Review(loaded.Prompt,loaded.Measurements).Measurements).Millimeters);
    }
}
