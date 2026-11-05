using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public sealed class ALineBodyTotalsTests
{
    [Fact] public void ExplicitTotalsUseMillimetersWithoutInferringMissingDistribution()
    {
        Assert.Equal(new ALineBodyTotals(720m,1000m),ALineBodyTotals.Review("bel çevresi 72 cm","kalça çevresi 1000 mm"));
        Assert.Equal(new ALineBodyTotals(null,1000m),ALineBodyTotals.Review("A kesim elbise","kalça çevresi 100 cm"));
    }
    [Theory]
    [InlineData("bel çevresi 72 cm; bel çevresi 80 cm")]
    [InlineData("bel çevresi yaklaşık 72 cm")]
    public void ConflictingOrQualifiedTotalsAreNotPresentedAsKnown(string text)=>Assert.Null(ALineBodyTotals.Review(text,"").WaistMm);
}
