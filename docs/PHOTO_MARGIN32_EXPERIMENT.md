# Fotoğraf yolunda arama alanı: 16 ve 32 piksel

5 Kasım 2026'da, son nokta çiftinin çevresindeki sabit arama payının 16'dan 32 önizleme pikseline çıkarılması ürün kodundan ayrılmış bir kopyada incelendi. Maliyet, 160.000 piksel iş sınırı, nokta sınırı ve diğer ayarlar aynı tutuldu. `PhotoEdgePath.cs` kaynak özeti `ee5fef0a18945287102fd4f4eba74e67a7100a787aa6fa815b5e965ea9266648`; kaynak görüntü ZIP özeti `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Kod yerel ve klasiktir; model veya ücretli servis kullanılmadı.

Her konturun 12 ucu anotasyondan eşit aralıkla alındı; bunlar gerçek kullanıcı tıklaması değildir. Geliştirme kümesi 40 şort/40 tişört, ayrılmış küme farklı görüntü kimliği ve baytlarına sahip aynı sayıda örnek içerir. Ayrılmış örnekler önceki deneylerde de incelendi ve aynı veri kaynağındandır; yeni bir bağımsız veri kaynağı değildir. Yol/nokta kabulü deney aracında taklit edildi, masaüstü doğrulayıcıları burada çalıştırılmadı. Mesafeler örneklenen simetrik en yakın kenar uzaklığıdır; sürekli en büyük sapma, gerçek milimetre veya üretim toleransı değildir.

| Küme | Ortalama hata, 16 → 32 px | Yerel örnekte >5 px, 16 → 32 | Sıkı kesişimli kontur, 16 → 32 |
|---|---:|---:|---:|
| Geliştirme | 1,42271 → 1,42280 px | 65 → 65 / 80 | 0 → 0 |
| Ayrılmış, anotasyon uçları | 1,45192 → 1,45200 px | 59 → 59 / 80 | 1 → 1 |
| Ayrılmış, uçlara yapay 2 px kaydırma | 1,59068 → 1,58360 px | 61 → 60 / 80 | 3 → 3 |

Her koşulda 80 konturun tüm aralıkları bulundu ve taklit edilen nokta kontrollerini geçti. Eşli karşılaştırmada yalnız kaydırmalı koşuldaki bir konturun ortalama hatası 0,5 pikselden fazla azaldı; hiçbir koşulda aynı miktarda artan kontur yoktu. En büyük örneklenmiş yerel hata kaydırmalı ve kaydırmasız ayrılmış kümelerde 25,61 piksel kaldı. Kesişim sayacı yalnız sıkı çapraz geçişleri sayar; temas, çakışma ve tüm topolojiyi kapsamaz.

Kaydırmasız ayrılmış koşul ikinci kez ayrı klasörde çalıştırıldı: kontur ölçüm satırları bayt düzeyinde aynıydı (SHA-256 `130146422F87D8AAEACEB89FA72058F5BA66EE018470FDBBCF37A5B9AD0B6206`). Özet dosyalarının tek farkı, iş listesindeki mutlak klasör yollarını da kapsayan `jobs_sha256` alanıydı; bu yüzden özetlerin kendisi bayt düzeyinde aynı değildi.

İki ayrı sentetik görüntü takası gösterdi. Önceden bilinen 24 piksel genlikli kıvrımlı sınırda yol noktalarının analitik eğriye **yatay** ortalama uzaklığı 2,93'ten 0,35 piksele, örneklenmiş en büyük yatay uzaklığı 8'den 1 piksele indi; bu en yakın kenar uzaklığı veya gerçek giysi değildir. 512×512 görüntüde X ve Y uç farkı 350 piksel olan diğer örnekte 16 payı 383² = 146.689 piksel arayıp yol buldu; 32 payı 415² = 172.225 piksel gerektirdiğinden 160.000 sınırında yol bulmadan reddedildi. Bu ikinci örnek doğruluk değil, iş sınırının davranışıdır.

Sonuç: bu veriler varsayılan arama alanını büyütmeyi desteklemiyor. Geniş alan özel kıvrımlarda yararlı olabilir, fakat aynı bütçeyle daha çok aralık reddedilebilir ve gerçek kumaş sınırı doğrulanmaz. Ürün davranışı bu deney sırasında değiştirilmedi. Sonrasında kullanıcı tarafından seçilen, geniş alan iş sınırına sığmazsa standart alana dönen bir alternatif eklendi; ayrıntılar [yol açıklamasında](PHOTO_PATH_EXPERIMENT.md). Bu, çok ölçekli arama, gerçek kullanıcı işaretleri veya farklı görüntü kaynaklarında doğrulama yerine geçmez.

Sayısal sonuçlar: [geliştirme](PHOTO_MARGIN32_DEVELOPMENT.json), [geliştirme eşli](PHOTO_MARGIN32_DEVELOPMENT_COMPARISON.json), [ayrılmış](PHOTO_MARGIN32_HOLDOUT.json), [ayrılmış eşli](PHOTO_MARGIN32_HOLDOUT_COMPARISON.json), [kaydırılmış](PHOTO_MARGIN32_JITTER.json), [kaydırılmış eşli](PHOTO_MARGIN32_JITTER_COMPARISON.json), [sentetik sonuç](PHOTO_MARGIN32_SYNTHETIC.json). Gerçek görüntüler veya anotasyonlar depoya eklenmedi; veri lisansı ayrıca değerlendirilmelidir.

Kontur başına sayısal ölçümler: [geliştirme](PHOTO_MARGIN32_DEVELOPMENT_ROWS.json), [ayrılmış](PHOTO_MARGIN32_HOLDOUT_ROWS.json) ve [kaydırılmış](PHOTO_MARGIN32_JITTER_ROWS.json).

Tekrarlama için Windows, .NET 10, Python 3.12, ayrı kaynak ZIP ve **yeni boş** klasörler kullanın. `make_margin_experiment.py <prototip> 32` ile kopyayı oluşturun. Ardından `<prototip>/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-klasör> 12 0 development`, aynı komutla `12 0 holdout` ve `12 2 holdout` çalıştırın; `tools/PhotoPathBenchmark/compare_contours.py` ile aynı koşullardaki eski ve yeni klasörleri eşleyin. Sentetik dosyalar `margin_curved_fixture.py` ve `margin_budget_fixture.py` ile ayrı boş klasörlere üretilir. Mevcut ve prototip `PhotoPathBenchmark.dll` araçlarını aynı `jobs.json` için sırasıyla yeni `result16.json` ve `result32.json` dosyalarına çalıştırın; `measure_margin_fixtures.py` iki klasörden yeni bir özet üretir. Bu deney cihaz bağlantısı veya makine işi içermez.
