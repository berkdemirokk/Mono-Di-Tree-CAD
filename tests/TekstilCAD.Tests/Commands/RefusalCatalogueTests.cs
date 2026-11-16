using TekstilCAD.Core.Commands;

namespace TekstilCAD.Tests;

public sealed class RefusalCatalogueTests
{
    // Verbatim from tasarim/FINAL_COMMANDS.md section 3 (an independent copy on purpose).
    public static readonly (string Key, string Text)[] Expected =
    {
        ("R01", "“{yazı}” anlaşılamadı. Şöyle yazabilirsiniz: 120 x 80 dikdörtgen · 25 sağa taşı · 90 sağa döndür · geri al"),
        ("R02", "Kaç milimetre? Örnek: {örnek}"),
        ("R03a", "Önce bir parça seçin. Parçaya tıklayın ya da “ön bedeni seç” yazın."),
        ("R03b", "Henüz parça yok. Önce bir parça çizin. Örnek: 120 x 80 dikdörtgen"),
        ("R04", "Ölçü 1 ile 20000 mm arasında olmalı. Örnek: 120 x 80 dikdörtgen"),
        ("R05", "{sayı} iki türlü okunabilir. {a} mm için {a} yazın; {b} mm için {b} yazın."),
        ("R06", "{yazı}: milimetre mi, santimetre mi?"),
        ("R07", "Önce çizimi bitirin (Enter) ya da iptal edin (Esc)."),
        ("R08", "Parça kilitli bir katmanda. Ayrıntılı görünümde katmanın kilidini açın."),
        ("R09", "Şu anda bir dosya işlemi sürüyor. Bitince yeniden deneyin."),
        ("R10", "Bu parça dikdörtgen değil; ölçüsü yazıyla değiştirilemez. Yeni bir dikdörtgen için şunu yazın: 95 x 80 dikdörtgen"),
        ("R11", "Bu parçada iplik yönü, köşe işareti, serileme kuralı ya da eğri ayarı var; ölçüsü yazıyla değiştirilemez. Parçayı silip yeni ölçüyle çizebilirsiniz."),
        ("R12", "Bu parçada eğri ayarı var; taşımak için önce Tüm araçlar bölümündeki “Eğriyi sabit çizgiye çevir…” işlemini yapın. Eğri düzenleme özelliği bundan sonra kalkar; Geri al ile dönebilirsiniz."),
        ("R13", "Şimdilik yalnız 90, 180 ve 270 derece döndürülebilir. Örnek: 90 sağa döndür"),
        ("R14", "Birden fazla parça seçili. Tek parça seçin ya da “Birden fazla parçayı taşı…” kullanın."),
        ("R15a", "“{ad}” adında parça bulunamadı. Örnek: {gerçek ad} seç"),
        ("R15b", "{n} parça eşleşti. Hangisi?"),
        ("R16", "Bu yazı iki ayrı iş gibi okunuyor. Tek iş yazın. Örnek: 120 x 80 dikdörtgen"),
        ("R16b-1", "“ters çevir” aynalamak da döndürmek de olabilir. Örnek: aynala · 90 sağa döndür"),
        ("R16b-2", "“büyüt” görünümü mü, parça ölçüsünü mü? Örnek: yakınlaştır · genişliği 95 yap"),
        ("R17", "Bu parçada 4096'dan fazla nokta var. Bu işlemi Ayrıntılı görünümdeki ilgili düğmeyle yapın."),
        ("R18a", "Geri alınacak işlem yok. Önce bir şey yazıp uygulayın. Örnek: 120 x 80 dikdörtgen"),
        ("R18b", "Yinelenecek işlem yok."),
        ("R19", "Dikdörtgen için iki ölçü gerekir: genişlik ve yükseklik. Örnek: 120 x 80 dikdörtgen"),
        ("R20", "Önce Ayrıntılı görünümdeki yarım kalmış ad ve konum kutularını uygulayın ya da temizleyin (“Alan taslaklarını temizle” düğmesi)."),
        ("R21", "Kurtarma işlemi bitmeden değişiklik yapılamaz. Üstteki kurtarma uyarısının düğmesini kullanın."),
        ("R22", "Parça değişti; önizleme yenilendi. Enter ile uygulayın."),
        ("R23", "Yazı çok uzun (en fazla 200 karakter). Kısaltın. Örnek: 120 x 80 dikdörtgen"),
        ("R24", "Yeni ad boş olamaz. Örnek: adı ön beden olsun"),
        ("R25", "Ölçü zaten {ölçü}. Değişiklik yok."),
        ("R26", "Ölçü eksi (−) olamaz. Yön için sağa, sola, yukarı ya da aşağı yazın. Örnek: 25 sola taşı"),
        ("R27", "Önce parçayı seçin: “ön bedeni seç”. Sonra: “25 sağa taşı”."),
        ("R28a", "İşlem tamamlanamadı: {neden}. Hiçbir şey değişmedi."),
        ("R28b", "İşlem yapıldı ama ekran yenilenemedi. Aynı komutu yeniden yazmayın; gerekirse Geri al'ı kullanın."),
    };

