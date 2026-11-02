# Düzlem perspektifi: gerçek fotoğraflarla sınırlı geometrik deney

**Tarih:** 2 Kasım 2026. **Amaç:** uygulamanın mevcut düzlem dönüşümünü, gerçek kamera görüntülerinde düz bir dama deseni üzerindeki referans dışı aralıklarla sınamak. Bu bir giysi, milimetre, kalıp, üretim veya kullanıcı kabul testi değildir.

## Görüntüler ve yöntem

- [OpenCV kalibrasyon örneklerinin](https://docs.opencv.org/4.4.0/dc/dbb/tutorial_py_calibration.html) `left01`–`left09` ve `left11`–`left14` JPEG fotoğrafları kullanıldı: 13 görüntü, her biri 640 × 480 piksel. Kaynak `opencv/opencv` etiketi `4.10.0`, Git commit `67f3511f2354ebfb5f98045ee6153345f632aadd`; bu etikette `left10.jpg` bulunmadı. Fotoğraflar depoya eklenmedi. [Ham sonuç dosyasında](PHOTO_PLANE_PHOTO_BENCHMARK.json) her dosyanın SHA-256 özeti ve görüntü bazında sonuçlar bulunur.
- OpenCV 4.10.0'ın klasik satranç tahtası köşe bulucusu ve piksel altı düzeltmesiyle 9 × 6 iç köşe belirlendi. Bu bağımsız bir elle işaretleme veya fiziksel ölçüm değildir. Model veya AI kullanılmadı; OpenCV yalnız deney aracında, çalışma klasörüne geçici kuruldu; uygulamaya bağımlılık olarak eklenmedi.
- Dört uç köşe, 8 × 5 **kare kenarı birimlik** düz dikdörtgen olarak kabul edildi. Karenin gerçek fiziksel büyüklüğü doğrulanmadığı için sayılar milimetreye çevrilmedi. Çekirdek `PhotoPlaneRectification.Convert` gerçek uygulama kodundan çağrıldı. Tek mesafe karşılaştırmasında ilk ve son üst köşe arası 8 birim kabul edilerek mevcut `PhotoTraceCalibration.Convert` kullanıldı.
- Dış referanslarda kullanılmayan içteki 28 köşeden 45 komşu yatay/dikey kenar oluşturuldu. Her birinin beklenen uzunluğu bir kare kenarı kabul edilip mutlak uzunluk hatası hesaplandı. Aynı fotoğraftaki kenarlar bağımsız gözlemler değildir.
- Ayrıca her fotoğrafta dört referans köşesinin X/Y koordinatlarına bağımsız, eşit dağılımlı ±1 piksel yapay sapma uygulayan 20 tekrarlı deneme yapıldı. Rastgele tohum `4837` sabittir. Bu, gerçek kullanıcı tıklama hatasının dağılımını temsil etmez; yalnız sınırlı duyarlılık örneğidir. Görüntülere lens düzeltmesi uygulanmadı.

## Sonuç

13 fotoğrafın tamamında köşeler bulundu ve dönüşümler hesaplandı. Deney ayrı bir çalışma klasöründe tekrarlandığında sonuç JSON dosyası aynı SHA-256 değerini verdi (`7D52BCFD74054BC30A6E1F55AE85596933540ECD4D725FE7A7D1C4DAED6DB0CC`).

| Ölçüm: mutlak kenar uzunluğu hatası, kare kenarı birimi | Sayı | Ortalama | Medyan | %95'lik değer | En büyük |
|---|---:|---:|---:|---:|---:|
| Dört köşeli düzlem, algılanan referans | 585 | 0,0241 | 0,0247 | 0,0427 | 0,0523 |
| Tek mesafeli ölçek, aynı görüntüler | 585 | 0,0975 | 0,0764 | 0,2424 | 0,6279 |
| Düzlem, referans köşelerine ±1 px yapay sapma | 11.700 | 0,0244 | 0,0248 | 0,0436 | 0,0577 |

260 sapmalı dönüşümün tamamı hesaplandı. Bu veri ve seçilen ölçüt üzerinde düzlem dönüşümü daha düşük ortalama hata verdi; bu, bütün görüntülerde daha iyi sonuç veya belirli bir fiziksel tolerans garantisi değildir. Tahtanın tam düzlüğü/kare ölçüleri, kamera iç parametreleri, köşe algılama hatası ve fotoğrafların kaynak koşulları bağımsız ölçülmedi. Fotoğraflar tek örnek serisidir; kumaş ve giysi içermez. Geniş kamera, lens, açı, ışık, kullanıcı seçimi ve metrik referans korpusu ile gerçek kalıp ve üretim testi hâlâ gereklidir.

## Tekrarlama

İlgili 13 dosyayı kaynak etiketin `samples/data` dizininden bağımsız olarak boş bir görüntü klasörüne alın. Windows, .NET 10 SDK ve Python 3.12 ile Python ortamında NumPy ve `opencv-python-headless==4.10.0.84` bulundurun; örneğin bu bağımlılığı yalnız ayrı bir deney klasörüne kurun. Ürün çalıştırmak için OpenCV gerekmez.

```powershell
python tools/PhotoPlaneBenchmark/benchmark.py <görüntü-klasörü> <opencv-python-klasörü> <dotnet.exe> <boş-çalışma-klasörü>
```

Araç mevcut uygulama çekirdeğini derler ve köşe koordinatları, tüm sonuçlar ve `summary.json` dosyasını yalnız boş çalışma klasörüne yazar. Veri setini, geçici OpenCV kurulumunu ve fotoğrafları ürün deposuna eklemeyin.
