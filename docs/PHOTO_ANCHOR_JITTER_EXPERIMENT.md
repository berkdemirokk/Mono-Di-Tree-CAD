# Fotoğraf konturunda yapay nokta kaydırması

2 Kasım 2026'da, [önceki 12 uçlu deneyin](PHOTO_CONTOUR_EXPERIMENT.md) aynı 80 aRTF Clothes test anotasyonunda, her anotasyon ucu bağımsız ve sabit rastgele yönde iki piksel kaydırıldı. Yönler anotasyon kimliğine bağlı tohumla üretildi; görüntü sınırına kırpılacak nokta çıkmadı. Uçlar kullanıcı tıklaması değildir; tek bir yapay kaydırma dağılımı ve tek tohum, gerçek kullanıcıların hata dağılımını veya farklı cihazları temsil etmez. Aynı görüntüler daha önce geliştirme ve değerlendirmede kullanıldığı için deney bağımsız doğrulama değildir.

Her iki koşulda da aynı 12 anotasyon konumu başlangıçtır. Kaydırılmış konumlar arasındaki 12 aralıkta, kapanış dahil ürünün kontrast yolu hesaplandı. Karşılaştırma [eşli sonuç dosyasında](PHOTO_ANCHOR_JITTER_EXPERIMENT.json) kategori ve anotasyon kimliğine göre yapıldı; [kaydırılmış koşulun özeti](PHOTO_ANCHOR_JITTER_SUMMARY.json) ayrıca saklanır. Görüntüler ve anotasyonlar depoya eklenmedi. İki çalıştırmanın özet ve ölçüm dosyaları kendi aralarında bayt düzeyinde aynıydı.

| Ölçüm | Anotasyon uçları | İki piksel kaydırılmış uçlar |
|---|---:|---:|
| Tam öneri / kontur | 80 / 80 | 80 / 80 |
| Ortalama simetrik uzaklık | 1,42 piksel | 1,53 piksel |
| Örneklenen yerel uzaklığı 5 pikseli aşan kontur | 65 / 80 | 69 / 80 |
| En büyük örneklenmiş yerel uzaklık | 24,34 piksel | 26,08 piksel |
| Sıkı kenar kesişimi bulunan kontur | 0 / 80 | 1 / 80 |

Eşli karşılaştırmada dört konturun ortalama hatası 0,5 pikselden fazla arttı; hiçbirinde aynı miktarda azalmadı. Yerel en büyük hata 19 konturda bir pikselden fazla arttı, 11 konturda aynı miktarda azaldı. Bu değişimler hem nokta kaymasının hem de bu uçlarla yeniden hesaplanan yolun sonucudur; tek başına tıklama hatası değildir.

Yol noktası sınırı ve sadeleştirme deney aracında taklit edildi; masaüstü arayüzü ve tüm doğrulayıcılar çalıştırılmadı. Kesişim sayacı sadece kenar içlerindeki sıkı çapraz geçişleri sayar; temas ve çakışmaları saymaz. Yaklaşık bir piksel aralıkla örneklenen uzaklık gerçek en büyük sapmayı garanti etmez. Anotasyon, fiziksel kalıp doğruluğunun referansı değildir; perspektif, kumaş dökümü, gerçek ölçüler ve üretim uygunluğu ölçülmedi. Bir ortalama iyileşme, yerel yanlış sınırların otomatik kabulünü haklı çıkarmaz.

Tekrarlamak için veri arşivi, .NET 10, Python 3.12.14 ve iki yeni boş çalışma klasörü gerekir:

```powershell
python tools/PhotoPathBenchmark/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-referans-klasörü> 12
python tools/PhotoPathBenchmark/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-kaydırma-klasörü> 12 2
python tools/PhotoPathBenchmark/compare_contours.py <referans-klasörü> <kaydırma-klasörü> <yeni-sonuç.json>
```

Arşiv SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Arşivin lisans ve ticari kullanım koşulları ayrıca değerlendirilmelidir.
