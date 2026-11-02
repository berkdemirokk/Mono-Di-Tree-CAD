# Fotoğrafta kapanış dahil bütün kontur deneyi

2 Kasım 2026'da [aRTF Clothes 1.0.1](https://doi.org/10.5281/zenodo.14501499) veri kümesinin aynı 40 şort ve 40 tişört test görüntüsünde kapanış dahil yol önerisi ölçüldü. Bu, [önceki deneylerde](PHOTO_PATH_EXPERIMENT.md) kullanılan örneklemdir; bağımsız doğrulama kümesi değildir. Kaynak görüntüler ve anotasyonlar depoya eklenmez; veri lisansı yeniden dağıtım ve ticari kullanım için ayrıca değerlendirilmelidir.

En uzun anotasyon çokgeninin çevresi üzerinde eşit mesafeli 12 nokta seçildi. Her ardışık çift ve son-ilk çift için ürünün `PhotoEdgePath` kodu çalıştırıldı. Üründeki doğrusal ara nokta sadeleştirmesi, uçların korunması ve iki referans noktasına göre 512 nokta sınırı deney aracında taklit edildi; masaüstü arayüzü ve tüm doğrulayıcılar bu deneyde çalıştırılmadı. Yol başarısız olan veya taklit edilen nokta kontrollerinden geçmeyen konturlar uzaklık hesaplarına alınmaz. Karşılaştırma, aynı 12 noktayı düz kenarlarla birleştiren çokgendir.

Bu düzen **iyimserdir**: 12 nokta gerçek anotasyondan gelir, kullanıcı seçim hatası ölçülmez ve noktalar üretim kalıbındaki dikiş veya çentik noktaları değildir. Konturları yaklaşık bir piksel aralıkla örnekleyip her iki yönde diğer çokgenin en yakın kenarına uzaklık hesaplandı. Kontur başına simetrik ortalama ve örneklenen en büyük uzaklık piksel cinsindendir; gerçek ölçekteki hata değildir. Sınır anotasyonları da kusursuz üretim referansı olarak kabul edilemez.

| Ölçüm | Şort | Tişört | Toplam |
|---|---:|---:|---:|
| Kontur / tüm aralıkları bulunan | 40 / 40 | 40 / 40 | 80 / 80 |
| Taklit edilen nokta kontrollerini geçen | 40 | 40 | 80 |
| Kontur başına ortalama uzaklık, düz → öneri (piksel) | 2,09 → 1,37 | 2,70 → 1,48 | 2,39 → 1,42 |
| Ortalama hata 0,5 pikselden fazla azalan / artan | 22 / 0 | 32 / 0 | 54 / 0 |
| Örneklenen yerel uzaklığı 5 pikseli aşan kontur | 33 | 32 | 65 |
| En büyük örneklenmiş yerel uzaklık (piksel) | 19,05 | 24,34 | 24,34 |
| Sıkı kenar kesişimi bulunan kontur | 0 | 0 | 0 |

Kesişim sayacı yalnız birbirini iç kısımlarında çapraz kesen kenarları sayar; teğet temasları veya çakışan kenarları saptamaz ve tam topoloji doğrulaması değildir. Örnekleme sürekli eğrideki gerçek en büyük sapmayı garanti etmez. Ortalama iyileşmeye rağmen çok sayıdaki yerel sapma, önerinin ayrıntılı kullanıcı incelemesi olmadan kalıp kabul edilemeyeceğini gösterir. Perspektif, kumaş dökümü, gerçek ölçüler, parça ayrımı ve üretim uygunluğu ölçülmedi. [Ham sonuç](PHOTO_CONTOUR_EXPERIMENT.json) iki ayrı çalıştırmada aynı çıktı; kaynak ZIP SHA-256 değeri `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`.

## Tekrarlama

Veri kümesinin `artf-all-categories-resized_512x256.zip` dosyasını bağımsız indirin. Windows, Python 3.12, .NET 10 SDK ve boş bir çalışma klasörüyle:

```powershell
python tools/PhotoPathBenchmark/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-çalışma-klasörü>
```

Araç kaynak fotoğrafları ve ara sonuçları yalnız çalışma klasörüne çıkarır. Ticari ürüne veri veya model eklemez.
