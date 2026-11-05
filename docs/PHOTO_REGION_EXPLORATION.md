# Fotoğraftan giysi bölgesi: klasik yöntemlerin keşif deneyi

**Tarih:** 2 Kasım 2026. Bu deney, fotoğraf üzerindeki bütün giysiyi tek adımda ayırma olasılığını araştırır; ürün özelliği, gerçek kullanıcı deneyi veya kalıp doğrulaması değildir. Veri, [aRTF Clothes 1.0.1](https://doi.org/10.5281/zenodo.14501499) test bölümündeki 40 tişört ve 40 şort fotoğrafından oluşur. Aynı 80 görüntü daha önceki kenar yolu deneyinde kullanıldı; bağımsız doğrulama kümesi değildir.

## Yöntem

Kaynak veri ZIP dosyasının SHA-256 değeri `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`; görüntüler ve anotasyonlar depoya eklenmedi. Veri lisansının yeniden dağıtım ve ticari kullanım koşulları ayrıca değerlendirilmelidir. Örnekler kategori başına 40 adet olacak biçimde `73114` tohumuyla seçildi. OpenCV 4.10.0 klasik görüntü işlemleri yalnız deneyde çalıştırıldı; ürün bağımlılığı veya AI modeli eklenmedi.

Anotasyon çokgenleri piksel maskesine dönüştürüldü. **Gerçek duruma göre iyimser olarak**, başlangıç noktası maskenin kenarından en uzak iç pikselden, çalışma çerçevesi ise gerçek anotasyon kutusuna %5 pay eklenerek üretildi. Yani bölgenin veya çerçevenin otomatik bulunması ve bir kullanıcının gerçek seçimi ölçülmedi. Tahmin ve maske kesişiminin birleşimlerine oranı (IoU) hesaplandı; yüksek IoU dahi kenar uzunluğu, çentik veya üretim doğruluğunu kanıtlamaz.

Ön incelemede farklı eşikler denendikten sonra burada gösterilen sabit parametreler seçildi; sonuç **aynı veri üzerinde seçilmiş keşif sonucudur**, ayrılmış test başarımı değildir. Bulanıklaştırılmış Lab görüntüsünde sabit eşikli renk yayılımı ile sınır işaretli watershed karşılaştırıldı. Ayrıntılı değerler [ham sonuç dosyasında](PHOTO_REGION_EXPLORATION.json) yer alır.

| Yöntem | Ortalama IoU | Medyan IoU | IoU ≥ 0,8 | IoU < 0,5 |
|---|---:|---:|---:|---:|
| Renk yayılımı, 5×5 yumuşatma, eşik 50 | 0,654 | 0,711 | 31/80 | 23/80 |
| Watershed, 5×5 yumuşatma | 0,520 | 0,518 | 0/80 | 32/80 |

80 görüntünün tamamı işlendi. Deney ikinci kez aynı JSON özetiyle sonuçlandı (SHA-256 `6EB9CBBA357E32191E4F9F7EEED61B18845DEA6BC0E98113DDF4DA53D793E785`). Her iki yöntem iyimser girişlere rağmen belirgin eksik veya taşmış bölgeler üretebildiği için otomatik giysi/kalıp çıkarımı olarak ürüne eklenmedi. Daha geniş giysi türleri, farklı arka planlar, gerçek kullanıcı girdileri ve ayrılmış test kümesi gereklidir.

## Tekrarlama

Veri kümesinin `artf-all-categories-resized_512x256.zip` dosyasını bağımsız indirin. Windows'ta Python 3.12, NumPy ve ayrı bir klasöre kurulu `opencv-python-headless==4.10.0.84` ile boş bir çalışma klasörü kullanın:

```powershell
python tools/PhotoRegionBenchmark/benchmark.py <veri.zip> <opencv-python-klasörü> <boş-çalışma-klasörü>
```

Araç yalnız sayısal `summary.json` üretir. Veri ve geçici bağımlılıklar ürüne dahil edilmemelidir.

Daha sonraki [GrabCut ve kutu duyarlılığı deneyi](PHOTO_GRABCUT_EXPERIMENT.md), daha yüksek bölge IoU’suna rağmen yaygın yerel sınır sapmaları ve bazı parçalı maskeler buldu.
