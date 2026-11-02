# Fotoğraf yolunda yöntemler arası uyuşmazlık deneyi

2 Kasım 2026. Fotoğraf yolunun hangi bölümünün ayrıca incelenmesi gerektiğine ilişkin klasik, çevrimdışı bir uyarı ölçütü araştırıldı; ürün algoritması değiştirilmedi. Mevcut yol maliyeti sabiti 0,12 ve [önceki deneydeki](PHOTO_BASE_COST_EXPERIMENT.md) alternatif 0,03 kullanıldı. Aynı fotoğrafta aynı anotasyon kaynaklı 12 uç ve kapanış aralıkları için her iki yol hesaplandı. Uçlar gerçek kullanıcı tıklaması değildir.

Her aralıkta iki piksel yolunun iki yönlü **ayrık nokta kümesi** uzaklığı ölçüldü. Önceden belirlenmiş 1, 2, 3 ve 5 piksel eşikleri raporlandı. Aşağıda, iki yoldan herhangi bir nokta diğer yoldaki tüm noktalardan **1 pikselden uzaksa** o aralık uyarılı sayılır. Bu örneklenmiş, ayrık bir ölçüdür; sürekli kenarlar veya fiziksel ölçü değildir. Uyarı oluşturulurken anotasyon kullanılmaz; yalnız sonuç puanlamasında kullanılır.

| Küme | Konturda >5 px yerel hata | Bunlardan uyarılan | Hatalı olduğu halde uyarılmayan | Uyarılan, >5 px bulunmayan |
|---|---:|---:|---:|---:|
| Geliştirme, 80 görüntü | 65 | 41 | 24 | 5 |
| Ayrılmış, 80 görüntü | 59 | 43 | 16 | 4 |
| Ayrılmış, uçlar yapay 2 px kaydırılmış | 61 | 42 | 19 | 1 |

Kontur düzeyinde aynı konturun başka bir aralığındaki hata, uyarılan aralıkla ilgiliymiş gibi görünebilir. Bu nedenle 960 aralığın her birinde mevcut yolun **piksel noktalarından anotasyon çokgeni kenarlarına tek yönlü** en büyük uzaklık da ölçüldü. Bu ölçü, yolun izlemediği gerçek sınır bölümlerini yakalamaz ve yukarıdaki simetrik kontur metriğiyle aynı değildir.

| Küme | Yolu >5 px sapan aralık | Bunlardan uyarılan | Sapan ama uyarılmayan | Uyarılan ama bu ölçüde >5 px sapmayan |
|---|---:|---:|---:|---:|
| Geliştirme | 157 | 34 | 123 | 37 |
| Ayrılmış | 151 | 27 | 124 | 49 |
| Ayrılmış, 2 px kaydırma | 165 | 33 | 132 | 47 |

Ayrılmış kümedeki uyarılı 76 aralığın 49'unda bu tek yönlü örnekleme 5 pikseli aşmadı; 151 sapan aralığın 124'ü uyarılmadı. Bu veri ve eşiklerde yöntem sorunlu bölümü güvenilir biçimde bulmaz. **Uyarı yokluğu doğruluk kanıtı değildir**; otomatik kabul veya kullanıcıya güvenli/tehlikeli işareti olarak ürüne alınmadı. Anotasyon, 512×256 görseller, iki giysi kategorisi ve aynı veri kaynağındaki önceden incelenmiş ayrılmış görüntüler fiziksel kalıp, ölçek, diğer giysiler veya gerçek kullanıcı testi değildir. 5 piksel üretim toleransı değildir.

[Geliştirme sonuçları](PHOTO_DISAGREEMENT_DEV.json), [ayrılmış sonuçlar](PHOTO_DISAGREEMENT_HOLDOUT.json), [kaydırılmış sonuçlar](PHOTO_DISAGREEMENT_JITTER.json) bütün eşikleri içerir. Karşılaştırma aracı arşiv özetini, anotasyon kimliklerini, uçları, çıktı kimliklerini ve ilgili görüntü baytlarını eşleştirir; yanlışlıkla farklı işler kullanılırsa durur. Bu, kötü niyetli eşzamanlı değişikliklere karşı imzalı kanıt değildir. Görseller depoya eklenmedi; veri lisansı ayrıca değerlendirilmelidir.

Önce [önceki deneye](PHOTO_BASE_COST_EXPERIMENT.md) göre mevcut ve 0,03 alternatif klasörlerini aynı koşulda oluşturun; sonra:

```powershell
python tools/PhotoPathBenchmark/disagreement_assessment.py <mevcut-klasör> <alternatif-klasör> <yeni-sonuç.json> <veri.zip>
python tools/PhotoPathBenchmark/test_disagreement_assessment.py
```

İkinci komut mevcut `work/photo-holdout-12-reference` ve `work/photo-base-cost-holdout-003` deney klasörlerini gerektirir; sentetik bozulmalarla yanlış eşleşme ve var olan çıktının korunmasını sınar. ZIP SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`.

## Ek açık kaynak adayı

[Open+Pattern](https://openpattern.io/) gerçek giysilerden sayısallaştırıldığını belirttiği bazı kalıpları CC BY 4.0 altında sunduğunu söylüyor; örneğin [TuTa sayfası](https://openpattern.io/product/tuta-jumpsuit-thayaht-1920/) tek beden AAMA DXF ve PNG illüstrasyon listeliyor ve sıfır fiyat seçeneği gösteriyor. Bu dosyalar indirilmedi; sayfa, aynı giysiye ait ölçekli gerçek fotoğraf ile parça düzeyinde eşlenmiş fiziksel referans bulunduğunu kanıtlamıyor. AAMA içe aktarma uyumluluğu, görselin türü ve lisansın dosya bazında uygulanması dosyalar edinilip incelenmeden doğrulanmış değildir. Ücret veya hesap işlemi yapılmadı.
