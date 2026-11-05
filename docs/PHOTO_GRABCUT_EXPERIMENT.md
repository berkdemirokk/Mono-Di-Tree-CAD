# Fotoğraf bölgesi için GrabCut keşif deneyi

5 Kasım 2026. [OpenCV GrabCut](https://docs.opencv.org/4.x/d8/d83/tutorial_py_grabcut.html), ön plan ve arka plan için görüntüden renk dağılımı kurup yinelemeli grafik kesimi yapan klasik bölütleme yöntemidir. Bu araştırmada hazır eğitilmiş ağırlık, ücretli servis veya yerel dil/görüntü modeli kullanılmadı; her görüntünün renk dağılımı o görüntüden hesaplandı. OpenCV yalnız deney klasöründedir, masaüstü ürününe eklenmedi.

## Veri ve yöntem

Aynı aRTF veri kümesinden daha önce kullanılan 40 şort ve 40 tişört geliştirme, 40 şort ve 40 tişört ayrılmış görüntüsü kullanıldı. Kümelerin görüntü kimlikleri ve ham bayt özetleri arasında tekrar yoktur; yine de aynı veri kaynağından gelirler ve bu görüntüler önceki deneylerde incelenmiştir. Yeni bağımsız saha doğrulaması değildir. Kaynak ZIP SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`; görüntüler depoya eklenmedi.

**İyimser giriş:** her dikdörtgen gerçek anotasyon kutusundan ve en az 4 piksel veya büyük kenarın %5'i kadar paydan türetildi; insanın kutuyu bulması ölçülmedi. GrabCut aynı görüntü üzerinde beş yineleme ve sabit rastgele tohumla çalıştırıldı. Karşılaştırma için daha önce denenen Lab renk yayılımı aynı kutuyu ve ek olarak gerçek maskenin en içteki pikselini başlangıç olarak kullandı; bu da gerçek kullanıcı girdisi değildir ve iki yöntemin girişleri eşdeğer değildir. Ayrı koşulda kutunun dört kenarı sabit tohumla, ilgili anotasyon eninin veya boyunun ±%10'u içinde bağımsız kaydırıldı; bu, gözlemlenmiş kullanıcı hatası dağılımı değildir.

IoU, tahmin maskesi ile çokgenden rasterleştirilmiş anotasyon maskesinin kesişim/birleşim oranıdır. Sınır farkı, iki maskenin delikler dahil bir piksel kalınlığındaki iç sınırları arasındaki iki yönlü en yakın piksel merkezi uzaklığıdır; çizgi veya fiziksel milimetre değildir. 5 piksel eşiği yalnız keşif ölçütüdür, üretim toleransı değildir. Bağlı bileşen sayısı en az 25 piksel içeren 8 bağlantılı bölgeleri sayar; bunların gerçekten hatalı olduğunu tek başına göstermez.

| Koşul | GrabCut ortalama / medyan IoU | IoU ≥ 0,8 | Renk yayılımı ortalama IoU | GrabCut sınırı >5 px | Çoklu ≥25 px bölge | Kutunun kestiği referans |
|---|---:|---:|---:|---:|---:|---:|
| Geliştirme, ideal kutu | 0,929 / 0,970 | 69/80 | 0,654 | 70/80 | 6/80 | 0/80 |
| Ayrılmış, ideal kutu | 0,925 / 0,973 | 68/80 | 0,673 | 57/80 | 4/80 | 0/80 |
| Ayrılmış, kutu kenarları kaydırılmış | 0,920 / 0,971 | 71/80 | 0,678 | 65/80 | 3/80 | 47/80 |

Üç koşulun tamamında 80/80 GrabCut çalışması sonuçlandı. Ayrılmış görüntülerde kutu kaydırmasının eşli ortalama IoU farkı −0,00495'tir; dokuz görüntüde 0,05'ten fazla azaldı, altısında 0,05'ten fazla arttı, en büyük azalma 0,382 oldu. İdeal ve kaydırılmış koşulların sırasıyla 57 ve 65 sınır aşımının 54'ü aynı görüntüdür; 11 yeni aşım ve üç eski aşımın kalkması görüldü. İdeal koşulda en büyük tek sınır uzaklığı yaklaşık 102,12 pikseldir. Bunlar görüntü kenarına, anotasyon kalitesine, diğer nesnelere ve maskenin parçalı olmasına duyarlıdır.

## Tekrarlanabilirlik ve karar

İdeal ayrılmış koşul iki kez çalıştırıldığında 80 maskenin SHA-256 özetleri ve IoU değerleri aynıydı; bir sınır maksimumunda yaklaşık `4,77e-7` piksel farkı oluştu. Bu nedenle sayısal raporların tüm baytlarının deterministik olduğu iddia edilmez. Bir üçüncü çalışmada da 80 maske ve IoU eşleşti. Var olan çıktı dosyası üzerine yazma girişimi reddedildi ve önceki çıktı özeti değişmedi.

Bölge IoU sonucu önceki basit yönteme göre araştırmaya değer; ancak yüksek IoU büyük yerel sınır sapmalarını ve parçalı maskeleri gizleyebiliyor. Üründe otomatik kalıp veya güvenli sınır olarak kullanılmadı. Bir sonraki adım; kullanıcı çizdiği kutu ve ön/arka plan düzeltmelerini içeren, yerel ve iptal edilebilir bir prototipte ham maskenin düzenlenmesini, kontura dönüşümde ayrıntı/topoloji kaybını ve farklı gerçek kaynakları ölçmektir. Açık kaynak bağımlılıklarının sürüm, lisans ve çevrimdışı paketlenmesi ayrıca değerlendirilmelidir.

[Çalıştırma aracı](../tools/PhotoRegionBenchmark/benchmark_grabcut.py), [geliştirme satırları](PHOTO_GRABCUT_DEVELOPMENT.json), [ayrılmış satırlar](PHOTO_GRABCUT_HOLDOUT.json), [kaydırılmış satırlar](PHOTO_GRABCUT_JITTER.json) ve [araç özeti](PHOTO_GRABCUT_SCRIPT_SHA256.json) saklandı. Tekrarlamak için arşiv, OpenCV 4.10.0 ve NumPy 2.3.5 yüklü ayrı yerel klasör gerekir:

```powershell
python tools/PhotoRegionBenchmark/benchmark_grabcut.py <arşiv.zip> <opencv-klasörü> <yeni-çıktı.json> holdout 0
python tools/PhotoRegionBenchmark/benchmark_grabcut.py <arşiv.zip> <opencv-klasörü> <başka-yeni-çıktı.json> holdout 0.1
```

Daha sonraki [ayrı C# prototipinde](PHOTO_GRABCUT_SHARP_PROTOTYPE.md) aynı 80 anotasyon kutusuyla maskeler Python/OpenCV çalışmasıyla eşleşti; ürün entegrasyonu ve gerçek kullanıcı doğrulaması henüz yapılmadı.
