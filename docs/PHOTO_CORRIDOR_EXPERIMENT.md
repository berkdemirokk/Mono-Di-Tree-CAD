# Fotoğraf yolunda doğruya uzaklık cezası deneyi

2 Kasım 2026'da, iki uç arasındaki kontrast yolunun uzun sapmalarını azaltmak için klasik bir uzaklık cezası denendi. Bu araştırma prototipidir; ürün koduna bağlanmadı. Kaynak `PhotoEdgePath.cs` dosyasının normalize edilmiş UTF-8 SHA-256 değeri `ee5fef0a18945287102fd4f4eba74e67a7100a787aa6fa815b5e965ea9266648` idi.

Her pikselin, yuvarlanmış iki uçtan geçen **sonsuz doğruya** dik uzaklığı `d` hesaplandı ve mevcut piksel maliyetine `w × d / (8 + d)` eklendi. Arama alanı ve diğer parametreler korunarak, önceden belirlenen `w=0,1` ve `w=0,3` çalıştırıldı. Bu terim bir doğruya yakınlığı teşvik eder; gerçek kumaş sınırının doğruya yakın olacağını garanti etmez ve eğimli veya kavisli sınırları olumsuz etkileyebilir.

Her iki prototip [önceki kontur deneyinin](PHOTO_CONTOUR_EXPERIMENT.md) 40 şort ve 40 tişört görüntüsünde, anotasyondan alınan aynı 12 uçla kıyaslandı. Görüntüler daha önce geliştirmede kullanıldı; bağımsız doğrulama değildir. Yol noktası sadeleştirmesi ve sınırları deney aracında taklit edildi. İki prototipte de 80 konturun tüm aralıkları bulundu ve taklit edilen kontrolleri geçti.

| Ölçüm | Mevcut yol | `w=0,1` | `w=0,3` |
|---|---:|---:|---:|
| Ortalama simetrik uzaklık | 1,423 px | 1,460 px | 1,659 px |
| Örneklenen yerel uzaklığı 5 px aşan kontur | 65 / 80 | 67 / 80 | 70 / 80 |
| En büyük örneklenmiş yerel uzaklık | 24,34 px | 26,40 px | 26,40 px |
| Sıkı kesişim bulunan kontur | 0 | 0 | 0 |

Eşli karşılaştırmada `w=0,1` için kontur başına en büyük yerel hata 11'inde bir pikselden fazla arttı, üçünde azaldı. `w=0,3` için 39'unda arttı, sekizinde azaldı; 15 konturda ortalama hata 0,5 pikselden fazla arttı. [Sayısal özet ve eşli sonuçlar](PHOTO_CORRIDOR_EXPERIMENT.json) kategori bazında da kaydedildi. İki prototipin ölçüm satırları yeniden çalıştırıldığında kendi önceki sonuçlarıyla bayt düzeyinde aynıydı.

Uçlar gerçek kullanıcı seçimi değil, anotasyondan alınmıştır; piksel uzaklığı gerçek fiziksel kalıp veya üretim toleransı değildir ve örnekleme sürekli en büyük hatayı garanti etmez. Sıkı kesişim sayacı temasları ve çakışmaları saymaz. Bu verilerde iki varyant da mevcut yolu değiştirmek için yeterli iyileşme göstermedi; ayrılmış görüntü kümesi prototipleri seçmek için kullanılmadı.

Windows, Python 3.12.14, .NET 10, [önceki deneydeki](PHOTO_CONTOUR_EXPERIMENT.md) arşiv ve yeni boş çalışma klasörleriyle tekrarlama:

```powershell
python tools/PhotoPathBenchmark/make_corridor_experiment.py <yeni-prototip-klasörü> 0.1
python <yeni-prototip-klasörü>/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-sonuç-klasörü> 12
python tools/PhotoPathBenchmark/compare_contours.py <mevcut-yol-sonuç-klasörü> <boş-sonuç-klasörü> <yeni-karşılaştırma.json>
```

İkinci ağırlık için ayrı yeni klasörlerle `0.3` kullanın. Karşılaştırmanın ilk girdisi aynı kaynak ve örnek seçimindeki mevcut ürün yolunun sonuçları olmalıdır. Arşiv SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Görüntüler depoya eklenmedi; veri lisansı ayrıca değerlendirilmelidir.