    [Fact]
    public void TheCatalogueHoldsEveryTextOfTheSpecificationVerbatim()
    {
        Assert.Equal(Expected.Length, Refusals.Catalogue.Count);
        foreach (var (key, text) in Expected)
        {
            var entry = Assert.Single(Refusals.Catalogue, e => e.Key == key);
            Assert.Equal(text, entry.Text);
        }
    }

    [Fact]
    public void EveryCodeFromR01ToR28bIsCovered()
    {
        var codes = Refusals.Catalogue.Select(e => e.Code).Distinct().ToHashSet();
        var wanted = new List<string>();
        for (var i = 1; i <= 27; i++) wanted.Add("R" + i.ToString("00"));
        wanted.Remove("R15"); wanted.Add("R15a"); wanted.Add("R15b");
        wanted.Remove("R16"); wanted.Add("R16"); wanted.Add("R16b");
        wanted.Add("R28a"); wanted.Add("R28b");
        foreach (var code in wanted) Assert.Contains(code, codes);
    }

    [Fact]
    public void EveryTextThatTheSpecificationGivesAnExampleForHasOne()
    {
        foreach (var entry in Refusals.Catalogue.Where(e => e.NeedsExample))
        {
            var hasExample = entry.Text.Contains("Örnek:", StringComparison.Ordinal)
                || entry.Text.Contains("Şöyle yazabilirsiniz:", StringComparison.Ordinal)
                // These end in a button the specification lists ("Düğmeler" column) instead of a written example.
                || entry.Code is "R03" or "R05" or "R10" or "R27" or "R06" or "R07" or "R08" or "R11" or "R12" or "R14" or "R15b" or "R17" or "R20" or "R28b";
            Assert.True(hasExample, entry.Key);
        }
    }

