# App Store ekran görüntüleri — gereken boyutlar ve eldekiler

Durum: **taslak**, 16 Kasım 2026. Kaynak: Apple, [Screenshot specifications](https://developer.apple.com/help/app-store-connect/reference/app-information/screenshot-specifications/) (aynı gün okundu). Apple bu listeyi sık değiştirir; yüklemeden önce sayfaya yeniden bakın.

## Uygulama hangi cihazlarda çalışıyor?

`Apple/project.yml` içinde `TARGETED_DEVICE_FAMILY` yazılı değildir. XcodeGen'in iOS ön ayarı ([SettingPresets/Platforms/iOS.yml](https://github.com/yonaskolb/XcodeGen/blob/master/SettingPresets/Platforms/iOS.yml)) bunu `'1,2'` yapar: uygulama **iPhone ve iPad** için derlenir. `INFOPLIST_KEY_UISupportedInterfaceOrientations_iPad` de tanımlıdır. Bu yüzden **iPad ekran görüntüsü de zorunludur** ve App Review uygulamayı iPad'de de deneyebilir. Yalnız iPhone yayımlanmak istenirse `TARGETED_DEVICE_FAMILY: '1'` eklenmesi kullanıcının kararıdır (bu dalda değiştirilmedi).

## Bugün zorunlu olanlar

Her boyut için en az 1, en çok 10 görüntü; `.png` veya `.jpg`; alfa kanalı (saydamlık) olmamalı.

| Grup | Örnek cihaz | Kabul edilen dikey boyutlar (piksel) | Durum |
|---|---|---|---|
| iPhone, Dynamic Island, orta ekran ("6.3 inç") | iPhone 16 Pro, 17 Pro | 1179 × 2556, 1206 × 2622 | **Zorunlu** (Apple: en az bir görüntü) |
| iPhone, Dynamic Island, büyük ekran ("6.9 inç") | iPhone 16/17 Pro Max | 1260 × 2736, 1290 × 2796, 1320 × 2868 | Önerilir; verilmezse 6.5 inç grubu gerekir |
| iPhone, Face ID, büyük ekran ("6.5 inç") | iPhone 11 Pro Max, XS Max | 1242 × 2688, 1284 × 2778 | Yalnız 6.9 inç verilmezse zorunlu |
| iPad 13 inç | iPad Pro (M4/M5), iPad Air 13 inç | 2048 × 2732, 2064 × 2752 | **Zorunlu** (uygulama iPad'de çalıştığı için) |
| iPhone 6.1 inç, iPad 11 inç, iPad 12.9 inç | — | — | Gerekmez; büyük boyuttan ölçeklenir |

En kolay yol: **6.3 inç + 6.9 inç iPhone** ve **13 inç iPad** setleri. Yatay görüntüler de kabul edilir (aynı boyutların yatay hali), ama bir sette tek yönde kalmak daha düzgün görünür.

Not: Apple sayfası "iPhone Duo" için Nisan 2027'den itibaren iOS 27.1 SDK ile ayrıca görüntü isteneceğini yazıyor. Bugün bu uygulamayı etkilemez.

## CI'daki görüntüler ne kadar işe yarar?

`.github/workflows/apple-ci.yml`, `ScreenshotTourTests` (`Apple/Tests/ScreenshotTourTests.swift`) testlerini çalıştırıp PNG'leri `iphone-screenshots` adlı çıktıya koyar (7 gün saklanır). Sınırları:

1. **Cihaz boyutu sabit değil.** İş akışı, çalıştırıcıdaki iPhone simülatörlerinin adına göre alfabetik sıralamada **sonuncusunu** seçer (ör. "iPhone 16e" "iPhone 16 Pro Max"tan sonra gelir). Görüntü boyutu o cihazın piksel boyutudur: iPhone 16e ise 1170 × 2532 olur ve **hiçbir zorunlu gruba uymaz**. Hangi cihaz kullanıldığı çıktıdaki `device.txt` dosyasında yazar; boyut PNG'den kontrol edilmelidir.
2. **iPad görüntüsü hiç yok.** CI yalnız iPhone simülatörü çalıştırır; 13 inç iPad seti tamamen eksiktir.
3. **Hata ayıklama derlemesi ve sistem dili.** Testler `-AppleLanguages (en)` ile açılır; uygulama metinleri Türkçe kalır ama klavye ("Done") ve sistem sayfaları İngilizce görünür. Türkçe mağaza sayfası için sistem dili Türkçe olmalıdır.
4. **Durum çubuğu.** Saat, pil ve operatör simülatörün o anki hâlidir. Mağaza için `xcrun simctl status_bar <cihaz> override --time 9:41 --batteryState charged --batteryLevel 100` ile sabitlenmesi önerilir.
5. **Amaç farklı.** Bu turlar görsel denetim içindir; bazıları bilerek hata, büyük yazı ya da koyu mod durumunu gösterir.

| CI görüntüsü | Mağazada kullanılabilir mi? |
|---|---|
| `01-home` (ana ekran) | Evet — 1. görüntü adayı (ilk açılış, üç düğme, "Nasıl çalışır?"). |
| `09-example-two-pieces` (örnek iki parça) | Evet — çizim atölyesini gösterir. |
| `03-editor-one-piece` (bir dikdörtgen) | Evet, ama `09` daha dolu görünür. |
| `11-home-with-recent-project` (Son projeler) | Evet — cihazda saklanan proje listesini gösterir. |
| `12-viewer-all-pieces`, `14-viewer-piece-selected` (.tcad önizleme) | Evet — dosya açma özelliği. Test dosyası "Seçim Testi" adını taşır; daha anlamlı bir örnek dosyayla yeniden çekmek iyi olur. |
| `04-editor-scrolled-1…6` (araç bölümleri) | Belki biri: ölçü alanları ve kaydetme bölümü için. |
| `08-editor-landscape`, `15-viewer-landscape` | Yalnız yatay bir set yapılırsa. |
| `02-editor-empty`, `03b-editor-typing-dimension`, `10-after-save`, `13-viewer-piece-picker` | Zayıf aday (boş ekran, klavye, ara durum). |
| `16-rejected-file-alert`, `17/18/19-…-large-text`, `20/21-…-system-dark-app-light` | Hayır — hata, erişilebilirlik ve koyu mod denetimleridir. |

## Eksik olan görüntüler

- **Parmakla şekil çiz** ekranı (`TouchContourComposer`): noktalarla çizilmiş bir şekil.
- **Yakınlaştırılmış çizim ve ölçü etiketleri** (seçili parçanın üstünde "… mm" yazıları). Ölçü etiketleri çizim alanı 150 noktadan alçaksa gizlenir; dikey ekranda, klavye kapalıyken çekilmelidir.
- **Köşe noktaları** düzenleyicisi (`ContourPointEditor`).
- **Paylaş menüsü** (`.tcad` hazırlandıktan sonra). İsteğe bağlı.
- **13 inç iPad**'de yukarıdakilerin hepsi.

## Önerilen sıra (5–6 görüntü)

1. Ana ekran — "Kalıbını kolayca oluştur".
2. Örnek iki parça, bir parça seçili, ölçü etiketleri görünür.
3. Parmakla şekil çiz.
4. Ölçü alanları (En/Boy, X/Y milimetre).
5. Son projeler listesi.
6. .tcad dosyası önizleme.

Görüntülere başlık yazısı eklenecekse (ör. "Ölçüyü milimetreyle yaz") yalnız uygulamada gerçekten olan özellikler yazılmalı; "üretime hazır", "kesim dosyası" gibi ifadeler kullanılmamalıdır.

## Bunları üretmek için (bu dalda yapılmadı)

Mağaza görüntüleri için ayrı, elle tetiklenen bir iş akışı ya da `apple-ci.yml`'e bir iş eklenebilir: belirli simülatörleri adıyla seçer (ör. "iPhone 17 Pro", "iPhone 17 Pro Max", "iPad Pro 13-inch (M5)" — çalıştırıcıda hangileri varsa), sistem dilini Türkçe yapar, durum çubuğunu sabitler ve eksik ekranlar için tur testleri ekler. Bu, `.swift` test dosyalarını ve iş akışlarını değiştirir; bu dalın kapsamı dışında bırakıldı ve kullanıcının kararıdır.
