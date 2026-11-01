# Fotoğraf kenar önerisi: sınırlı gerçek görüntü deneyi

1 Kasım 2026'da `3918272` sürümündeki yerel kontrast önerisi, Ghent Üniversitesi'nin [aRTF Clothes 1.0.1](https://doi.org/10.5281/zenodo.14501499) veri kümesindeki test görüntülerinin küçük bir örneğinde ölçüldü. Kaynak kayıt, gerçek ev ortamındaki giysi görüntülerini ve nesne maskesi anotasyonlarını tanımlar. Bu, giysi sınırı anotasyonuna yakınlık deneyidir; fotoğraftan kalıp çıkarma, beden uyumu veya üretim doğrulaması değildir.

Veri dosyası `artf-all-categories-resized_512x256.zip`: kaynağın verdiği MD5 `40f8a228bb76476499c89fe8c15ae138`; indirilen dosyanın SHA-256 değeri `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Görüntüler ve anotasyonlar bu depoya dahil edilmemiştir; kaynaktaki veri lisansı yeniden dağıtım veya ticari kullanım için ayrıca değerlendirilmelidir.

## Yöntem ve sonuç

Önceden belirlenmiş rastgele tohumla test bölümünden 40 şort ve 40 tişört görüntüsü seçildi. Her görüntüde en uzun anotasyon çokgeninin 20 indis konumu alındı; yerel sınıra dik iki yönde 4 piksel ötelenerek 3.200 yapay başlangıç noktası oluşturuldu. Gerçek kullanıcı tıklaması değildir. Her nokta için, ürün kodundaki `PhotoEdgeSuggestion.TryFind` çalıştırıldı ve başlangıç ile önerinin en yakın çokgen kenarına uzaklığı ölçüldü. Öneri yoksa başlangıç noktası korunmuş sayıldı. İndis üzerinden seçilen noktalar çevre uzunluğuna göre eşit aralıklı değildir.

| Ölçüm | Şort | Tişört | Toplam |
|---|---:|---:|---:|
| Deneme | 1.600 | 1.600 | 3.200 |
| Öneri bulundu | 1.586 | 1.541 | 3.127 |
| Sınıra uzaklık >0,5 piksel azaldı | 992 | 1.066 | 2.058 |
| Sınıra uzaklık >0,5 piksel arttı | 160 | 148 | 308 |
| Öneriden önce sınıra ≤2 piksel | 20 | 12 | 32 |
| Öneriden sonra sınıra ≤2 piksel | 837 | 893 | 1.730 |
| Ortalama uzaklık, önce → sonra (piksel) | 3,68 → 2,32 | 3,69 → 2,23 | 3,69 → 2,28 |

Pikseller aynı fotoğraf üzerinde kümelenmiştir; 3.200 deneme bağımsız fotoğraf sayısı değildir. Anotasyon sınırları kusursuz fiziksel gerçeklik kabul edilemez; fotoğraflar 512×256 boyutuna dönüştürülmüştür ve bu örnek iki giysi türünü kapsar. Kumaş katları, doku, gölgeler, örtülme, başka cihazlar ve gerçek kullanıcıların başlangıç seçimleri için performans bu sonuçtan çıkarılamaz. Özellikle 308 uzaklaşma nedeniyle öneri otomatik uygulanmaz. Üründe öneriyi kabul edip etmemek kullanıcıya bırakılır.

## Tekrarlama

Windows'ta .NET 10 SDK ve Python 3.12.14 ile, yukarıda belirtilen ZIP bağımsız olarak indirilip aşağıdaki komut çalıştırılır:

```powershell
python tools/PhotoEdgeBenchmark/benchmark.py <veri.zip> <dotnet.exe> <geçici-çalışma-klasörü>
```

Araç ürünün mevcut C# kaynak dosyasını doğrudan derler, seçilmiş görüntüleri yalnız verilen boş geçici klasöre çıkarır ve ayrıntılı denemelerle `summary.json` yazar. Veri klasörünü depoya eklemeyin. Deney eşik seçimini veya üretim onayını belirlemez.
