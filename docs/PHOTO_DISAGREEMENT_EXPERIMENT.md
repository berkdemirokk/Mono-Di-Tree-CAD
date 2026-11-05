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

Karşılaştırma aracı artık bağ bilgisi taşımayan eski sonuçları kabul etmez; deneyleri güncel altyapıyla yeniden üretmek gerekir. Test komutu aşağıdaki gri ve renkli bağlı deney klasörlerini gerektirir; sentetik bozulmalarla iş, sonuç, ölçüm ve kaynak bağı uyuşmazlığı ile mevcut çıktının korunmasını sınar. ZIP SHA-256: `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6`.

## Ek açık kaynak adayı

[Open+Pattern](https://openpattern.io/) gerçek giysilerden sayısallaştırıldığını belirttiği bazı kalıpları CC BY 4.0 altında sunduğunu söylüyor; örneğin [TuTa sayfası](https://openpattern.io/product/tuta-jumpsuit-thayaht-1920/) tek beden AAMA DXF ve PNG illüstrasyon listeliyor ve sıfır fiyat seçeneği gösteriyor. Bu dosyalar indirilmedi; sayfa, aynı giysiye ait ölçekli gerçek fotoğraf ile parça düzeyinde eşlenmiş fiziksel referans bulunduğunu kanıtlamıyor. AAMA içe aktarma uyumluluğu, görselin türü ve lisansın dosya bazında uygulanması dosyalar edinilip incelenmeden doğrulanmış değildir. Ücret veya hesap işlemi yapılmadı.

## Gri ve renkli yol uyuşmazlığı

5 Kasım 2026. Mevcut gri ve standart renkli ürün yolları aynı 80 ayrılmış görüntü, aynı 12 anotasyon noktası ve kapanış aralığıyla karşılaştırıldı. Bu görüntüler daha önce incelenmiştir; yeni bağımsız bir veri kaynağı değildir. İkinci koşulda noktalar deterministik olarak 2 piksel kaydırıldı; bu gerçek kullanıcı tıklaması değildir. Gri yöntem güncel kaynakla yeniden çalıştırıldı; yol ve ölçüm dosyaları önceki gri deneyin her iki koşuluyla bayt düzeyinde eşleşti. Renkli yöntem daha önce aynı çalışma altyapısıyla yeniden üretilmiştir.

Aşağıda, iki yoldan herhangi bir pikselin diğer yoldaki piksel kümesine uzaklığı 1 pikseli aştığında o aralık işaretlenir. Hatalı sayımı gri yolun piksel noktalarının anotasyon sınırına tek yönlü uzaklığının 5 pikseli aşmasına dayanır; izlenmeyen sınır bölümleri bu ölçüde görülmez. 5 piksel fiziksel veya üretim toleransı değildir.

| Koşul | 960 aralıkta >5 px sapma | Sapmış ve işaretli | Sapmış, işaretsiz | İşaretli, bu ölçüde sapmamış |
|---|---:|---:|---:|---:|
| Referans noktaları | 151 | 33 | 118 | 85 |
| 2 px kaydırılmış | 165 | 35 | 130 | 78 |

Bu ölçüt çok sayıda hatalı aralığı işaretlemediği için güvenli/doğru uyarısı olarak ürün akışına eklenmedi. İşaretli aralığın kullanıcı tarafından incelenmesi yararlı olabilir, fakat bu veri uyarının gerçek kullanıcı işine etkisini kanıtlamaz. Anotasyon kusurları, görüntü ölçeği, fiziksel ölçü ve gerçek kalıp doğruluğu değerlendirilmemiştir. [Referans sonucu](PHOTO_GRAY_COLOR_DISAGREEMENT_HOLDOUT.json), [kaydırılmış sonuç](PHOTO_GRAY_COLOR_DISAGREEMENT_JITTER.json) ve gri kaynak özeti ([kaynak hash](PHOTO_GRAY_BOUND_SOURCE_HASH.json), [referans çalışma](PHOTO_GRAY_BOUND_HOLDOUT_SUMMARY.json), [kaydırılmış çalışma](PHOTO_GRAY_BOUND_JITTER_SUMMARY.json)) saklandı; renkli çalışma özetleri [önceki deneyde](PHOTO_WIDE_COLOR_EXPERIMENT.md) yer alır.

Üretmek için `contour_benchmark.py` ile güncel gri 12 noktalı `holdout` koşullarını 0 ve 2 piksel kaydırmayla çalıştırın; `make_wide_color_benchmark.py` ile `standard` renkli deney kopyasını oluşturup aynı koşulları çalıştırın. Sonra `disagreement_assessment.py` ile eşleşen gri ve renkli klasörleri, yeni çıktı yolunu ve aynı ZIP dosyasını verin. Araç her çalışma için iş, sonuç ve ölçüm hashlerini, kullanılan görüntü baytlarını, arşivi, iş uçlarını ve anotasyon kimliklerini denetler. Bu bağlantılar kötü niyetli eşzamanlı değişikliklere karşı imzalı kanıt değildir.
