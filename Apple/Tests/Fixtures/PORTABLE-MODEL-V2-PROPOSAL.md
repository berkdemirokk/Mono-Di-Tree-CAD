# Taşınabilir istek: model kimliği için v2 önerisi

Durum: Tasarım önerisidir; v2 okuyucu/yazıcı uygulanmamıştır. Proje dosyası sürümleriyle ilgisi yoktur. Bu dosyadaki veriler sentetiktir.

## Mevcut davranış ve kanıt

- iPhone v1 `prompt(style:description:)` seçili modeli `Seçilen elbise modeli: …` satırı olarak metne ekler. `portableDraft` v1 dosyasını açarken tüm prompt'u aynen açıklamaya alır, `.describe` kullanır; bu yüzden model kartı kimliği geri kazanılmaz. Yerel taslak biçimi ise model tercihini ayrı saklar.
- iPhone v1 `portableDraft` üst düzey anahtarları tam eşleştirir. Windows `DesignRequestStorage` da `Fields` ile bilinmeyen veya tekrarlı anahtarları reddeder ve yalnız sürüm 1'i kabul eder. v1'e sessizce `model` eklemek eski okuyucuları kırar.
- Windows `DesignRequestWindow.RecoverLoadedModelCandidate` yalnız kendi şablon metnini arar; eşleşme veya belirsizlik durumunda izlenen kart bilgisi değişir. İki uygulamanın şablonları özdeş olduğu varsayılmamalı; serbest metin seçimin güvenilir kimliği değildir. iPhone bilinen gömülü model satırlarında ayrı seçimle çakışmayı engeller; genel serbest metindeki her çelişki otomatik belirlenemez.
- `proposal-v1-extra-model.json` ve `proposal-v2-model.json` sentetik örnekleri Windows kaynak commit `3f6a4e40cca709bd3a1bfb5e495a698f500d5e86` okuyucu koduyla .NET üzerinde denendi: ikisi de `InvalidDataException`, bilinmeyen alan nedeniyle reddedildi. Testte kullanılmayan dosya yazma bağımlılıkları stub'landı; Windows arayüzü ve iPhone çalıştırılmadı. `ios-complete-request.tcad-request.json` mevcut v1 için metin ve fotoğraf baytı örneğidir.

## Önerilen v2 sözleşmesi

`format` aynı kalır; `version` 2 olur. `prompt`, `measurements` ve `reference` mevcut anlamlarını ve sınırlarını korur. Zorunlu `model` değeri ya `null` ya da yalnız sabit, sürümlü sözlükten bir `id` içeren nesnedir (örnek `{"id":"aline"}`). Önerilen tanımlar `wrap`, `aline`, `corset`, `sheath`, `straight`, `shirt`; kimlikler iki uygulama tarafından ortak kararlaştırılmadan yayınlanmaz. `null` model seçilmedi anlamına gelir. Kullanıcının açıklaması `prompt` içinde aynen saklanır; v2'de uygulama tarafından türetilen model satırı prompt'a otomatik eklenmez. Bilinmeyen kimlik, eksik/tekrarlı/bilinmeyen alan ve geçersiz değer güvenle reddedilir; mevcut açık içerik değiştirilmez. V2 bu öneri olarak uygulanmadığı sürece örnek dosya üretim formatı sayılmaz.

## Geçiş ve uyuşmazlık

1. Önce iki uygulamanın v1/v2 okuyucuları ve ortak kimlik tablosu, sentetik sözleşme testleriyle birlikte çıkarılır; her iki uç doğrulanmadan v2 varsayılan yapılmaz.
2. V1 içe aktarmada tüm prompt birebir korunur, model `null` kalır. Açık ve tek bir şablon bulunması ancak kullanıcıya doğrulatılacak bir öneridir; kimlik sessizce atanmaz, metin silinmez. Çift, düzenlenmiş veya çelişkili satır dahil hiçbir metin yeniden yazılmaz.
3. V2'de kullanıcı prompt'u düzenleyebilir; kimlik ancak açık kart seçimiyle değişir. Görünür çelişki uyarısı, tüm doğal dil çelişkilerini saptadığını iddia etmeden incelemeye yönlendirir. İçe aktarma ve ret, mevcut taslağı yalnız bütün doğrulama başarılı olduktan ve kullanıcı ilgili değiştirmeyi onayladıktan sonra değiştirir.
4. Eski alıcıya v1 aktarım açık kullanıcı seçimiyle yeni dosyaya yapılır: seçili kimlik açıklanabilir bir model satırına dönüştürülür, mevcut prompt ve ölçü/fotoğraf korunur; dosyanın ayrı model kimliğini geri yükleyemeyeceği açıkça gösterilir. Zaten model satırı veya olası çelişki varsa otomatik satır eklemek yerine kullanıcı çözümü seçer. V2 kaynağının üzerine yazılmaz.
5. İki yönde metin, birim, boşluk, fotoğraf baytları, çelişki, bilinmeyen kimlik, eşzamanlı durum değişikliği ve eski okuyucunun ret/önceki durum koruma senaryoları test edilir. Cihaz, VoiceOver, dosya sağlayıcısı ve Windows UI kanıtı ayrıca gerekir.