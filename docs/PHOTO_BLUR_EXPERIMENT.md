# Fotoğraf yolu öncesinde bulanıklaştırma deneyi

2 Kasım 2026'da kontrast yolu aramadan önce gri seviye görüntüsünü yumuşatmanın, doku ve gölgeye sapmayı azaltıp azaltmadığı sınandı. Bu klasik görüntü işleme prototipleri ürün koduna bağlanmadı. Kullanılan `PhotoEdgePath.cs` kaynağının normalize edilmiş UTF-8 SHA-256 değeri `ee5fef0a18945287102fd4f4eba74e67a7100a787aa6fa815b5e965ea9266648` idi.

Arama penceresinde önce yatay, ardından dikey binom filtresi uygulandı: üç piksel için `[1,2,1]`, beş piksel için `[1,4,6,4,1]`. Pencere kenarında en yakın piksel tekrarlandı; sonuç iki boyutlu ağırlık toplamına bölünüp en yakın tam gri değere yuvarlandı. Sonraki gradyan, maliyet ve yol araması değiştirilmedi. İki ölçek önceden seçildi; kapsamlı filtre optimizasyonu değildir.

Prototipler [önceki kontur deneyinin](PHOTO_CONTOUR_EXPERIMENT.md) 40 şort ve 40 tişört görüntüsünde, anotasyondan alınan aynı 12 uçla kıyaslandı. Görüntüler geliştirmede daha önce kullanılmıştır; bağımsız doğrulama değildir. Yol noktası sadeleştirmesi ve sınırları deney aracında taklit edildi. Her üç koşulda 80 konturun tüm aralıkları bulundu ve taklit edilen kontrolleri geçti.

| Ölçüm | Mevcut yol | 3 piksel | 5 piksel |
|---|---:|---:|---:|
| Ortalama simetrik uzaklık | 1,423 px | 1,486 px | 1,562 px |
| Örneklenen yerel uzaklığı 5 px aşan kontur | 65 / 80 | 66 / 80 | 67 / 80 |
| En büyük örneklenmiş yerel uzaklık | 24,34 px | 24,34 px | 24,34 px |
| Sıkı kesişim bulunan kontur | 0 | 0 | 1 |

Eşli karşılaştırmada kontur başına en büyük yerel hata üç piksellik filtrede 14'ünde bir pikselden fazla arttı, yedisinde azaldı. Beş piksellik filtrede 26'sında arttı, 11'inde azaldı. [Sayısal özet ve eşli sonuçlar](PHOTO_BLUR_EXPERIMENT.json) kategori bazında da kaydedildi. Her iki prototipin ölçüm satırları yeniden çalıştırıldığında kendi önceki sonuçlarıyla bayt düzeyinde aynıydı.

Anotasyon uçları gerçek kullanıcı tıklaması değildir. Piksel uzaklığı fiziksel kalıp veya üretim toleransı değildir; örnekleme sürekli en büyük hatayı garanti etmez. Sıkı kesişim sayacı temas ve çakışmaları saymaz. Bu verilerde iki filtre de mevcut yolu değiştirmek için yeterli iyileşme göstermedi; ayrılmış görüntüler prototipleri seçmek için kullanılmadı.

Windows, Python 3.12.14, .NET 10, [önceki deneydeki](PHOTO_CONTOUR_EXPERIMENT.md) veri arşivi ve yeni boş çalışma klasörleriyle tekrarlama:

```powershell
python tools/PhotoPathBenchmark/make_blur_experiment.py <yeni-prototip-klasörü> 3
python <yeni-prototip-klasörü>/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-sonuç-klasörü> 12
python tools/PhotoPathBenchmark/compare_contours.py <mevcut-yol-sonuç-klasörü> <boş-sonuç-klasörü> <yeni-karşılaştırma.json>
```

İkinci ölçek için ayrı yeni klasörlerle `5` kullanın. Karşılaştırmanın ilk girdisi aynı kaynak ve örnek seçimindeki mevcut ürün yolunun sonuçları olmalıdır. Arşiv SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Görüntüler depoya eklenmedi; veri lisansı ayrıca değerlendirilmelidir.
