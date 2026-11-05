using TekstilCAD.FileFormats;

namespace TekstilCAD.Tests;

public sealed class ALineRequestMeasurementsTests
{
    [Theory]
    [InlineData("ön  bel yayı 36 cm",0,360)]
    [InlineData("ön\tbel yayı 36 cm",0,360)]
    [InlineData("ön bel  yayı 36 cm",0,360)]
    [InlineData("ön  bel yayı bolluğu 2 cm",4,20)]
    public void HorizontalWhitespaceBetweenExactLabelWordsIsAccepted(string text,int index,int expected)
    {
        var review=ALineRequestMeasurements.Review(text, "");
        Assert.Empty(review.Issues);Assert.Equal((decimal)expected,review.Millimeters[index]);
    }
    [Theory]
    [InlineData("ön bel yayi 36 cm")]
    [InlineData("on bel yayi 36 cm")]
    [InlineData("ÖN BEL YAYİ 36 CM")]
    [InlineData("arka kalca yayi 50 cm")]
    [InlineData("ön bel YAYİ BOLLUĞU 2 cm")]
    public void SimilarAsciiLabelsAreReportedButNeverParsed(string text)
    {
        var review=ALineRequestMeasurements.Review(text, "");
        Assert.NotEmpty(review.Issues);
        Assert.All(review.Millimeters, value=>Assert.Null(value));
    }

    [Fact] public void ExplicitConjoinedFieldsAreConsumedTogetherAndPreserveDistinctEase()
    {
        const string original="Ön bel yayı 36 cm ve arka bel yayı 360 mm ve ön bel yayı bolluğu 0 mm";
        var source=ALineRequestMeasurements.Review(original,"");
        Assert.Empty(source.Issues);Assert.Equal(360m,source.Millimeters[0]);Assert.Equal(360m,source.Millimeters[1]);Assert.Equal(0m,source.Millimeters[4]);Assert.Null(source.Millimeters[5]);
        Assert.Empty(ALineRequestMeasurements.Review("ön bel yayı 36 cm ve ön bel yayı 360 mm","").Issues);
        Assert.NotEmpty(ALineRequestMeasurements.Review("ön bel yayı 36 cm ve ön bel yayı 37 cm","").Issues);
    }
    [Theory]
    [InlineData("ön bel yayı 36 cm ve arka bel yayı 36 cm değil")]
    [InlineData("ön bel yayı 36 cm ve arka bel yayı 36 cm?")]
    [InlineData("ön bel yayı 36 cm ve arka bel yayı 36 cm eğer uygunsa")]
    [InlineData("ön bel yayı 36 cm ve kumaş yumuşak")]
    [InlineData("ön bel yayı 36 cm ve arka bel yayı 36")]
    [InlineData("ön bel yayı 36 cm ve arka bel yayı 36 cm ve")]
    public void IncompleteOrAmbiguousGroupNeverPublishesPartialValues(string text)
    {
        var source=ALineRequestMeasurements.Review(text,"");Assert.NotEmpty(source.Issues);Assert.Null(source.Millimeters[0]);Assert.Null(source.Millimeters[1]);
    }
    [Theory]
    [InlineData("A kesim elbise istiyorum, ölçülerimi birazdan vereceğim")]
    [InlineData("Lütfen bana A kesim elbise hazırla; ölçüleri daha sonra paylaşacağım.")]
    [InlineData("A-kesim olsun, ölçülerimi sonra vereceğim")]
    [InlineData("Model olarak A kesim seçelim; ölçüleri birazdan paylaşacağım")]
    public void ExplicitModelAndBoundedFutureIntentAllowExplicitLaterValues(string prompt)
    {
        var result=ALineRequestMeasurements.Review(prompt,"Ön bel yayı: 36 cm");
        Assert.Empty(result.Issues);Assert.Equal(360m,result.Millimeters[0]);Assert.Null(result.Millimeters[1]);
    }
    [Theory]
    [InlineData("A kesim elbise istiyorum, ölçülerimi birazdan vereceğim ama vazgeçtim")]
    [InlineData("A kesim elbise istiyorum, ölçülerimi birazdan vereceğim; ölçüler geçersiz")]
    [InlineData("A kesim elbise istiyorum, ölçülerimi birazdan vereceğim. Ölçüleri kullanmayın")]
    [InlineData("A kesim elbise istiyorum, ölçülerimi birazdan vereceğim?")]
    [InlineData("A kesim elbise istiyorum, ölçülerimi birazdan vereceğim eğer karar verirsem")]
    [InlineData("A kesim elbise istiyorum, ölçülerimi birazdan vereceğim; bel çevresi 72 cm")]
    [InlineData("Anvelop elbise istiyorum, ölçülerimi birazdan vereceğim")]
    [InlineData("A kesim elbise istemiyorum, ölçülerimi birazdan vereceğim")]
    [InlineData("A kesim elbise istiyorum, ölçüleri sonra paylaşmayacağım")]
    [InlineData("A kesim elbise istiyorum, ölçüleri sonra vereceğim; anvelop elbise olsun")]
    [InlineData("A kesim elbise istiyorum 72 cm, ölçüleri sonra vereceğim")]
    [InlineData("A kesim elbise istiyorum, ölçüleri sonra vereceğim, bel çevresi 72 cm")]
    public void FuturePhraseWithAdditionalOrContraryContentRemainsUnresolved(string prompt)
    {
        Assert.NotEmpty(ALineRequestMeasurements.Review(prompt,"Ön bel yayı: 36 cm").Issues);
    }
    private static string AllFields=>string.Join(";",ALineRequestMeasurements.Labels.Zip(new[]{"36","36","50","50","2","2","2","2","20","70","160"},(label,value)=>label+" "+value+" cm"));

