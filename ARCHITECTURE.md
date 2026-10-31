# Mimari

## Uygulanan karar

İlk Windows sürümü `net10.0-windows` WPF kullanır. Ek Uno iş yükü gerektirmeden mevcut Windows üzerinde gerçek masaüstü derlemesini doğrulamak için seçildi. Çekirdek modeller ve hesaplar WPF bağımlılığı taşımayan `net10.0` kitaplıklarındadır. Uno'ya otomatik taşınmış sayılmaz; yeni UI katmanı gerekecektir. Uno 6.4+ için .NET 10 desteği resmî [sürüm tablosunda](https://platform.uno/docs/articles/net-version-support.html) yer alır.

## Modüller

| Modül | Sorumluluk |
| --- | --- |
| Core | Point2, PatternPiece, ProjectDocument; mesafe, alan, dönüşümler, kesişim, kontur doğrulama |
| Patterns | Dışbükey dikiş payı, nokta düzenleme, çentik/yay geometri yardımcıları |
| Grading | Nokta bazlı X/Y kuralları, beden adımı, kural JSON'u |
| Nesting | Sabit yönlü raf yerleşimi, en/aralık sınırları ve kullanım hesabı |
| FileFormats | Yerel proje, sınırlı ASCII DXF, SVG, HPGL; ortak çıktı yolu koruması |
| MachineGateway | Yerel çıktı dosyası, SHA256 özeti ve manifest kullanan simülasyon kuyruğu |
| Licensing | Güvenilen açık anahtarla RSA SHA-256 lisans doğrulaması |
| Desktop | WPF kanvas, komutlar, dosya diyalogları, tekstil araçları ve kurtarma |
| Tests | xUnit geometri, aktarım, tekstil, güvenlik ve simülasyon regresyonları |

## Veri ve doğrulama

İç koordinat birimi mm, sayı tipi double'dır. Koordinat çalışma alanı ±100.000.000 mm ile sınırlandırılır. Kapalı üretim konturları asgari nokta, sıfır olmayan alan ve kendini kesmeme koşullarından geçer. Kalıcı proje taslak çizgileri tutabilir; kaydetmek üretime uygunluk belgesi değildir. Eğriler örneklenmiş noktalara dönüşür; tam analitik eğri veri modeli henüz yoktur.

Undo/redo proje kopyaları kullanır. Büyük projelerde bellek ve UI yanıt süresi ayrıca ölçülmelidir. Kesişim kontrolü karesel karmaşıklıktadır; modüllerde nokta sınırları bulunur. Uygulama SQLite veya PostgreSQL kullanmaz; yerel proje ve manifestler JSON'dur.

Dosyalar geçici dosyaya yazılıp hedefe taşınır. Format yazımları WearCAD/üretim köklerini ve bağlantı üzerinden yönlendirilen hedefleri reddeder. Bu uygulama düzeyi koruma OS erişim denetiminin yerine geçmez.

## Sonraki mimari işler

Sürümlü proje şeması ve göçler; açık kontur/analitik eğri/katman/beden metaverisi; iptal edilebilir arka plan hesapları; büyük proje undo stratejisi; doğrulanmış protokole özel makine adaptörleri; güvenilir lisans zamanı ve iptal listeleri. Mobil/Uno UI, bulut eşitleme ve AI ayrı aşamalardır.
