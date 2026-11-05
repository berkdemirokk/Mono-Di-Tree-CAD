using TekstilCAD.FileFormats;
namespace TekstilCAD.Tests;
public sealed class ChatModelChoiceTests
{
    [Fact] public void MultipleDetailsAndMeasurementsRetainSourceAndModelApproval()
    {
        const string text="A kesim elbise istiyorum uzun kollu bel çevresi 72 cm kalça çevresi 100 cm";
        var suggestion=ChatModelChoice.Review(text);
        Assert.Equal("aline",suggestion.ModelId);Assert.Contains("onay",suggestion.Explanation);
        var review=DressMeasurementParser.Review(text,"");
        Assert.Contains(review.Measurements,m=>m.Label=="bel çevresi" && m.Millimeters==720 && m.SourceExpressions.Contains(text));
        Assert.Contains(review.Measurements,m=>m.Label=="kalça çevresi" && m.Millimeters==1000 && m.SourceExpressions.Contains(text));
        Assert.Empty(review.Unrecognized);
    }
    [Theory]
    [InlineData("A kesim elbise istiyorum uzun kollu kısa kollu bel çevresi 72 cm")]
    [InlineData("A kesim elbise istiyorum uzun kollu bel çevresi 72 cm bel çevresi 90 cm")]
    [InlineData("A kesim elbise istiyorum uzun kollu bel çevresi 72 cm değil")]
    [InlineData("A kesim elbise istiyorum uzun kollu bel çevresi 72 cm?")]
    [InlineData("A kesim elbise istiyorum uzun kollu bel çevresi 72 cm olursa")]
    [InlineData("A kesim elbise istiyorum uzun kollu bel çevresi 72 cm anvelop")]
    [InlineData("A kesim elbise istiyorum uzun kollu bel çevresi 72 cm bilinmeyen")]
    public void MultiDetailUnknownOrConflictingInputIsNotPartiallyAccepted(string text)
    {
        Assert.Null(ChatModelChoice.Review(text).ModelId);
        Assert.Empty(DressMeasurementParser.Review(text,"").Measurements);
    }
    [Fact] public void MultiDetailEquivalentRepeatedMeasurementsAndLimits()
    {
        const string text="Anvelop istiyorum v yaka bel çevresi 72,5 cm bel çevresi 725 mm";
        Assert.Equal("wrap",ChatModelChoice.Review(text).ModelId);
        Assert.Equal(725m,Assert.Single(DressMeasurementParser.Review(text,"").Measurements).Millimeters);
        var excessive="A kesim istiyorum "+string.Join(" ",Enumerable.Repeat("uzun kollu",33));
        Assert.Null(ChatModelChoice.Review(excessive).ModelId);
    }
    [Fact] public void MultiDetailMeasurementsConflictAsOneGroupWithOtherField()
    {
        var review=DressMeasurementParser.Review("A kesim istiyorum uzun kollu bel çevresi 72 cm kalça çevresi 100 cm","bel çevresi 90 cm");
        Assert.Empty(review.Measurements);
        Assert.Contains(review.Questions,q=>q.StartsWith("kalça çevresi:") && q.Contains("belirsiz"));
    }
    [Theory]
    [InlineData("A kesim","Yeni tarif\nA kesim","",true)]
    [InlineData("A kesim","Yeni tarif\nA kesim","yeni mesaj",false)]
    [InlineData("A kesim","Yeni tarif\nAnvelop","",false)]
    [InlineData("A kesim","A kesim\nA kesim","",false)]
    [InlineData("A kesim","A kesim\nsonraki not","",false)]
    public void RetryRequiresUniqueUnchangedFinalMessageAndNoUnsentInput(string last,string prompt,string unsent,bool allowed)
        =>Assert.Equal(allowed,ChatModelChoice.CanReevaluate(last,prompt,unsent));
    [Theory]
    [InlineData("du\u0308z elbise","straight")]
    [InlineData("go\u0308mlek elbise","shirt")]
    [InlineData("A KESI\u0307M ELBI\u0307SE I\u0307STI\u0307YORUM","aline")]
    [InlineData("A kesim, kirmizi","aline")]
    public void AnalysisAcceptsCanonicalEquivalentsWithoutRewritingInput(string text,string id)
    {
        var original=text.ToCharArray();
        Assert.Equal(id,ChatModelChoice.Review(text).ModelId);
        Assert.Equal(original,text.ToCharArray());
    }
    [Fact] public void MalformedUnicodeDoesNotThrowOrSuggest()
    {
        var result=ChatModelChoice.Review("düz elbise\uD800");
        Assert.Null(result.ModelId);Assert.Contains("Unicode",result.Explanation);
    }
    [Theory]
    [InlineData("du\u0308z elbise değil")]
    [InlineData("go\u0308mlek elbisesiz")]
    public void NormalizationDoesNotRemoveNegationOrWordBoundaries(string text)=>Assert.Null(ChatModelChoice.Review(text).ModelId);
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
    [InlineData("A kesim elbise istiyorum uzun kollu","aline")]
    [InlineData("Lütfen anvelop elbise olsun v yaka","wrap")]
    [InlineData("Balenli korsaj istiyorum saten","corset")]
    [InlineData("Kılıf elbise istiyorum siyah","sheath")]
    [InlineData("Düz elbise olsun midi boy","straight")]
    [InlineData("Gömlek elbise istiyorum kolsuz; bel çevresi 72 cm","shirt")]
    [InlineData("A kesim istiyorum uzun kollu, uzun kollu","aline")]
    public void AffirmativeModelAndOneUnpunctuatedKnownDetailAreSuggested(string text,string id)
    {
        var result=ChatModelChoice.Review(text);
        Assert.Equal(id,result.ModelId);
        Assert.Contains("onay",result.Explanation);
    }
    [Theory]
    [InlineData("A kesim elbise istemiyorum uzun kollu")]
    [InlineData("A kesim elbise istiyorum uzun kollu değil")]
    [InlineData("A kesim elbise istiyorum uzun kollu mu?")]
    [InlineData("A kesim elbise istiyorum uzun kollu olursa")]
    [InlineData("A kesim elbise istiyorum uzun kollu belki")]
    [InlineData("A kesim elbise istiyorum uzun kollu kısa kollu")]
    [InlineData("A kesim elbise istiyorum uzun kollu, kısa kollu")]
    [InlineData("A kesim elbise istiyorum uzun kollu anvelop elbise")]
    [InlineData("A kesim elbise istiyorum 'uzun kollu'")]
    [InlineData("A kesim elbise uzun kollu")]
    public void UnpunctuatedCombinationRejectsUnclearOrConflictingText(string text)=>Assert.Null(ChatModelChoice.Review(text).ModelId);
    [Theory]
    [InlineData("A kesim elbise istiyorum bel çevresi 72 cm","aline",720)]
    [InlineData("Anvelop elbise olsun göğüs çevresi 92,5 cm","wrap",925)]
    [InlineData("Gömlek elbise istiyorum kol boyu 600 mm","shirt",600)]
    public void ExplicitModelAndMeasurementShareOriginalEvidence(string text,string model,int mm)
    {
        Assert.Equal(model,ChatModelChoice.Review(text).ModelId);
        var measurement=Assert.Single(DressMeasurementParser.Review(text,"").Measurements);
        Assert.Equal(mm,measurement.Millimeters);
        Assert.Contains(text,measurement.SourceExpressions);
    }
    [Theory]
    [InlineData("A kesim elbise istemiyorum bel çevresi 72 cm")]
    [InlineData("A kesim elbise istiyorum bel çevresi 72 cm?")]
    [InlineData("A kesim elbise istiyorum bel çevresi 72 cm değil")]
    [InlineData("A kesim elbise istiyorum bel çevresi 72 cm olursa")]
    [InlineData("A kesim elbise istiyorum bel çevresi 72 cm belki")]
    [InlineData("A kesim elbise veya anvelop istiyorum bel çevresi 72 cm")]
    [InlineData("A kesim elbise istiyorum bel çevresi 72 cm bel çevresi 90 cm")]
    public void ModelAndMeasurementRejectUnclearExpressions(string text)
    {
        Assert.Null(ChatModelChoice.Review(text).ModelId);
        Assert.Empty(DressMeasurementParser.Review(text,"").Measurements);
    }
    [Fact] public void CombinedMeasurementStillConflictsWithPriorValue()
    {
        var review=DressMeasurementParser.Review("A kesim elbise istiyorum bel çevresi 72 cm","bel çevresi 90 cm");
        Assert.Empty(review.Measurements);
        Assert.Contains(review.Questions,q=>q.Contains("bel çevresi") && q.Contains("çeliş"));
        Assert.Null(ChatModelChoice.Review("A kesim elbise istiyorum bel çevresi 72 cm; bel çevresi 90 cm").ModelId);
        var question=DressMeasurementParser.Review("bel çevresi 72 cm?\nA kesim elbise istiyorum bel çevresi 72 cm","");
        Assert.Empty(question.Measurements);
        Assert.Contains(question.Questions,q=>q.Contains("bel çevresi") && q.Contains("belirsiz"));
    }
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
