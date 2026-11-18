# Windows önizlemesi için operatör kabul formu

Her katılımcı için boş bir kopya kullanın. Katılımcı işlemleri kendisi yapar. Gözlemci yardım verdiğinde hangi adımda ne söylediğini yazar. Her satıra **Geçti**, **Kaldı** veya **Denenmedi** yazın. Boş hücreler henüz gözlem yapılmadığını gösterir.

| Deneme bilgisi | Kayıt |
|---|---|
| Tarih, saat, operatör kodu, gözlemci | |
| Uygulama sürümü, paket adı, varsa commit | |
| Windows sürümü, ekran ölçeği, görüntü ve konuşma dili | |
| Katılımcının CAD deneyimi, başlangıç ve bitiş saati | |
| Yazıcı modeli, sürücü, PDF okuyucu, cetvelin en küçük bölümü | |
| Baskı için sorumlu kişinin deneme öncesi belirlediği tolerans (± mm) | |
| Referans DXF kopyası, birimi, beklenen en × boy, ölçü kaynağı ve tolerans (mm) | |

## İlk açılıştan yeniden açmaya

Yedek bilgisayarda yeni bir deneme klasörü oluşturun. Çıktılara burada benzersiz adlar verin. Deneme sırasında makine veya kesiciye iş göndermeyin.

| Adım | Uygulama ve beklenen sonuç | Sonuç, süre, gözlem |
|---|---|---|
| 1. Kur ve aç | Aday sürümün dağıtım talimatını izleyin. ZIP kullanılıyorsa yeni klasöre çıkarıp uygulamayı oradan başlatın. Açılış ekranı görünmeli. Bir Windows uyarısı erişimi engellerse metnini kaydedin. | |
| 2. Çiz ve ölç | Yeni projede Ctrl+K ile komut kutusunu açın. `10 x 15 cm dikdörtgen` yazın. Önizlemede 100 × 150 mm görüp onaylayın. Tuvalde tek kapalı parça ve aynı ölçüler görünmeli. Görünen birimi kaydedin. | |
| 3. Geri al ve yinele | Son çizimi geri alın, parça sayılarını kaydedin. Yineleyince aynı dikdörtgen ve ölçüler geri gelmeli. | |
| 4. Kaydet | Farklı Kaydet penceresini açıp önce İptal’i seçin. Çizim açık kalmalı. Tekrar açıp deneme klasörüne yeni bir `.tcad` adıyla kaydedin. Dosyanın oluştuğunu klasörde kontrol edin. | |
| 5. Aç | Kaydı tamamladıktan sonra uygulamayı kapatıp yeniden başlatın. Aç penceresinden kaydettiğiniz dosyayı seçin. Parça sayısını, kapanışını ve iki ölçüyü 2. adımla karşılaştırın. | |

## Win+H ile on komut

Ayrı, boş bir deneme projesinde her satır için Ctrl+K ile kutuya odaklanın, önceki metni temizleyin, Win+H ile Türkçe söyleyin. Windows’un yazdığı metni düzeltmeden kaydedin. Önizleme beklenen işlem ve ölçüyü gösteriyorsa onaylayın. Yanlışsa iptal edin. Sonraki satır bir seçime ihtiyaç duyuyorsa ilk dikdörtgeni seçin. Dikte başlatılamazsa Windows iletisini kaydedin. Yazılı girişle yaptığınız tekrarları ayrıca işaretleyin.

| Söylenecek söz | Önizleme veya uygulanınca beklenen sonuç | Diktenin yazdığı metin | Sonuç / yardım |
|---|---|---|---|
| yüz yirmi çarpı seksen dikdörtgen | 120 × 80 mm dikdörtgen | | |
| bir santim sola kaydır | Seçili dikdörtgen 10 mm sola | | |
| yarım santim yukarı | 5 mm yukarı | | |
| bir buçuk santim aşağı | 15 mm aşağı | | |
| bir santim iki milim sağa | 12 mm sağa | | |
| sola döndür | Saat yönünün tersine 90° | | |
| çeyrek tur | Saat yönünde 90° | | |
| yarım tur | 180° döndürme | | |
| on beş santim yatay çizgi | 150 mm yatay çizgi | | |
| seksen dikey çizgi | 80 mm dikey çizgi | | |

Komutların sayısal örnekleri ve dönüş yönleri [çözümleyici testlerinde](../tests/TekstilCAD.Tests/Commands/CommandParserTests.cs) bulunur. Sözlerin Windows tarafından rakamla veya yazıyla aktarılması denemenin bir parçasıdır.

## Kâğıt ölçeği ve bağımsız DXF

| Adım | Uygulama ve beklenen sonuç | Ölçülen değer, sonuç, gözlem |
|---|---|---|
| 6. A4 PDF | 5. adımda kaydettiğiniz projeyi açıp dikdörtgeni seçin. “Seçili parçayı A4 PDF çıkar…” ile PDF’i deneme klasörüne yazın. PDF okuyucuda A4 kâğıt ve “Gerçek boyut” veya %100 seçin. Sayfaya sığdırmayı kapatıp kâğıda basın. | |
| 7. Cetvelle ölç | Basılı sayfadaki “50 MM KONTROL” çizgisinin iki ucu arasını ölçün. Hedef 50 mm. Gerçek ölçüyü ve hedeften sapmayı yazın. Geçme kararı için formun başındaki toleransı kullanın. Tolerans belirlenmediyse yalnız ölçümü kaydedin ve kabulü açık bırakın. | |
| 8. DXF içe aktar | Kaynak dosyanın bağımsız bir kopyasını deneme klasörüne alın. Uygulamada yalnızca o kopyayı seçin. Birim seçimini, uyarıları ve aktarılan parça sayısını kaydedin. Bilinen konturun enini ve boyunu ölçün, başta yazılan referans ve toleransla karşılaştırın. Sonucu ayrı bir `.tcad` dosyasına kaydedip yeniden açarak ölçüleri tekrar kontrol edin. | |

## Başarısızlık kaydı

Her sorun için satırı çoğaltın. Aynı sürüm ve örnekle tekrar denediğinizde ikinci sonucu da yazın.

| Adım / komut | Beklenen | Gözlenen ve tam hata metni | Tekrar adımları / kaç denemede | Ekran görüntüsü veya çıktı adı | Yardım, tekrar sonucu |
|---|---|---|---|---|---|
| | | | | | |

**Genel sonuç ve açık adımlar:** ____________________

**Operatör / gözlemci / tarih:** ____________________