    [Fact] public void SeparateFieldsPreserveExplicitTotalsAndOriginalEvidence()
    {
        var text="bel çevresi 72 cm;kalça çevresi 100 cm;"+AllFields;
        Assert.Equal(new ALineBodyTotals(720m,1000m),ALineBodyTotals.Review(text,""));
        var fields=ALineRequestMeasurements.Review(text,"");
        Assert.True(fields.Complete);Assert.Empty(fields.Issues);
        Assert.Equal(new decimal?[]{360,360,500,500,20,20,20,20,200,700,1600},fields.Millimeters);
        var totals=DressMeasurementParser.Review(text,"");
        Assert.Contains("Ön bel yayı 36 cm",totals.Unrecognized);
        Assert.Equal("bel çevresi 72 cm",totals.Measurements.Single(m=>m.Label=="bel çevresi").SourceExpressions.Single());
    }
    [Fact] public void MissingEaseIsNotInferredAndEquivalentDuplicatesAreAccepted()
    {
        var partial=ALineRequestMeasurements.Review("ön bel yayı 36 cm","ÖN BEL YAYI 360 mm");
        Assert.False(partial.Complete);Assert.Empty(partial.Issues);Assert.Equal(360m,partial.Millimeters[0]);Assert.Null(partial.Millimeters[4]);
        Assert.Equal(0m,ALineRequestMeasurements.Review("ön bel yayı bolluğu 0 mm","").Millimeters[4]);
    }
    [Theory]
    [InlineData("ön bel yayı 36 cm?")]
    [InlineData("ön bel yayı yaklaşık 36 cm")]
    [InlineData("ön bel yayı 36 cm değil")]
    [InlineData("ön bel yayı 36")]
    [InlineData("ön bel yayı 0 cm")]
    [InlineData("ön bel yayı 10001 mm")]
    [InlineData("ön bel yayı 36 cm;ön bel yayı 37 cm")]
    [InlineData("ön bel yayı 36 cm?;ön bel yayı 36 cm")]
    [InlineData("eğer kumaş ipekse;ön bel yayı 36 cm")]
    public void AmbiguousConflictingOrConditionalFieldsCannotBeProposed(string text)
    {
        var result=ALineRequestMeasurements.Review(text,"");Assert.False(result.Complete);Assert.NotEmpty(result.Issues);
    }
    [Fact] public void CrossFieldConditionsAndConflictingTotalsRemainBlocked()
    {
        Assert.NotEmpty(ALineRequestMeasurements.Review(AllFields,"eğer kumaş ipekse").Issues);
        Assert.NotEmpty(ALineRequestMeasurements.Review(AllFields,"bel çevresi 80 cm").Issues);
        Assert.NotEmpty(ALineRequestMeasurements.Review(AllFields,"bel çevresi 72 cm;bel çevresi 80 cm").Issues);
        Assert.NotEmpty(ALineRequestMeasurements.Review(AllFields,"bel çevresi 72 cm?").Issues);
        Assert.NotEmpty(ALineRequestMeasurements.Review("ön bel yayı 36 cm","ön bel yayı 37 cm").Issues);
        Assert.Null(ALineBodyTotals.Review("bel çevresi 72 cm?;ön bel yayı 36 cm", "").WaistMm);
        Assert.Null(ALineBodyTotals.Review("bel çevresi 72 cm;ön bel yayı 36 cm", "eğer kumaş ipekse").WaistMm);
    }
    [Fact] public void TurkishAndDecomposedNamesDoNotChangeFieldMeaning()
    {
        Assert.Equal(360m,ALineRequestMeasurements.Review("ÖN BEL YAYI 36 cm","").Millimeters[0]);
        Assert.Equal(360m,ALineRequestMeasurements.Review("O\u0308n bel yayı 36 cm","").Millimeters[0]);
        Assert.Equal(720m,ALineBodyTotals.Review("bel çevresi 72 cm;ÖN BEL YAYI 36 cm","").WaistMm);
    }
    [Fact] public void ElevenFieldsNeverInventATotalMeasurement()
    {
        Assert.Equal(new ALineBodyTotals(null,null),ALineBodyTotals.Review(AllFields,""));
        Assert.True(ALineRequestMeasurements.Review(AllFields,"").Complete);
    }
    [Theory]
    [InlineData("bu ölçüler geçerli değil")]
    [InlineData("önceki ölçüleri kullanmayın")]
    [InlineData("BU ÖLÇÜLER DOĞRU DEĞİLDİR")]
    [InlineData("ölçüleri geri çekiyorum")]
    [InlineData("ölçüler yanlış")]
    [InlineData("ölçüleri dikkate almayın")]
    [InlineData("ölçüler doğru değilmiş")]
    [InlineData("ölçüleri kullanmayacağım")]
    [InlineData("ölçüleri kullanmadım")]
    [InlineData("ölçüleri kullanmam")]
    [InlineData("ölçüler kesin değillermiş")]
    [InlineData("BU ÖLÇÜLER DOĞRU DEĞİLMİŞ")]
    [InlineData("ölçüleri kullanmayacağız")]
    [InlineData("ölçüler yanlışmış")]
    [InlineData("ölçüleri dikkate almayacağım")]
    [InlineData("bu ölçüler değiştirilecek")]
    [InlineData("ölçüler hakkında emin değilim")]
    [InlineData("ölçüleri başkullanmayacağım")]
    [InlineData("ölçüleri kullanmakılavuzu")]
    [InlineData("ölçüler geçerli; değil")]
    [InlineData("ölçüleri kullanmak istiyorum\nama vazgeçtim")]
    public void WithdrawalInEitherFieldOrOrderBlocksAllProposals(string withdrawal)
    {
        foreach(var (prompt,notes) in new[]{(AllFields+";"+withdrawal,""),(withdrawal+";"+AllFields,""),(AllFields,withdrawal),(withdrawal,AllFields)})
        {
            var result=ALineRequestMeasurements.Review(prompt,notes);
            Assert.False(result.Complete);Assert.Contains(result.Issues,s=>s.Contains("serbest ölçü ifadesi"));
        }
    }
    [Theory]
    [InlineData("1,000")]
    [InlineData("1.000")]
    [InlineData("36.125")]
    [InlineData("0,000")]
    public void ThreeDigitSeparatorNeverBecomesASectionValue(string value)
    {
        var source=AllFields.Replace("Ön bel yayı bolluğu 2 cm","Ön bel yayı bolluğu "+value+" mm");
        var result=ALineRequestMeasurements.Review(source,"");
        Assert.False(result.Complete);Assert.Null(result.Millimeters[4]);
        Assert.Contains(result.Issues,s=>s.Contains("üç basamaklı ayırıcı belirsiz"));
        Assert.False(ALineRequestMeasurements.Review(source+";Ön bel yayı bolluğu 2 cm","").Complete);
    }
    [Theory]
    [InlineData("uzun kollu değil")]
    [InlineData("önceki modeli kullanmayın")]
    [InlineData("ölçüler geçerli")]
    [InlineData("bu ölçüler geçerli!")]
    [InlineData("elbise; kumaş düz olsun")]
    [InlineData("kumaş doğru değilmiş; modeli kullanmayacağım")]
    [InlineData("ölçüleri kullanmak istiyorum")]
    [InlineData("Bu ölçüleri kullanmak istiyorum.")]
    [InlineData("önölçüleri kullanmayacağım")]
    [InlineData("bu kumaşı kullanmayın")]
    [InlineData("bu model geçersiz; tüm kumaşlar ipek olsun")]
    [InlineData("bunaltıcı kumaş istemiyorum")]
    [InlineData("onlarca düğme; onuncu model")]
    public void HarmlessDesignWordsAndExplicitZeroEaseRemainSupported(string description)
    {
        var result=ALineRequestMeasurements.Review(description,AllFields.Replace("Ön bel yayı bolluğu 2 cm","Ön bel yayı bolluğu 0 mm"));
        Assert.True(result.Complete);Assert.Empty(result.Issues);Assert.Equal(0m,result.Millimeters[4]);
    }
    [Fact] public void AffirmativeExceptionRequiresAnEntireFieldAndCannotClearAnUnknownMeasurementStatement()
    {
        Assert.False(ALineRequestMeasurements.Review(AllFields+";ölçüler geçerli","").Complete);
        Assert.False(ALineRequestMeasurements.Review("ölçüler geçerli",AllFields+";ölçüler yanlışmış").Complete);
        Assert.False(ALineRequestMeasurements.Review("ölçüler geçerli?",AllFields).Complete);
        Assert.True(ALineRequestMeasurements.Review("ölçüler geçerli",AllFields).Complete);
        Assert.True(ALineRequestMeasurements.Review(AllFields,"ölçüleri kullanmak istiyorum").Complete);
    }
    [Theory]
    [InlineData("bunları kullanmayın")]
    [InlineData("bunlar geçersiz")]
    [InlineData("hepsini kullanmayın")]
    [InlineData("tümünü dikkate almayacağım")]
    [InlineData("şunlar yanlışmış")]
    [InlineData("onları geri çekiyorum")]
    [InlineData("bunlar geçerli")]
    [InlineData("bunun doğruluğu bilinmiyor")]
    public void PronounReferenceCannotBeSilentlyResolvedByFieldOrderOrAffirmativeException(string reference)
    {
        var snapshots=new[]{(AllFields+";"+reference,""),(reference+";"+AllFields,""),(AllFields,reference),(reference,AllFields),
            ("ölçüler geçerli",AllFields+";"+reference),(AllFields+";"+reference,"ölçüler geçerli"),
            ("ölçüleri kullanmak istiyorum",reference+";"+AllFields),(reference+";"+AllFields,"ölçüleri kullanmak istiyorum")};
        foreach(var (prompt,notes) in snapshots)
        {
            var result=ALineRequestMeasurements.Review(prompt,notes);
            Assert.False(result.Complete);Assert.Contains(result.Issues,s=>s.Contains("referans zamirinin"));
        }
    }
    [Fact] public void PronounPolicyRequiresMeasurementContextButAlsoCoversExplicitBodyTotals()
    {
        Assert.Empty(ALineRequestMeasurements.Review("bunlar kumaş örnekleri","").Issues);
        Assert.Contains(ALineRequestMeasurements.Review("bunları kullanmayın","bel çevresi 72 cm").Issues,s=>s.Contains("referans zamirinin"));
    }
}
