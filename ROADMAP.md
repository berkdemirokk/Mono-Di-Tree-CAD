# Yol haritası

Her aşamada kaynak, temiz derleme, otomatik test, gerçek UI kullanımı, kayıt doğrulaması ve bilinen sınırlar birlikte tamamlanır. Aşağıdaki liste tamamlanma iddiası değildir; güncel kanıt `TEST_REPORT.md` içindedir.

1. **Windows temel CAD — geliştirme/doğrulama:** Çizim, seçim, düzenleme, undo/redo, mm hassasiyeti, yerel kayıt ve kurtarma. Ekran akışları ve büyük dosya davranışları tamamlanmalı.
   - *Güncel durum (15 Kasım 2026):* Çizim, seçim, tuvalde çoklu seçim/grup sürükleme, katmanlar, geri al/yinele, atomik kayıt ve kurtarma kodda var; penceresiz doğrulamalar ve CI çalışıyor. Kalan: gerçek operatörle UI kabulü, dış yazıcı kayıt yarışı, büyük gerçek dosya gecikmesi.
2. **Tekstil araçları — ilk uygulama:** Nokta bazlı serileme, dışbükey dikiş payı ve raf pastalı mevcut. İçbükey konturlar, gerçek beden tabloları, kuralların UI'dan yeniden kullanımı, iplik yönü/rotasyon kısıtları, alternatif yerleşim ve performans sıradaki işlerdir.
   - *Güncel durum:* Serileme, kuralların projede saklanması, güvenlik denetimli dikiş payı (bazı içbükey konturlar dâhil), iplik yönü ve dik açılı pastal seçenekleri var. Kalan: içbükey dikiş payında yerel ayrıntı korumasının bağımsız kanıtı, gerçek beden tabloları, serbest açılı/optimum pastal, fabrika verisiyle performans.
3. **CAD veri modeli:** Analitik yay/Bézier, pens, çentik metaverisi, katmanlar, çoklu seçim, birleştirme, hassas ölçülendirme ve kontur düzenleme.
   - *Güncel durum:* Katmanlar (v8), çoklu seçim, görsel köşe işaretleri ve tek segmentli açık analitik Bézier/yay (v9, sayısal kontrol düzenleme ve açık poliline dönüştürme) var. Kalan: kapalı/çok segmentli parametrik kontur, tuvalde kontrol tutamaçları, parametrik geometriyi koruyan taşıma/dönüştürme, pens, gerçek çentik semantiği, genel kontur birleştirme, ölçülendirme.
4. **Dosya uyumluluğu:** ASCII DXF alt kümesinden INSERT/bulge desteğine; ölçü/katman/çentik/beden korunumu için kontrollü AAMA/ASTM örnek kümesi. PDF ve HPGL içe aktarımı ayrıca geliştirilmeli.
   - *Güncel durum:* Sıkı metin DXF içe/dışa aktarımı, kontrollü INSERT ve isteğe bağlı bulge yaklaşımı, SVG, HPGL, tek sayfa ve A4 parçalı inceleme PDF'i var. Kalan: gerçek DXF/WearCAD örnek korpusu, AAMA/ASTM semantiği, parametrik eğrinin DXF'te korunması, ileri uyumluluk/bilinmeyen alan koruması.
5. **Makine merkezi:** Yerel simülasyon sonrası kuyruk kurtarma ve kalibrasyon. Her gerçek model için üretici protokolü, fiziksel test ve ayrı doğrulama kaydı gerekir.
   - *Güncel durum:* Yalnız dosya tabanlı simülasyon ve plotter yeterlilik sözleşmeleri var; fiziksel iş gönderilmedi. Kalan: fabrika cihaz modeli/protokolü (WP01), kalibrasyon ve fiziksel test.
6. **Ticari hizmet:** Lisans doğrulayıcıdan aktivasyon, güvenilir zaman, iptal, kullanıcı/firma hesabı, rol ve abonelik hizmetine. Canlı ödeme entegrasyonu mevcut değildir.
   - *Güncel durum:* Yalnız imzalı çevrimdışı lisans doğrulama kitaplığı var. Kalan: aktivasyon hizmeti, güvenilir zaman, iptal, hesap/rol/abonelik.
7. **Dağıtım:** Taşınabilir paket, temiz makine testi, imzalı yükleyici/güncelleme, lisans bildirimleri ve kaynak yedek doğrulaması.
   - *Güncel durum:* Taşınabilir Windows ZIP betiği ve Windows CI derleme/test iş akışı var. Kalan: imzalı kurulum/güncelleme, temiz makinede güncel paket doğrulaması.
8. **Çok platform ve AI:** Windows doğrulamasından sonra Uno/iPad/macOS/iPhone; macOS/Xcode ortamı kontrolü. AI önerileri çekirdek geometriyle doğrulanmalı, temel kullanım API'ye bağımlı olmamalı.
   - *Güncel durum:* iPhone/iPad/macOS SwiftUI görüntüleyici sürüm 1–9 dosyaları salt okunur önizler ve kendi yalın çizimlerini düzenleyebilir; TestFlight yükleme iş akışı başarıyla çalıştı. Kalan: gerçek cihaz ve kullanıcı kabulü, üretime hazır mobil sürüm; AI özelliği yok.
