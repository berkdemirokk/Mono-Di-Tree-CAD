using TekstilCAD.Desktop;
using TekstilCAD.Patterns;

namespace TekstilCAD.Tests;

public sealed class ALineSourceCorrectionWhitespaceTests
{
    [Fact]
    public void CorrectSingleWhitespaceVariantChangesOnlyNumericSpan()
    {
        const string prompt="Önceki tarif", notes="Özgün not;\r\nÖN\tBEL  YAYI: 35 cm;\r\nSon not";
        var plan=ALineSourceCorrection.Create(prompt,notes,0,"36",ALineLengthUnit.Centimeter);
        Assert.Equal(prompt,plan.Prompt);
        Assert.Equal("Özgün not;\r\nÖN\tBEL  YAYI: 36 cm;\r\nSon not",plan.Notes);
        Assert.Equal("35 cm",plan.OldText);Assert.Equal("36 cm",plan.NewText);
        Assert.Equal("Özgün not;\r\nÖN\tBEL  YAYI: 35 cm;\r\nSon not",notes);
    }
    [Fact]
    public void CorrectUnicodeWhitespacePreservesSourceOutsideValueSpan()
    {
        const string notes="not;ön\u00a0bel yayı\u00a035\u00a0cm;son";
        var plan=ALineSourceCorrection.Create("",notes,0,"36",ALineLengthUnit.Centimeter);
        Assert.Equal("not;ön\u00a0bel yayı\u00a036 cm;son",plan.Notes);
        Assert.Equal("35\u00a0cm",plan.OldText);
        Assert.Equal("not;ön\u00a0bel yayı\u00a035\u00a0cm;son",notes);
    }
    [Theory]
    [InlineData("ön bel yayı\u00a035 cm\u00a0ve\u00a0arka bel yayı 37 cm")]
    [InlineData("ön bel yayı\u202835 cm")]
    [InlineData("ön  bel yayı 35 cm ve arka bel yayı 37 cm")]
    [InlineData("ön  bel yayı 35 cm ve arka bel yayı 37 cm değil")]
    [InlineData("ön  bel yayı 35 cm?")]
    [InlineData("ön  bel yayi 35 cm")]
    public void GroupAndAmbiguousSourcesAreNeverAutomaticallyRewritten(string notes)
    {
        Assert.Throws<ArgumentException>(()=>ALineSourceCorrection.Create("",notes,0,"36",ALineLengthUnit.Centimeter));
    }
}
