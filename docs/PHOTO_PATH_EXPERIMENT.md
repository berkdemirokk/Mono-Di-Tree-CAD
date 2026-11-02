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

Kullanıcı en fazla 17 çizgi noktası seçerek ardışık aralıkların tamamı için tek seferde öneri isteyebilir. `Kapatmayı incele` seçildiğinde `Çizgi + kapanış öner` düğmesi, son noktadan ilk noktaya giden yol için de öneri hesaplar. Öneri uygulandığında ilk nokta yinelenmez; taslak açık kalır. CAD'e ekleme sonrası kapanış aracı kalan son kenarı düz olarak ayrıca önizler ve onayla ayrı kapalı parça oluşturur; fotoğraf geçmişi açık kaynakta kalır. Bir aralık başarısızsa bütün öneri reddedilir; noktalar ve referanslar değişmez. Kullanıcı öneriyi inceleyip uygularsa tüm işlem tek adımda geri alınabilir. Bu sayfadaki fotoğraf deneyi tek aralıkları ölçmüştür. Daha sonra aynı görüntülerde yapılan [kapanış dahil kontur deneyi](PHOTO_CONTOUR_EXPERIMENT.md), ortalama iyileşmeye rağmen önemli yerel sapmalar gösterdi. Otomatik giysi bölgesi için incelenen iki klasik yöntem [ayrı deneyde](PHOTO_REGION_EXPLORATION.md) yetersiz kalmıştır.

Öneri incelenirken fotoğrafa sağ tıklamak inceleme imlecini taşır; yakınlaştırılmış görünüm o konuma ortalanır. Yakınlaştırma düğmesi imleç konumunu kullanır. Bu görünüm işlemleri seçilmiş noktaları veya bekleyen öneriyi değiştirmez. Klavyeyle imleç ok tuşlarıyla, daha hızlı olarak Shift ve ok tuşlarıyla taşınabilir. Görünüm kontrolü sapmayı otomatik saptamaz; gerçek fare ve erişilebilirlik kullanıcı kabulü yapılmadı.

`Düzelt…`, seçilmiş bir çizgi noktasını önizleme piksel koordinatlarıyla değiştirir, seçilenin sonrasına nokta ekler veya seçilen noktayı siler. Kullanıcı yeni yeri önce sağ tıklayıp açılan pencerede `İnceleme imlecini kullan` seçebilir veya koordinat girebilir. Referans noktaları değiştirilemez; komşu noktayla aynı konum ve iki çizgi noktasından daha aza inen silme reddedilir. Değişiklik bekleyen öneriyi kaldırır; `Geri al` son nokta düzenlemesini veya ondan önce düzenleme yapılmadıysa son uygulanan yolu bir kez geri alır. CAD'e kaydedilen fotoğraf geçmişi düzeltilmiş son noktaları içerir. Düzeltme, bütün konturun geçerli ya da üretime uygun olduğunu kanıtlamaz.

Aynı penceredeki `Aralığı düzleştir…` iki çizgi ucu arasında kaç noktanın kaldırılacağını gösterir; açık onayla aradaki tüm noktaları kaldırır ve uçları düz bir kenarla bağlar. Uçlar ve diğer noktalar korunur; son işlem bir kez geri alınabilir. Araç sorunlu aralığı tespit etmez, uçların seçimi ve yeni düz kenarın uygunluğu kullanıcıya aittir. Referans noktaları bu işlemle kaldırılamaz.

## Tekrarlama

ZIP'i bağımsız olarak indirin; Windows, .NET 10 SDK ve Python 3.12.14 ile boş bir çalışma klasörü kullanın:

```powershell
python tools/PhotoPathBenchmark/benchmark.py <veri.zip> <dotnet.exe> <boş-çalışma-klasörü>
```

Araç gerçek prototip kaynak dosyasını derler; seçilen görselleri sadece çalışma klasörüne çıkarır, denemeleri ve `summary.json` çıktısını oraya yazar. Veriler depoya eklenmemelidir.