    [Fact]
    public void TheOnlyTextsWithoutAnExampleOrButtonAreTheOnesTheSpecificationLeavesBare()
    {
        var bare = Refusals.Catalogue.Where(e => !e.NeedsExample).Select(e => e.Key).OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "R09", "R18b", "R21", "R22", "R25", "R28a" }, bare);
    }

    [Fact]
    public void NoTextNamesTheProductAsAnAssistantOrAModel()
    {
        foreach (var entry in Refusals.Catalogue)
            Assert.False(TurkishText.ContainsForbiddenWord(entry.Text), entry.Key);
    }

    [Fact]
    public void BuiltMessagesFillTheirPlaceholders()
    {
        Assert.Equal(
            "“zzz” anlaşılamadı. Şöyle yazabilirsiniz: 120 x 80 dikdörtgen · 25 sağa taşı · 90 sağa döndür · geri al",
            Refusals.Unrecognized("zzz").Message);
        Assert.Equal("Kaç milimetre? Örnek: 150 çizgi", Refusals.NumberMissingFor("150 çizgi").Message);
        Assert.Equal("“kolu” adında parça bulunamadı. Örnek: Ön beden seç",
            Refusals.NameNotFoundFor("kolu", new[] { "Ön beden", "Kol" }, "seç").Message);
        Assert.Equal("3 parça eşleşti. Hangisi?", Refusals.NameAmbiguousFor(new[] { "a", "b", "c" }, "seç").Message);
        Assert.Equal("Ölçü zaten 120 × 80 mm. Değişiklik yok.", Refusals.NothingChanges("120 × 80 mm").Message);
        Assert.Equal("İşlem tamamlanamadı: disk dolu. Hiçbir şey değişmedi.", Refusals.Failed("disk dolu").Message);
    }

    [Fact]
    public void NoBuiltMessageKeepsAnUnfilledPlaceholder()
    {
        var all = new[]
        {
            Refusals.Unrecognized("x"), Refusals.NumberMissingFor("e"), Refusals.NoPieceSelected(new[] { "a" }), Refusals.NoPiecesYet(),
            Refusals.OutOfRangeMeasure(), Refusals.AmbiguousNumberText("1.500", "1,5", "1500", "1.500 çizgi", 0, 5),
            Refusals.UnitQuestionFor("12 x 8 dikdörtgen", "a", "b"), Refusals.AngleNotSupported(),
            Refusals.NameNotFoundFor("x", new[] { "a" }, "seç"), Refusals.NameAmbiguousFor(new[] { "a", "b" }, "sil"),
            Refusals.TwoJobsRefusal(), Refusals.FlipOrRotate(), Refusals.ZoomOrResize(), Refusals.TwoMeasuresRequired(),
            Refusals.TextTooLong(), Refusals.EmptyNameRefusal(), Refusals.NegativeNumberRefusal(), Refusals.NamedPieceCannotMove(),
            Refusals.DrawingPending(), Refusals.Locked(), Refusals.Busy(), Refusals.NotARectangle(), Refusals.HasExtraData(),
            Refusals.HasCurves(), Refusals.SeveralSelected(), Refusals.TooManyPointsRefusal(), Refusals.NothingToUndoRefusal(),
            Refusals.NothingToRedoRefusal(), Refusals.AreaDraftsPending(), Refusals.RecoveryInProgress(), Refusals.PreviewRefreshed(),
            Refusals.NothingChanges("1 × 1 mm"), Refusals.Failed("x"), Refusals.RefreshFailedAfterApply(),
        };
        foreach (var r in all)
        {
            Assert.DoesNotContain("{", r.Message);
            Assert.DoesNotContain("}", r.Message);
        }
    }

    [Fact]
    public void ButtonsOnlyFillTheBoxAndNeverCarryEmptyText()
    {
        var question = Refusals.UnitQuestionFor("12 x 8 dikdörtgen", "12 x 8 mm dikdörtgen", "12 x 8 cm dikdörtgen");
        Assert.Equal("R06", question.Code);
        Assert.True(question.IsQuestion);
        Assert.All(question.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(b.FillText)));
        var hint = new RefusalButton("Şunu mu demek istediniz: dikdörtgen", "dikdörtgen 1 2");
        Assert.Equal("dikdörtgen 1 2", hint.FillText);
        Assert.Equal("x", new RefusalButton("x").FillText);
    }

    [Fact]
    public void AnUnrecognizedTextWithAForbiddenWordIsNotEchoed()
    {
        var r = Refusals.Unrecognized("yapay zeka ile çiz");
        Assert.DoesNotContain("yapay", r.Message);
        Assert.Contains("“…”", r.Message);
        Assert.True(r.HasExample);
    }

    [Fact]
    public void TheEchoOfAnUnrecognizedTextIsCappedAtFortyCharacters()
    {
        var long50 = new string('a', 50);
        Assert.Contains("“" + new string('a', 40) + "…” anlaşılamadı.", Refusals.Unrecognized(long50).Message);
        Assert.Contains("“" + new string('a', 40) + "” anlaşılamadı.", Refusals.Unrecognized(new string('a', 40)).Message);
    }

    [Fact]
    public void AmbiguousNumberButtonsReplaceTheNumberInsideTheText()
    {
        var r = Refusals.AmbiguousNumberText("1.500", "1,5", "1500", "20 sağa 1.500 yukarı", 8, 5);
        Assert.Equal(new[] { "20 sağa 1,5 yukarı", "20 sağa 1500 yukarı" }, r.Buttons.Select(b => b.FillText));
    }

    [Fact]
    public void AmbiguityButtonsAreCappedAtThree()
    {
        var r = Refusals.NameAmbiguousFor(new[] { "a1", "a2", "a3", "a4", "a5" }, "seç");
        Assert.Equal(3, r.Buttons.Count);
        Assert.Contains("5 parça", r.Message);
    }
}
