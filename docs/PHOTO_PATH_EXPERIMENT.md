# Fotoğraf kenarı boyunca yol: deneysel araştırma

1 Kasım 2026'da, iki başlangıç noktası arasında görüntü kontrastını izleyen klasik bir en kısa yol prototipi [aRTF Clothes 1.0.1](https://doi.org/10.5281/zenodo.14501499) test görüntülerinde ölçüldü. Algoritma bir giysiyi tanımaz. Sonrasında fotoğraf çiziminde öneri olarak sunuldu; otomatik kalıp çıkarma ve üretim uygunluğu kanıtlanmamıştır.

Önceki [noktasal öneri deneyi](PHOTO_EDGE_BENCHMARK.md) ile aynı arşiv ve rastgele seçim kullanıldı: 40 şort, 40 tişört fotoğrafı; arşiv SHA-256 `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Görüntüler depoda bulunmaz; lisans koşulları ayrıca değerlendirilmelidir.

Her görüntünün en uzun anotasyon çokgeninde beş başlangıç indisi seçildi ve sınır boyunca en az 40 piksel ilerideki noktayla birleştirildi. **Her iki uç anotasyondan verildi**; bu, kullanıcının uçları seçme hatasını ve tam bir konturun kendiliğinden bulunmasını sınamaz. Çıkan yol ve doğru anotasyon parçası, her iki yönde ortalama en yakın çokgen kenarı uzaklığı alınarak karşılaştırıldı. Düz çizgi aynı uçlar arasında kuruldu. Aynı fotoğraftan gelen parçalar bağımsız değildir.

| Ölçüm | Şort | Tişört | Toplam |
|---|---:|---:|---:|
| Parça | 200 | 200 | 400 |
| Yol bulundu | 200 | 200 | 400 |
| Ortalama simetrik uzaklık: düz çizgi → öneri (piksel) | 1,80 → 1,36 | 1,86 → 1,08 | 1,83 → 1,22 |
| Hata >0,5 piksel azaldı | 62 | 90 | 152 |
| Hata >0,5 piksel arttı | 37 | 22 | 59 |

Arama, uçların çevresindeki sınırlı bir dikdörtgende kontrast ve yol uzunluğunu birlikte maliyetlendirir. En fazla 160.000 piksel inceler, 4.096 noktayı aşan sonucu reddeder ve iptali gözler. İç hesap uçları en yakın tamsayı önizleme pikseline yuvarlar; kullanıcıya sunulan öneri seçilen özgün uçları korur. Arayüzde yol asenkron hazırlanır, önizlenir ve yalnız açıkça kabul edilirse ara noktalar eklenir; son uygulama, başka nokta değişikliği öncesinde tek adımda geri alınabilir. Doku veya gölge izlenebilir; öneri tamamı için kalıp onayı değildir. Proje geçmişi kabul edilmiş noktaları saklar, öneri kökenini ayrı kaydetmez. Gerçek kullanıcılarla, diğer giysi türleriyle ve üretim verileriyle doğrulama eksiktir.

Kullanıcı en fazla 17 çizgi noktası seçerek 16 ardışık aralığın tamamı için tek seferde öneri de isteyebilir; son nokta ilkine otomatik bağlanmaz. Bir aralık başarısızsa bütün öneri reddedilir; noktalar ve referanslar değişmez. Kullanıcı öneriyi inceleyip uygularsa tüm işlem tek adımda geri alınabilir. Bu sayfadaki fotoğraf deneyi tek aralıkları ölçmüştür; bütün giysi konturunun toplu öneriyle doğruluğunu ölçmemiştir. Otomatik giysi bölgesi için incelenen iki klasik yöntem [ayrı deneyde](PHOTO_REGION_EXPLORATION.md) yetersiz kalmıştır.

## Tekrarlama

ZIP'i bağımsız olarak indirin; Windows, .NET 10 SDK ve Python 3.12.14 ile boş bir çalışma klasörü kullanın:

```powershell
python tools/PhotoPathBenchmark/benchmark.py <veri.zip> <dotnet.exe> <boş-çalışma-klasörü>
```

Araç gerçek prototip kaynak dosyasını derler; seçilen görselleri sadece çalışma klasörüne çıkarır, denemeleri ve `summary.json` çıktısını oraya yazar. Veriler depoya eklenmemelidir.
