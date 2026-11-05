using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class IphoneDescriptionInteropTests
{
    [Fact]
    public void ApprovedIphoneFieldsAndOriginalSentenceSurviveRequestRoundTrip()
    {
        // Mirrors the iPhone V1 contract; it is not an actual device export.
        const string sentence = "A kesim elbise istiyorum uzun kollu bel çevresi 72 cm kalça çevresi 100 cm";
        var original = new DesignRequest("Seçilen elbise modeli: A kesim elbise\n" + sentence,
            "Bel çevresi: 72 cm\nKalça çevresi: 100 cm", null);
        var restored = DesignRequestStorage.Deserialize(DesignRequestStorage.Serialize(original));
        Assert.Equal(original.Prompt, restored.Prompt);
        Assert.Equal(original.Measurements, restored.Measurements);
        // This branch's Windows parser still flags the long description as
        // unclear, even when approved fields agree. It must not silently claim
        // the resulting values are ready for production.
        var review = DressMeasurementParser.Review(restored.Prompt, restored.Measurements);
        Assert.DoesNotContain(review.Measurements, item => item.Label == "bel çevresi" || item.Label == "kalça çevresi");
        Assert.Contains(review.Questions, question => question.StartsWith("bel çevresi"));
        Assert.Contains(review.Questions, question => question.StartsWith("kalça çevresi"));
    }
}
