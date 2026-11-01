# Ticari ürün çıkış planı

Hedef: 499 USD tek seferlik, çevrimdışı Windows tekstil CAD ürünü. Ücretli servis veya yerel AI gerektirmez. Görsel tasarım tek başına satışa hazır olma kriteri değildir. Her madde çalışan kod, veri koruma testi ve operatör kabulüyle kapatılır. [Rakip ve fiyat kanıtı](COMPETITOR_EVIDENCE_2026-11-01.md), ürünün henüz satışa hazır olduğunu göstermez.

## Şimdiki doğrulanabilir aşama — 0.2 önizleme

Yerel WPF uygulaması; tutarlı arayüz ve komut durumları; geometri/serileme/temel pastal; sınırlı dosya alışverişi; yerel simülasyon. Yeni arayüz 1440x900 ve1024x640 boyutlarında arka planda render edilir. Kullanıcı dosyaları ve WearCAD korumalıdır. Bu aşama ticari yayın değildir.

## Üretim işlevleri

- Parametrik eğri, ölçü ve katman modeli; çoklu seçim, iplik yönü, pens ve çentik meta verisi.
- İçbükey konturlarda güvenilir dikiş payı; gerçek kalıplarda referans ölçü ve topoloji testleri.
- Beden tablosu/kural yönetimi ve gerçek üretici dosyalarında doğrulanmış AAMA/ASTM alışverişi.
- Yön ve adet kısıtlarını koruyan iyileştirilmiş pastal; aynı iş için tekrarlanabilir sonuç, çakışmasız yerleşim ve belgeli performans.

## Veri güvenilirliği

- Sürümlü yerel dosya şeması ve ileri/geri geçiş politikası.
- Çökme/güç kesintisi/kayıt başarısızlığı kabul testleri; otomatik kurtarma yeniden açılışları.
- Bozuk/büyük dosya korpusu, sınırlandırılmış işlem süresi, iptal edilebilir uzun işlemler.

## Masaüstü dağıtım ve işletim

- İmzalı kurulum ve uygulama, güvenli güncelleme/geri alma, temiz Windows kullanıcı hesabında test.
- DPI/çoklu ekran/klavye/erişilebilirlik ve gerçek operatör iş akışı kabulü.
- Hata günlükleri, veri dışa aktarma/yedekleme ve destek tanılama süreci.

## Tek seferlik lisans ve destek

- Tek seferlik lisansın kapsamı, çevrimdışı kullanım, cihaz değişikliği ve anahtar kurtarma akışı; anahtar üretimi ve dağıtımı için güvenli işletim modeli.
- Zorunlu abonelik, ücretli servis veya yerel AI gerektirmeden satış ve destek sürecinin tanımlanması; ödeme entegrasyonu mevcut kabul edilmez.
- Bağımlılık bildirimi, müşteri sözleşmesi, güncelleme ve destek süresi ile pilot kullanıcı geri bildirimi.

## Fiziksel makine

Üretici protokolü ve cihaz modeli doğrulanmadan uyumlu etiketi verilmez. Dosya simülasyonu fiziksel kesim doğrulaması değildir. Gerçek testler için ayrı yetki ve fiziksel güvenlik prosedürü gerekir.

Sonraki ilk dilim: gerçek operatörle kayıt/açma, serileme hücreleri, sürükleme/undo ve kurtarma kabulü; ardından katman/çoklu seçim ve genel dikiş payı. UI otomasyonu için mevcut arka plan tercihini koruyun.
