using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public sealed class ChatModelChoiceTests
{
    [Theory]
    [InlineData("A KESİM ELBİSE İSTİYORUM","aline")]
    [InlineData("A KESIM ELBISE ISTIYORUM","aline")]
    [InlineData("  Lütfen a-kesim olsun.  ","aline")]
    [InlineData("modelim: anvelop","wrap")]
    [InlineData("Balenli korsaj seçelim","corset")]
    [InlineData("KILIF ELBİSE","sheath")]
    [InlineData("düz kesim elbise seç","straight")]
    [InlineData("gömlek tipi elbise istiyorum","shirt")]
    public void ExplicitNamesAndSupportedSynonymsMapToPortableIdentity(string text,string id)=>Assert.Equal(id,ChatModelChoice.Review(text).ModelId);
    [Theory]
    [InlineData("A kesim değil")][InlineData("A kesim istemiyorum")][InlineData("A kesim veya anvelop")]
    [InlineData("gömlek elbise mi?")][InlineData("anvelop yerine düz elbise")][InlineData("A kesim ama seçme")]
    [InlineData("\"A kesim\"")][InlineData("A kesim istiyor musun")][InlineData("A kesim; bel çevresi 72 cm")]
    [InlineData("anvelopsuz")][InlineData("bel çevresi 72 cm")]
    public void NegativeAmbiguousQuotedAndMixedMessagesNeverSelect(string text)=>Assert.Null(ChatModelChoice.Review(text).ModelId);
}
