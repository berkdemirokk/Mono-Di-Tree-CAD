# Sonraki görev

1. TEST_REPORT.md ve PROGRESS.md okuyun; mevcut testleri koruyun. AGENTS.md WearCAD korumasına uyun.
2. Kullanıcı arka planda çalışma istedi: görünür UI otomasyonu yapmayın. Gerekli UI kabul testi için kullanıcı ekran çalışmasına döndüğünde test edin.
3. Son kaynak paketini temiz klasörde derleme ve test sonucu ile karşılaştırın.
4. İlk milestone eksik kabul kontrolleri: kaydet/aç diyalogları, DXF birim seçimi, serileme hücre düzenleme, sürükleme/undo, kurtarma gerçek yeniden açılışı, simülasyon akışı.
5. Geçersiz ve büyük gerçek DXF korpusu, birim/katman/çentik/beden round-trip kabul kriterleri ekleyin. Kullanıcı dosyalarını yalnızca ayrı kopyalarda kullanın.
6. Genel içbükey dikiş payı için lisansı incelenmiş Clipper2 entegrasyonunu değerlendirin; çekirdek ile testleri önce tamamlayın.
7. Katmanlar/çoklu seçim/ölçülendirme ve parametrik eğrileri sırayla geliştirip her modülü UI'da doğrulayın.
8. İmzalı kurulum, veri dizini taşınabilirliği ve üretim kalite kapılarından önce sürümü ticari hazır olarak adlandırmayın.

## 0.2 devamı

Yeni kaynaklar: StudioTheme.cs/App.xaml, AppPaths.cs, yenilenmiş MainWindow ve araç pencereleri. Görünür kullanıcı ekranını açmadan --verify raporlarıyla ilerleyin. Normal runtime veri dizini artık kullanıcı LocalAppData; test verisini AppPaths.UseVerificationDirectory ile workspace altında tutun. Ticari kalite planı docs/PRODUCT_RELEASE_PLAN.md.

## 31 Ekim 2026 - 50 ajan fabrika denetimi sonrası öncelik

Yeni plan `docs/audit-2026-10-31/FINAL_PLAN.json`; ham kanıtlar R01-R50.json, toplam 155 bulgu. Bu denetim uygulama kodunu değiştirmedi. İlk müşteri büyük fabrika; 7+ kişi geliştirme kapasitesi olarak planlandı, operatör sayısı bilinmiyor. USB bağlantısı bildirildi; cihaz model/protokolü hâlâ bilinmiyor. Fiziksel iş gönderilmedi.

1. WP01 kapsamında tek ürün ailesi, referans korpus, kabul sahibi ve cihaz kimliği/erişimini netleştirin. Yeni kapsamı genişletmeden mevcut örneklerin köken ve ölçülerini kaydedin.
2. R11/R12 çoklu süreç kayıt ve kurtarma, R04 yalnız ad değişiminde yuvarlama, R13 no-op redo/savepoint, R26 ardışık beden ve üretim kümesi sorunlarını izole regresyonlarla yeniden üretin. Statik bulguları çalıştırılmış hata gibi sunmayın.
3. WP02 sürümlü kimlikli tekstil modeli, WP03 UI bağımsız belge oturumu ve WP04 güvenli kayıt/kurtarmayı ilk uygulama dilimi yapın. Tam fabrika yedeğinden başka PC/hesap/yola dönüş, pilot ön koşuludur.
4. WP09-11 için kalıcı iş kimliği, onaylı revizyon ve profil yaşam döngüsünü tasarlayın. Askı/iptal edilmiş profil hazırlama ve gönderim anında engellenmeli; belirsiz teslim otomatik tekrar gönderilmemeli.
5. PDF/JSON planındaki haftalar düşük güvenli bütçe senaryosudur. Dördüncü hafta ve iki teslim sprinti sonunda rol yükü, gerçek hız ve cihaz erişimiyle yeniden tahmin edin. 50 ajan, 50 kişilik mühendislik kapasitesi veya fabrika sertifikası değildir.

WearCAD koruması ve arka planda çalışma talebi aynen geçerlidir. Kullanıcının dosyalarını veya açık uygulamasını değiştirmeyin. Son rapor teslimleri chat workspace outputs klasöründedir.

## 0.3 sonrası güncel sıra
1. IMPLEMENTATION_RECORD.json ve I10/I11/I12/I15 incelemelerini okuyun. 0.3 düzeltmelerini tekrar uygulamayın; 267 test temelini koruyun.
2. Sürümlü tekstil modeli ve serileme kurallarını kaydet/aç/undo/redo ile kalıcılaştırın; eski .tcad uyumluluğunu test edin.
3. İki bağımsız OS süreciyle kayıt çakışması ve gerçek süreç sonlandırma/kurtarma senaryolarını izole work dizininde doğrulayın.
4. HPGL gerçek çıktı önizlemesini DXF ölçek kontrolü kadar kapsamlı doğrulayın; cihaz göndermeyin.
5. Gerçek operatör kabulü, genel kontur/pastal kısıtları, üretim revizyonu/roller, yedekten başka hesapta dönüş ve imzalı kurulum hâlâ açık.
6. WearCAD ve 0.2 kurulumuna dokunmayın. 0.3 ayrı klasöre teslim edilir; görünür pencere açmayın.

## Serileme kalıcılığı sonrası
1. Yeni sürümlü biçimde bilinmeyen alanların sessizce kaybolmasını engelleyecek ileri uyumluluk yaklaşımını test edin; kullanıcıya eski 0.3'ün yeni kurallı projeyi açamayacağını açıkça anlatın.
2. Yeni kalıcılık akışının aktif hücre ve kapatma etkileşimi gerçek kullanıcı testi ile doğrulanmalı. 48 başsız kontrol yalnız programatik kanıttır.
3. Yeni önizleme ve proje formatını ayrı sürüm olarak paketlemeden önce bağımsız incelemeyi ve tam regresyonu tamamlayın.
