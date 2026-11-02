# Ayrılmış görüntülerde fotoğraf konturu deneyi

2 Kasım 2026'da, mevcut `PhotoEdgePath` davranışı aRTF Clothes 1.0.1 test bölümünden seçilen **40 şort ve 40 tişört** görüntüsünde incelendi. Önceki fotoğraf kenarı, yol ve bölge deneylerinde kullanılan 80 görüntünün kimlikleri dışlandı; dosya baytları bu görüntülerden herhangi biriyle aynı olan başka kimlikler ve ayrılmış kümedeki tekrarlar da dışlandı. Anotasyon yerine görüntü düzeyinde ayırma yapıldı. Görsel olarak benzer veya farklı kodlanmış aynı sahneler dışlanmış sayılmaz; görüntüler aynı veri kümesinden geldiği için bu farklı bir kaynakta genelleme kanıtı değildir.

Her kategoride uygun görüntüler sabit `451827` tohumuyla karıştırıldı, her görüntüden kimliği en küçük uygun anotasyon seçildi. Konturun çevresi üzerinde eşit aralıklı 12 nokta anotasyondan alındı; ürün koduyla ardışık noktalar ve kapanış arasında yol hesaplandı. İkinci koşulda her uç bağımsız, sabit rastgele yönde iki piksel kaydırıldı; hiçbiri görüntü sınırına kırpılmadı. Bunlar gerçek kullanıcı tıklamaları değildir. Karşılaştırma aynı anotasyon kimlikleriyle eşlendi.

| Ölçüm | Anotasyon uçları | İki piksel yapay kaydırma |
|---|---:|---:|
| Tüm aralıkları bulunan / kontur | 80 / 80 | 80 / 80 |
| Taklit edilen nokta kontrollerini geçen | 80 / 80 | 80 / 80 |
| Kontur başına ortalama simetrik uzaklık | 1,45 piksel | 1,59 piksel |
| Örneklenen yerel uzaklığı 5 pikseli aşan kontur | 59 / 80 | 61 / 80 |
| En büyük örneklenmiş yerel uzaklık | 25,61 piksel | 25,61 piksel |
| Sıkı kenar kesişimi bulunan kontur | 1 / 80 | 3 / 80 |

Anotasyon uçları koşulunda aynı uçlar arasındaki düz kenarlı temel çizimin ortalaması 2,38 pikseldi; öneri 60 konturda ortalama hatayı 0,5 pikselden fazla azalttı, ikisinde aynı miktarda artırdı. İki koşulun eşli karşılaştırmasında kaydırma, sekiz konturda ortalama hatayı 0,5 pikselden fazla artırdı ve birinde aynı miktarda azalttı. Kontur başına örneklenen en büyük yerel hata 16'sında bir pikselden fazla arttı, sekizinde aynı miktarda azaldı. Bu farklar hem kaydırmanın hem de yeniden hesaplanan yolun etkisini içerir.

Piksel uzaklığı yaklaşık bir piksel aralıkla örneklenmiş en yakın kenar uzaklığıdır; gerçek en büyük sapmayı garanti etmez. Kesişim sayacı yalnız kenarların iç kısımlarındaki sıkı çapraz geçişleri sayar, temasları ve çakışmaları saymaz. Üründeki nokta sınırı ve sadeleştirme deney aracında taklit edildi; masaüstü arayüzü ile tüm geometri doğrulayıcıları bu deneyde çalıştırılmadı. Anotasyon uçları iyimserdir; anotasyon kusursuz fiziksel kalıp referansı değildir. Bu deney gerçek ölçüler, kumaş dökümü, gizlenen bölgeler, perspektif veya üretim uygunluğunu ölçmez. Sonuç otomatik kabulü haklı çıkarmaz.

[Referans özeti](PHOTO_HOLDOUT_REFERENCE.json), [kaydırma özeti](PHOTO_HOLDOUT_JITTER.json) ve [eşli karşılaştırma](PHOTO_HOLDOUT_COMPARISON.json) yöntem ve görüntü kimliklerini içerir. Kontur başına sayısal sonuçlar [referans](PHOTO_HOLDOUT_REFERENCE_ROWS.json) ve [kaydırma](PHOTO_HOLDOUT_JITTER_ROWS.json) dosyalarındadır. Görüntüler ve anotasyonlar depoya eklenmedi. Referans koşulu iki kez çalıştırıldığında özet ve satır dosyaları bayt düzeyinde aynıydı.

Tekrarlamak için veri arşivi, .NET 10, Python 3.12.14 ve iki yeni boş çalışma klasörü gerekir:

```powershell
python tools/PhotoPathBenchmark/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-referans-klasörü> 12 0 holdout
python tools/PhotoPathBenchmark/contour_benchmark.py <veri.zip> <dotnet.exe> <boş-kaydırma-klasörü> 12 2 holdout
python tools/PhotoPathBenchmark/compare_contours.py <referans-klasörü> <kaydırma-klasörü> <yeni-sonuç.json>
```

Arşiv SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`. Veri lisansının yeniden dağıtım ve ticari kullanım koşulları ayrıca değerlendirilmelidir.
