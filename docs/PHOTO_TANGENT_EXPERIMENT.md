# Kontrast yoluna yön cezası deneyi

2 Kasım 2026'da, fotoğraf yolunun kenarı dik kesmek yerine kenar boyunca ilerlemesini teşvik eden klasik bir yön cezası denendi. Bu **araştırma prototipidir**; ürün koduna bağlanmadı. Kullanılan kaynak `PhotoEdgePath.cs` dosyasının normalize edilmiş UTF-8 SHA-256 değeri `ee5fef0a18945287102fd4f4eba74e67a7100a787aa6fa815b5e965ea9266648` idi.

Mevcut maliyete, geçişin iki ucundaki gri seviye gradyanlarının hareket yönüne izdüşümlerinin mutlak değerlerinden bir terim eklendi. İzdüşümlerin adım uzunluğuna bölünmüş ortalaması `q` ise ek maliyet, adım uzunluğu çarpı `w × q / (64 + q)` idi. Önce `w=0,6`, kötü sonuçtan sonra `w=0,15` denendi. Bu seçimler sonradan yapıldı; sistematik parametre taraması değildir.

Her iki prototip, [önceki kontur deneyinde](PHOTO_CONTOUR_EXPERIMENT.md) kullanılan 40 şort ve 40 tişört görüntüsünde, anotasyondan alınan aynı 12 uçla kıyaslandı. Bu görüntüler daha önce geliştirmede kullanıldı; **bağımsız doğrulama değildir**. Aralıklar ve kapanış ayrı hesaplandı; nokta sadeleştirmesi ve sınırları deney aracında taklit edildi. İki prototipte de 80 konturun tüm aralıkları bulundu ve taklit edilen kontrolleri geçti.

| Ölçüm | Mevcut yol | `w=0,15` | `w=0,6` |
|---|---:|---:|---:|
| Ortalama simetrik uzaklık | 1,423 px | 1,426 px | 1,488 px |
| Örneklenen yerel uzaklığı 5 px aşan kontur | 65 / 80 | 66 / 80 | 71 / 80 |
| En büyük örneklenmiş yerel uzaklık | 24,34 px | 24,34 px | 25,08 px |
| Sıkı kesişim bulunan kontur | 0 | 0 | 0 |

Eşli karşılaştırmada `w=0,15` için kontur başına en büyük yerel hata beşinde bir pikselden fazla arttı, birinde azaldı. `w=0,6` için 25'inde arttı, yedisinde azaldı. Bunlar ortalama sonuçların her görüntüde aynı yönde değiştiği anlamına gelmez. [Sayısal özet ve eşli sonuçlar](PHOTO_TANGENT_EXPERIMENT.json) kategori bazında da kaydedildi. Her iki prototipin ölçüm satırları ikinci çalıştırmada bayt düzeyinde aynıydı.

Uçlar gerçek kullanıcı seçimi değil, anotasyondan alınmıştır; uzaklıklar yaklaşık piksel aralıklarıyla örneklenmiştir ve gerçek fiziksel kalıp, üretim toleransı veya en büyük sürekli hata değildir. Kesişim sayacı temas ve çakışmaları saymaz. Görüntüler aynı veri kaynağındandır. Bu verilere göre iki varyantın hiçbiri mevcut yolu değiştirmek için yeterli iyileşme göstermedi; ayrılmış görüntü kümesi bu prototipleri seçmek için kullanılmadı.

Windows, Python 3.12.14, .NET 10, [önceki deneydeki](PHOTO_CONTOUR_EXPERIMENT.md) veri arşivi ve yeni boş çalışma klasörleriyle tekrarlama:

```powershell
python tools/PhotoPathBenchmark/make_tangent_experiment.py <yeni-prototip-klasörü> 0.15
python <yeni-prototip-klasörü>/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-sonuç-klasörü> 12
python tools/PhotoPathBenchmark/compare_contours.py <mevcut-yol-sonuç-klasörü> <boş-sonuç-klasörü> <yeni-karşılaştırma.json>
```

İkinci ağırlık için ayrı yeni klasörlerle `0.6` kullanın. Karşılaştırmanın ilk girdisi, aynı kaynak ve örnek seçimindeki mevcut ürün yolunun sonuçları olmalıdır. Veri arşivi SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Görüntüler depoya eklenmedi; veri lisansı ayrıca değerlendirilmelidir.
