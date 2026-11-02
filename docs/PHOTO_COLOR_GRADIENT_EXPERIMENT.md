# Fotoğraf sınırı: RGB gradyanı deneyi

Bu araştırma üretim kodunu değiştirmez. Klasik RGB gradyanı, griye çevirmenin eş parlaklıktaki renk sınırlarını kaybetmesi sorununu araştırmak için ayrı bir kopyada denendi; yerel AI veya ücretli servis kullanılmadı.

Kaynak arşiv SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Geliştirme ve ayrılmış kümede 80'er farklı görüntü var; ayrılmış küme geliştirme görüntülerini ve aynı baytlı dosyaları dışlıyor. Her çevrede gerçek anotasyondan eşit aralıklı 12 bağlantı noktası kullanıldı; tüm çevrelerde simetrik örneklenmiş en yakın çizgi uzaklıkları hesaplandı. Bu noktalar kullanıcı tıklaması değildir.

| Deney | Eşleştirilmiş ortalama hata değişimi | 5 pikseli aşan yerel hata | Katı öz-kesişim |
|---|---:|---:|---:|
| Geliştirme, RGB − mevcut | −0,106 piksel | 65 → 63 / 80 | 0 → 0 |
| Ayrılmış küme, RGB − mevcut | −0,088 piksel | 59 → 59 / 80 | 1 → 1 |
| Ayrılmış küme, noktalar sentetik 2 piksel kaydırılmış | −0,077 piksel | 61 → 61 / 80 | 3 → 3 |

Ayrılmış kümede RGB ortalama hatayı altı konturda 0,5 pikselden fazla iyileştirdi, hiçbirinde bu miktarda kötüleştirmedi. Bununla birlikte aynı kaynaktaki görüntüler ve anotasyonlar fiziksel giysi ölçüsü, eksiksiz topoloji veya üretilebilir kalıp doğrulaması sağlamaz. Yerel hata ve kesişimler sürdüğü için değişiklik ürün algoritmasına alınmadı. `make_color_gradient_experiment.py` kaynak dosya karmasını doğrulayarak deney kopyasını oluşturur; `make_isoluminant_fixture.py` ayrıca eş gri değerli sentetik renk sınırı üretir.
