using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class IphoneDescriptionInteropTests
{
    [Theory]
    [InlineData(1, null)]
    [InlineData(2, "aline")]
    public void ApprovedIphoneFieldsAndOriginalSentenceSurviveRequestRoundTrip(int version,string? modelId)
    {
        // Mirrors the iPhone V1 contract; it is not an actual device export.
        const string sentence = "A kesim elbise istiyorum uzun kollu bel çevresi 72 cm kalça çevresi 100 cm";
        var original = new DesignRequest("Seçilen elbise modeli: A kesim elbise\n" + sentence,
            "Bel çevresi: 72 cm\nKalça çevresi: 100 cm", null, modelId, version);
        var json=version==1 ? DesignRequestStorage.Serialize(original) : DesignRequestStorage.SerializeV2(original);
        var restored = DesignRequestStorage.Deserialize(json);
        Assert.Equal(original.Prompt, restored.Prompt);
        Assert.Equal(original.Measurements, restored.Measurements);
        Assert.Equal(version,restored.Version);
        Assert.Equal(modelId,restored.ModelId);
        // The Windows parser recognizes these matching explicit values; this
        // is still a simulated interchange, not a device or fit validation.
        var review = DressMeasurementParser.Review(restored.Prompt, restored.Measurements);
        Assert.Equal(720m,Assert.Single(review.Measurements,item=>item.Label=="bel çevresi").Millimeters);
        Assert.Equal(1000m,Assert.Single(review.Measurements,item=>item.Label=="kalça çevresi").Millimeters);
        var conflict=DressMeasurementParser.Review(restored.Prompt,"Bel çevresi: 73 cm\nKalça çevresi: 100 cm");
        Assert.DoesNotContain(conflict.Measurements,item=>item.Label=="bel çevresi" || item.Label=="kalça çevresi");
        Assert.Contains(conflict.Questions,question=>question.StartsWith("bel çevresi") && question.Contains("çeliş"));
    }
}
