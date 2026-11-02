# Fotoğraf sınırı: RGB gradyanı deneyi

Bu araştırma üretim kodunu değiştirmez. Klasik RGB gradyanı, griye çevirmenin eş parlaklıktaki renk sınırlarını kaybetmesi sorununu araştırmak için ayrı bir kopyada denendi; yerel AI veya ücretli servis kullanılmadı.

Kaynak arşiv SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Geliştirme ve ayrılmış kümede 80'er farklı görüntü var; ayrılmış küme geliştirme görüntülerini ve aynı baytlı dosyaları dışlıyor. Her çevrede gerçek anotasyondan eşit aralıklı 12 bağlantı noktası kullanıldı; tüm çevrelerde simetrik örneklenmiş en yakın çizgi uzaklıkları hesaplandı. Bu noktalar kullanıcı tıklaması değildir.

| Deney | Eşleştirilmiş ortalama hata değişimi | 5 pikseli aşan yerel hata | Katı öz-kesişim |
|---|---:|---:|---:|
| Geliştirme, RGB − mevcut | −0,106 piksel | 65 → 63 / 80 | 0 → 0 |
| Ayrılmış küme, RGB − mevcut | −0,088 piksel | 59 → 59 / 80 | 1 → 1 |
| Ayrılmış küme, noktalar sentetik 2 piksel kaydırılmış | −0,077 piksel | 61 → 61 / 80 | 3 → 3 |

Ayrılmış kümede RGB ortalama hatayı altı konturda 0,5 pikselden fazla iyileştirdi, hiçbirinde bu miktarda kötüleştirmedi. Bununla birlikte aynı kaynaktaki görüntüler ve anotasyonlar fiziksel giysi ölçüsü, eksiksiz topoloji veya üretilebilir kalıp doğrulaması sağlamaz. Yerel hata ve kesişimler sürdüğü için değişiklik ürün algoritmasına alınmadı. `make_color_gradient_experiment.py` kaynak dosya karmasını doğrulayarak deney kopyasını oluşturur; `make_isoluminant_fixture.py` ayrıca eş gri değerli sentetik renk sınırı üretir.

## Ek kontrollü renk ve 16 nokta deneyleri

64 × 64 sentetik görüntüde iki rengin ürünün gri dönüşümündeki değeri aynıdır (45). Gerçek sınırın analitik X değerine, yolun her pikselindeki yatay uzaklık hesaplandı; bu en kısa geometrik uzaklık değildir. İki yol da 53 nokta içerdi. Mevcut yol düz kaldı: ortalama 5,525, en büyük 8,45 piksel yatay sapma. RGB yolu için ortalama 0,286, en büyük 0,837 piksel bulundu. `make_isoluminant_fixture.py` ve `measure_isoluminant.py` örneği ve hesabı tekrarlar. Bu özellikle seçilmiş tek sentetik örnektir.

Aynı 80 ayrılmış görüntüde bu kez anotasyon çevresinden eşit aralıklı 16 nokta seçildi; bunlar kullanıcının gerçek tıklamaları değildir. Mevcut ve RGB yolunun her birinde 64 konturda yerel sapma 5 pikseli aştı (63 aynı kontur, yöntemlerin her birine özgü birer kontur). 12 noktalı deneyde sayı her iki yöntem için 59 idi; nokta yerleri de değiştiğinden bu karşılaştırma daha fazla noktanın tek başına nedensel etkisini ayırmaz. RGB ortalama hata farkı −0,050 piksel oldu; bir konturda yerel maksimum 1 pikselden fazla kötüleşti. Her iki yöntemde katı kesişim sayacı sıfır olmasına rağmen ürünün piksel geometri doğrulayıcısı mevcut yöntemde 6, RGB yönteminde 5 konturu reddetti. Geometrik olarak kabul edilenlerde dahi sırasıyla 58/74 ve 59/75 konturda yerel sapma 5 pikseli aştı. Sınır örneklemesi fiziksel ölçüyü, örtüşmeleri ve üretilebilirliği doğrulamaz. Deney aynı veri kümesinin önceden incelenmiş ayrılmış bölümünde keşif amaçlı yapıldı; yeni bağımsız doğrulama değildir.
