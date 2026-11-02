# Fotoğraf konturunda nokta yoğunluğu deneyi

2 Kasım 2026'da [önceki tam kontur deneyindeki](PHOTO_CONTOUR_EXPERIMENT.md) aynı 80 aRTF Clothes test anotasyonu üzerinde, anotasyon çevresinden eşit aralıklarla seçilen **12 ve 16 uç** karşılaştırıldı. Her iki durumda kapanış dahil ardışık uçlar arasında ürünün aynı kontrast yolu kodu çalıştırıldı. Noktalar gerçek anotasyondan alındığından bu deney gerçek kullanıcının nokta seçme hatasını ölçmez; aynı örnek daha önce kullanılmıştır ve bağımsız doğrulama değildir.

İki nokta kümesi ayrı ayrı eşit aralıklarla seçildi: **16 nokta, 12 noktanın üzerine yalnızca dört nokta eklenmiş hâli değildir.** Bu nedenle sonuç, kullanıcının belirli dört noktayı eklemesinin etkisini veya yeni aralık düzeltme arayüzünün başarısını ölçmez. Anotasyon kimliği ve kategori üzerinden aynı görüntüdeki sonuçlar eşleştirildi. İki referans noktasıyla 512 nokta sınırı ve ara nokta sadeleştirmesi deney kodunda taklit edildi; masaüstü arayüzü ve tüm geometri doğrulayıcıları bu çalışmada çalıştırılmadı. Her iki yöntemde 80 yolun tüm aralıkları bulundu ve taklit edilen nokta kontrollerinden geçti.

| Ölçüm | 12 uç | 16 uç |
|---|---:|---:|
| Önerinin kontur başına ortalama simetrik uzaklığı | 1,42 piksel | 1,09 piksel |
| En az bir örneklenen yerel uzaklığı 5 pikseli aşan kontur | 65 / 80 | 65 / 80 |
| En büyük örneklenmiş yerel uzaklık | 24,34 piksel | 18,97 piksel |
| Sıkı kenar kesişimi bulunan öneri | 0 / 80 | 2 / 80 |

Çiftli karşılaştırmada 23 konturun ortalama hatası 0,5 pikselden fazla azaldı; hiçbirinde 0,5 pikselden fazla artmadı. Kontur başına örneklenen en büyük yerel uzaklık 41'inde bir pikselden fazla azaldı, 17'sinde bir pikselden fazla arttı. Her iki durumda 58 kontur beş piksel sınırını aştı; yedisi yalnız 12, yedisi yalnız 16 uçta aştı. İki sıkı kesişim 16 uçlu şort önerilerindedir. Sayaç yalnız kenarların iç kısımlarında çapraz kesişmeleri sayar; temasları ve çakışmaları dışlar. Bu önerilerin masaüstünde taslak veya kapalı parça olarak kabul edilip edilmediği bu deneyle sınanmadı.

Uzaklıklar yaklaşık bir piksel aralıkla örneklenir; gerçek sürekli en büyük hata daha büyük olabilir. Piksel hatası fiziksel ölçü değildir; giysinin gerçek kalıbı, perspektif, döküm, beden ve üretim uygunluğu doğrulanmadı. Daha fazla ve yeniden konumlandırılmış uç ortalamayı iyileştirse de yerel sapmalar ve kesişimler nedeniyle otomatik kabulü haklı çıkarmaz. [Çiftli sayısal sonuç](PHOTO_ANCHOR_DENSITY_EXPERIMENT.json), veri kümesinin SHA-256 değeri ve yöntemleri içerir. 12 uçlu özet önceki kayıtla, 16 uçlu özet tekrarla bayt düzeyinde aynıdır.

## Tekrarlama

Bağımsız indirilen `artf-all-categories-resized_512x256.zip`, Windows, Python 3.12, .NET 10 ve iki boş çalışma klasörü kullanın:

```powershell
python tools/PhotoPathBenchmark/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-12-klasörü> 12
python tools/PhotoPathBenchmark/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-16-klasörü> 16
python tools/PhotoPathBenchmark/compare_contours.py <12-klasörü> <16-klasörü> <yeni-sonuç.json>
```

Görüntüler ve anotasyonlar depoya eklenmez; veri lisansının yeniden dağıtım ve ticari kullanımı ayrıca değerlendirilmelidir.
