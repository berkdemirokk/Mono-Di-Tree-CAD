# Ticari ürün çıkış planı

Hedef: çevrimdışı Windows tekstil CAD ürünü. Görsel tasarım tek başına satışa hazır olma kriteri değildir. Her madde çalışan kod, veri koruma testi ve operatör kabulüyle kapatılır.

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

## Ticari hizmetler

- Lisans etkinleştirme/çevrimdışı süre ve cihaz değişikliği akışı; imza anahtarları yalnızca güvenli sunucuda.
- Firma/rol/yetki, satış ve abonelik yönetimi; ayrıca yetkilendirilmiş ödeme sağlayıcısı entegrasyonu.
- Bağımlılık bildirimi, müşteri sözleşmesi/destek süreci ve pilot kullanıcı geri bildirimi.

## Fiziksel makine

Üretici protokolü ve cihaz modeli doğrulanmadan uyumlu etiketi verilmez. Dosya simülasyonu fiziksel kesim doğrulaması değildir. Gerçek testler için ayrı yetki ve fiziksel güvenlik prosedürü gerekir.

Sonraki ilk dilim: gerçek operatörle kayıt/açma, serileme hücreleri, sürükleme/undo ve kurtarma kabulü; ardından katman/çoklu seçim ve genel dikiş payı. UI otomasyonu için mevcut arka plan tercihini koruyun.
