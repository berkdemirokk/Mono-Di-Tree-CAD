# Fotoğraf yolunda temel maliyet deneyi

2 Kasım 2026'da, ürünün `0,12 + 64/(64+kontrast)` piksel maliyetindeki sabit terim izole prototipte 0,30 ve 0,03 olarak denendi. Daha büyük sabit, uzun dolambaçlı yollara daha fazla maliyet yükler; diğer arama sınırları ve nokta sadeleştirmesi değiştirilmedi. Kaynak, `PhotoEdgePath.cs` normalize UTF-8 SHA-256 `ee5fef0a18945287102fd4f4eba74e67a7100a787aa6fa815b5e965ea9266648` idi. Ürün algoritması değiştirilmedi.

Önce aynı veri kaynağının daha önce geliştirmede kullanılan 80 görüntüsünde iki değer denendi. 0,03 daha düşük ortalama verdiği için, önceden ayrılmış ve görüntü kimliği/aynı baytları dışlanmış 80 görüntüde yalnız o değer ayrıca denendi. Ayrılmış görüntüler aynı veri kümesindendir ve daha önce algoritmanın mevcut davranışını değerlendirmekte kullanılmıştır; tamamen yeni veri kaynağı değildir. Her kontur için anotasyondan eşit aralıklı 12 uç ve kapanış kullanıldı. İkinci ayrılmış koşulda uçlar sabit rastgele yönlerde 2 piksel kaydırıldı; bu gerçek kullanıcı tıklaması değildir.

| Küme | Sabit | Ortalama simetrik hata | Yerel örneklemede 5 px üstü | En büyük yerel örnek | Dar sıkı kesişim |
|---|---:|---:|---:|---:|---:|
| Geliştirme | 0,12 | 1,423 px | 65/80 | 24,34 px | 0 |
| Geliştirme | 0,30 | 1,475 px | 68/80 | 25,06 px | 0 |
| Geliştirme | 0,03 | 1,380 px | 65/80 | 24,34 px | 0 |
| Ayrılmış, anotasyon uçları | 0,12 | 1,45 px | 59/80 | 25,61 px | 1 |
| Ayrılmış, anotasyon uçları | 0,03 | yaklaşık 1,45 px | 57/80 | 26,62 px | 1 |
| Ayrılmış, 2 px kaydırma | 0,12 | 1,59 px | 61/80 | 25,61 px | 3 |
| Ayrılmış, 2 px kaydırma | 0,03 | 1,55 px | 56/80 | 25,61 px | 4 |

Eşli karşılaştırmada 0,03 ile ayrılmış anotasyon koşulunun ortalama değişimi −0,0010 px idi; iki konturun ortalama hatası 0,5 px'ten fazla arttı. Kaydırılmış koşulda ortalama değişim −0,0440 px oldu. Aynı öneriler gerçek `PhotoTraceCanvas` ve piksel geometrisi doğrulayıcılarıyla incelendiğinde, kaydırmalı koşulda geçersiz/uyarılı kontur sayısı 11'den 16'ya çıktı: önce geçerli altı kontur uyarılı hale gelirken bir uyarılı kontur geçti. Anotasyon koşulunda uyarılar 8'den 6'ya indi; buna rağmen geçenlerde örneklenen en büyük yerel sapma 25,61'den 26,62 px'e çıktı.

Bu karışık sonuç **ürüne alma için yeterli değildir**. Topoloji kontrolünü geçmek kenar doğruluğu değildir; dar sayaç temas/çakışmaları kapsamaz. Mesafeler yaklaşık piksel aralıklarıyla örneklenir, gerçek sürekli maksimumu garanti etmez. Anotasyon uçları iyimserdir; ölçü, perspektif, örtülü bölgeler ve üretim uygunluğu değerlendirilmedi. Farklı kaynaklardan gerçek ölçülü kalıplar ve gerçek kullanıcı işaretleriyle doğrulama gereklidir.

Sayısal kayıtlar: [0,30 geliştirme](PHOTO_BASE_COST_DEV_03.json), [0,03 geliştirme](PHOTO_BASE_COST_DEV_003.json), [geliştirme eşli](PHOTO_BASE_COST_DEV_COMPARISON.json), [0,30 eşli](PHOTO_BASE_COST_DEV_03_COMPARISON.json), [ayrılmış eşli](PHOTO_BASE_COST_HOLDOUT_COMPARISON.json), [kaydırılmış eşli](PHOTO_BASE_COST_HOLDOUT_JITTER_COMPARISON.json), [ayrılmış topoloji](PHOTO_BASE_COST_HOLDOUT_TOPOLOGY.json), [kaydırılmış topoloji](PHOTO_BASE_COST_JITTER_TOPOLOGY.json). Kontur satırları ve topoloji satırları sırasıyla [ayrılmış](PHOTO_BASE_COST_HOLDOUT_ROWS.json), [kaydırılmış](PHOTO_BASE_COST_JITTER_ROWS.json), [ayrılmış topoloji](PHOTO_BASE_COST_HOLDOUT_TOPOLOGY_ROWS.json), [kaydırılmış topoloji](PHOTO_BASE_COST_JITTER_TOPOLOGY_ROWS.json) dosyalarındadır. Kaydırılmış prototipin aynı iş girdisindeki ikinci yol çıktısı bayt düzeyinde aynı SHA-256'yı verdi: `0DD5CDEFF6A25D2A315949F478F44C34102E44D64D047F010C3F054148163BB8`.

Tekrarlama için önceki [ayrılmış deneydeki](PHOTO_HOLDOUT_EXPERIMENT.md) aynı veri arşivi, .NET 10, Python 3.12 ve yeni boş klasörler gerekir. Görüntüler depoya eklenmedi; veri lisansı ayrıca değerlendirilmelidir.

```powershell
python tools/PhotoPathBenchmark/make_base_cost_experiment.py <yeni-prototip> 0.03
python <yeni-prototip>/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-sonuç> 12 2 holdout
python tools/PhotoPathBenchmark/compare_contours.py <mevcut-kaydırılmış-sonuç> <boş-sonuç> <yeni-karşılaştırma.json>
dotnet build tools/PhotoTopologyBenchmark/PhotoTopologyBenchmark.csproj -c Release
dotnet tools/PhotoTopologyBenchmark/bin/Release/net10.0-windows/PhotoTopologyBenchmark.dll <boş-sonuç>/jobs.json <boş-sonuç>/results.json <yeni-topoloji.json>
python tools/PhotoTopologyBenchmark/summarize.py <yeni-topoloji.json> <boş-sonuç>/measurements.json <boş-sonuç>/summary.json <yeni-özet.json>
```
