# TekstilCAD Studio

Windows üzerinde çevrimdışı çalışan, bağımsız tekstil CAD geliştirme sürümü. Arayüz WPF/.NET 10; geometri ve tekstil hesapları ayrı C# kitaplıklarıdır. Bu sürüm ticari üretim için tamamlanmış veya sertifikalı değildir.

## Başlatma ve test

Proje kökünde PowerShell:

```powershell
.\scripts\Build.ps1 -Test
.\scripts\Run.ps1
```

SDK önce `.tools\dotnet\dotnet.exe` konumunda aranır. Bulunmazsa PATH üzerindeki .NET SDK kullanılır; `global.json` SDK 10.0.403 sürümünü belirtir. İlk NuGet geri yüklemesi internet gerektirir. Kaynak yedeklerine SDK, `bin`, `obj`, önbellek veya kullanıcı kalıpları eklenmemelidir.

## Mevcut işlevler

- Milimetre koordinatlı çizgi, çoklu çizgi ve dikdörtgen; seçim, yakınlaştırma/kaydırma, ızgaraya yakalama, taşıma, döndürme, aynalama ve geri al/yinele.
- Doğrulanan yerel JSON proje (`.tcad`) kaydetme/açma; atomik yazma ve kurtarma kaydı altyapısı.
- Nokta indeksine göre X/Y artımlarıyla beden üretimi; geçersiz geometrinin reddi.
- Kapalı doğrusal konturlarda güvenlik denetimli dikiş payı denemesi; bazı içbükey şekiller dâhil belirsiz veya ayrıntı kaybı riski taşıyan sonuçlar reddedilir. Nokta ekleme/silme ile çentik ve yay hesaplama API'leri.
- Sabit yönlü, sınırlayıcı dikdörtgenleri kullanan deterministik raf tipi pastal. Kullanım/fire hesapları mevcut; optimum yerleşim garantisi yok.
- Sınırlı metin DXF içe/dışa aktarımı, SVG ve HPGL/PLT dışa aktarımı. DXF içe aktarımında varsayılan sıkı UTF-8; Türkçe Windows-1254 veya Batı Avrupa Windows-1252 yalnız açık kullanıcı seçimiyle kullanılır, otomatik kodlama tahmini yapılmaz. DXF çıktısı UTF-8 / DXF 2007 olarak yazılır.
- Dosya tabanlı makine simülasyon kitaplığı; imzalı çevrimdışı lisans doğrulama kitaplığı.

Kütüphane işlevinin bulunması tüm ekran akışlarının doğrulandığı anlamına gelmez. `TEST_REPORT.md` son kaydedilmiş kanıtın zamanını ve tarihsel sonuçları ayrı gösterir; sonraki kod değişiklikleri için testler tekrar çalıştırılmalıdır. UI gözlemleri `docs/reviews/ui-smoke.md` dosyasında tutulur. Test edilmemiş özellikler tamamlanmış sayılmaz.

## Çalışma akışı

1. Yeni proje açın veya örnek parçalarla başlayın.
2. Çizim aracını seçip kanvasa tıklayın. Çoklu çizgiyi Enter ile kapatın; Esc ile çizimi iptal edin.
3. Parçayı seçerek özelliklerden X/Y konumunu düzenleyin. Ölçüler mm'dir.
4. Tekstil araçları penceresinde seçili parçaya serileme veya desteklenen dikiş payını uygulayın.
5. Projeyi ayrı bir `.tcad` dosyasına kaydedin. DXF içe aktarırken birim ölçeğini açıkça seçin ve bilinen ölçüyü kontrol edin.

## Kritik sınırlar

