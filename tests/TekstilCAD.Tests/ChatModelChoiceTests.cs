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
    [InlineData("\"A kesim\"")][InlineData("A kesim istiyor musun")]
    [InlineData("anvelopsuz")][InlineData("bel çevresi 72 cm")]
    public void NegativeAmbiguousQuotedAndMixedMessagesNeverSelect(string text)=>Assert.Null(ChatModelChoice.Review(text).ModelId);
    [Theory]
    [InlineData("A kesim elbise istiyorum, uzun kollu")]
    [InlineData("Uzun kollu, A KESİM ELBİSE İSTİYORUM; bel çevresi 72 cm")]
    [InlineData("A kesim; bel çevresi 72,5 cm; kalça çevresi 100 cm")]
    [InlineData("A-kesim olsun, v yaka, kırmızı, keten, diz altı")]
    public void SingleExplicitModelCanAccompanyKnownAffirmativeDetailsAndMeasurements(string text)=>Assert.Equal("aline",ChatModelChoice.Review(text).ModelId);
    [Theory]
    [InlineData("A kesim istiyorum, uzun kollu değil")]
    [InlineData("A kesim istiyorum, uzun kollu mu?")]
    [InlineData("A kesim istiyorum, olursa uzun kollu")]
    [InlineData("A kesim istiyorum, belki uzun kollu")]
    [InlineData("A kesim istiyorum, uzun kollu, kısa kollu")]
    [InlineData("A kesim istiyorum; bel çevresi 72 cm; bel çevresi 80 cm")]
    [InlineData("A kesim istiyorum, 'uzun kollu'")]
    [InlineData("A kesim istiyorum, anvelop değil")]
    [InlineData("A kesim istiyorum, ama başka model olabilir")]
    [InlineData("A kesim, A kesim")]
    [InlineData("Anvelop elbise tercih etmiyorum")]
    [InlineData("Anvelop elbise düşünmüyorum")]
    [InlineData("Anvelop elbise uygun mudur")]
    [InlineData("Anvelop elbise istemem, uzun kollu")]
    [InlineData("Anvelop elbise istiyorum, tercih etmiyorum")]
    [InlineData("Anvelop elbise istiyorum, uzun kollu olmayacak")]
    [InlineData("düz, uzun kollu")]
    public void UncertainOrConflictingLongDescriptionsNeverSuggest(string text)=>Assert.Null(ChatModelChoice.Review(text).ModelId);
}
