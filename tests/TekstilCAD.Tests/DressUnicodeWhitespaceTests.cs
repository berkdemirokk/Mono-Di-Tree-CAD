using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class DressUnicodeWhitespaceTests
{
    [Theory]
    [InlineData("bel çevresi 36\u00a0cm")]
    [InlineData("bel çevresi 36\u202fcm")]
    [InlineData("bel çevresi 36\u2009cm")]
    [InlineData("bel\u00a0çevresi: 36\u00a0cm")]
    public void ExplicitHorizontalUnicodeSpaceIsAcceptedWithoutRewritingSource(string text)
    {
        var review = DressMeasurementParser.Review(text, "");
        var measurement = Assert.Single(review.Measurements, x => x.Label == "bel çevresi");
        Assert.Equal(360m, measurement.Millimeters);
        Assert.Contains(text, measurement.SourceExpressions);
    }
    [Theory]
    [InlineData("bel çevresi 36\u00a0cm değil")]
    [InlineData("bel çevresi 36\u202fcm?")]
    [InlineData("36\u00a0cm")]
    [InlineData("bel çevresi 36\u2028cm")]
    [InlineData("bel çevresi 36\u0085cm")]
    public void NoNegatedBareOrSplitValueIsAsserted(string text)
    {
        var review = DressMeasurementParser.Review(text, "");
        Assert.DoesNotContain(review.Measurements, x => x.Label == "bel çevresi");
    }
}