AAMA/ASTM beden, çentik ve iplik yönü semantiği henüz doğrulanmamıştır. Kontrollü DXF aktarımı, açık katman onayı ve kullanıcının seçtiği ölçü toleransıyla sınırlı 2B INSERT dönüşümlerini ve düz çizgi konturlarını denetler; belirsiz veya desteklenmeyen verileri reddeder. Standart aktarım INSERT yerleşimlerini uygulamaz; blok tanımlarını incelemek için kullanılabilir ve önizlemede uyarı gösterir. Ayrı, isteğe bağlı bulge yaklaşımı desteklenen bağımsız 2B çoklu çizgi yaylarını doğru parçalarına çevirir; seçilen sapma nominal hedeftir, kesin geometrik hata sınırı veya üretim onayı değildir. Parametrik eğri korunmaz; BLOCK/INSERT dönüşümleri, 3B ve ARC/CIRCLE bu seçenekle desteklenmez. UTF-16/32, binary DXF ve Windows-1254 ve Windows-1252 dışındaki eski kod sayfaları desteklenmez; seçilen kodlamanın ve ölçülerin doğruluğu kontrol edilmelidir. DXF çıktısının bağımsız CAD ürünleriyle ve gerçek fabrika dosyalarıyla uyumluluğu henüz doğrulanmamıştır. Yerel WearCAD `.pcs/.mak/.rul/.skt` dosyaları doğrudan desteklenmez. İçbükey dikiş payı kapsamı ve ayrıntı koruması henüz tamamlanmamış ve geniş gerçek veriyle doğrulanmamıştır; profesyonel kontur birleştirme/pens, çoklu seçim/katman sistemi, PDF, mobil, AI, hesap/ödeme ve canlı makine bağlantıları tamamlanmamıştır.

**WearCAD salt okunur referanstır.** Kurulumu, ayarları ve kullanıcı dosyaları değiştirilmez. Tüm geliştirme/test çıktıları TekstilCAD altında tutulur; gerçek dosya örnekleri ayrı kopyalarda sınanır. Ayrıntılar `AGENTS.md` ve `MIGRATION.md` içindedir.

## Dağıtım

`scripts/Package.ps1` temiz Git HEAD üzerinden kaynak ZIP ve .NET çalışma zamanı içeren Windows x64 ZIP üretir. Windows ZIP'i ayrı bir klasöre açıp TekstilCAD.Studio.exe çalıştırın; ayrıca SDK kurulumu gerekmez. Kurtarma, projeler ve simülasyon verileri %LOCALAPPDATA%\TekstilCAD\Studio altında tutulur; geliştirme kontrolleri ayrı work klasöründe yalıtılır. Paketler yazılım imzası/üretim sertifikası taşımaz.

Bézier: başlangıç, iki kontrol noktası, bitiş olmak üzere dört tıklama. Yay: merkez, başlangıç ve bitiş yönü olmak üzere üç tıklama; saat yönünde örneklenir. Seçim aracında düğüm veya parça sürüklenebilir.


## Tarihsel 0.2 masaüstü arayüzü

Koyu ortak tema, gruplanmış komutlar, durum göstergeleri, kaydırılabilir araç/özellik panelleri ve serileme/makine ekranları yenilendi. Ctrl+N/O/S/Shift+S/Z/Y, V/L/P/R/B/A/F kısayolları; metin alanında yazarken çizim kısayolları tetiklenmez. Kullanılamayan komutlar pasiftir. Kaydedilmemiş değişikliklerde Kaydet / Kaydetmeden devam et / Vazgeç seçenekleri sunulur.

Normal derleme kullanıcı verileri `%LOCALAPPDATA%\TekstilCAD\Studio` altında tutulur. Ayrı derlenen geliştirme önizlemesi `%LOCALAPPDATA%\TekstilCAD\Studio-Preview-v4` kullanır; aynı v4 veri dizinini kullanan diğer önizlemelerle paylaşır, normal Studio verileri otomatik taşınmaz. Bu dizinler uygulama klasöründen bağımsızdır. Mevcut uygulama ticari sürüm değil, geliştirme önizlemesidir.

