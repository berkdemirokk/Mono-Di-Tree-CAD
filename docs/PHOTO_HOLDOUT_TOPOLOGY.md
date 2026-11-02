# Ayrılmış fotoğraf önerilerinde gerçek piksel geometri kontrolü

2 Kasım 2026'da [ayrılmış görüntü deneyinin](PHOTO_HOLDOUT_EXPERIMENT.md) mevcut 12 aralıklı yol sonuçları yeniden incelendi. Araç, masaüstündeki gerçek `PhotoTraceCanvas.SetAllPathSuggestions` ve `ProposedTraceSnapshot` kodunu, ardından pencerenin kullandığı `LargeOpenPathValidator`, `LargeContourValidator` ve `PhotoPathCrossingDiagnostic` kodunu çalıştırır. Yalnız çizim renkleri boş yer tutucudur; görsel çizim, pencere veya fiziksel cihaz açılmaz. Her fotoğrafta iki örnek referans noktası eklenir; ölçek dönüşümü yapılmaz. Kontrol kapanış dahil piksel geometrisindedir.

| Ölçüm | Anotasyon uçları | İki piksel yapay kaydırma |
|---|---:|---:|
| Tuvalin kabul ettiği toplu öneri | 80 / 80 | 80 / 80 |
| Piksel geometri denetiminde uyarı | 8 / 80 | 11 / 80 |
| Uyarıda bulunabilen ilk kenar çifti | 8 / 8 | 11 / 11 |
| Piksel geometri denetiminden geçen | 72 / 80 | 69 / 80 |
| Denetimden geçenlerde 5 pikseli aşan örneklenmiş yerel uzaklık | 51 / 72 | 55 / 69 |
| Önceki dar sayacın bulduğu sıkı kesişim | 1 | 3 |
| Sıkı kesişim olup geometri uyarısı veya kenar çifti bulunmayan | 0 | 0 |

Ek uyarıların nedeni tek tek sınıflandırılmadı; dar sayaç yalnız içten çapraz geçişleri sayarken gerçek denetim temas, çakışma ve diğer geçersizlikleri de inceleyebilir. Geometri kontrolünü geçmek gerçek kumaş sınırının, piksel hatasının veya üretim uygunluğunun doğrulandığı anlamına gelmez. Araç, ürünün piksel inceleme mantığını çağırır; ana pencerenin asenkron durum yönetimi, fareyle kullanım ve milimetreye dönüştürülmüş sonuç bu veriyle sınanmadı. Noktalar anotasyondan veya yapay kaydırmadan gelir; kullanıcı seçimi ve farklı veri kaynağı test edilmedi.

İki koşulda da geometri denetiminden geçenler arasında en büyük örneklenmiş yerel uzaklık 25,61 pikseldi. Bu eşleştirme, geçerli görünen konturun anotasyona yakın olduğunu varsaymamak için somut bir örnektir; anotasyonlar fiziksel kalıp referansı değildir ve örnekleme sürekli en büyük hatayı garanti etmez.

[Referans özeti](PHOTO_HOLDOUT_TOPOLOGY_REFERENCE.json), [kaydırma özeti](PHOTO_HOLDOUT_TOPOLOGY_JITTER.json), [referans satırları](PHOTO_HOLDOUT_TOPOLOGY_REFERENCE_ROWS.json) ve [kaydırma satırları](PHOTO_HOLDOUT_TOPOLOGY_JITTER_ROWS.json) sonuçları içerir. Referans koşulu iki kez çalıştırıldığında satır dosyaları bayt düzeyinde aynıydı. Görüntüler depoya eklenmedi.

Tekrarlamak için önce [önceki deneydeki](PHOTO_HOLDOUT_EXPERIMENT.md) iki çalışma klasörünü oluşturun; sonra Windows'ta .NET 10 ve Python 3.12 ile, her koşulun kendi dosyalarını eşleştirerek:

```powershell
dotnet build tools/PhotoTopologyBenchmark/PhotoTopologyBenchmark.csproj -c Release
dotnet tools/PhotoTopologyBenchmark/bin/Release/net10.0-windows/PhotoTopologyBenchmark.dll <klasör/jobs.json> <klasör/results.json> <yeni-topoloji.json>
python tools/PhotoTopologyBenchmark/summarize.py <yeni-topoloji.json> <klasör/measurements.json> <klasör/summary.json> <yeni-özet.json>
```

Önceki arşivin SHA-256 değeri `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6` idi. Önceki sonuçlar yalnız sıra numarasına dayanıyordu; aşağıdaki yeni eşleştirme bu eski kanıtı geriye dönük doğrulamaz.


## Kaynağı eşleştirilmiş tekrar

Özetleyici, yeni çalıştırmalarda kontur deneyi ile topoloji sonucundaki `jobs.json`, `results.json` ve 80 görselin SHA-256 değerlerini eşleştirir; ölçüm dosyasının bayt özeti de kontur çalıştırmasının kaydıyla aynı olmalıdır. Eşleşmeyen veya eski biçimli girdiler reddedilir ve özet dosyası oluşturulmaz. İş dosyasında mutlak görsel yolları bulunduğundan farklı klasörlerdeki ayrı çalıştırmaların eşit sayılması beklenmez. Geçmişte yalnız sıra numarasıyla yapılan özetler bu korumadan yararlanmamıştı; geçmişteki eşleşme bu yöntemle geriye dönük kanıtlanmış sayılmaz.

Güncel araçlarla 80 ayrılmış fotoğrafta deney yeniden çalıştırıldı: 8/80 topoloji uyarısı, 72 geçerli içinde 51/72 için 5 piksel üzeri örneklenmiş yerel sapma elde edildi. Önceki sayılarla uyumlu olsa da bu, aynı kaynak kümesinde tekrardır; bağımsız bir kullanıcı veya fiziksel kalıp testi değildir. Gerçek 16 noktalı topoloji dosyasının 12 noktalı ölçümle karıştırılması `jobs_sha256` uyuşmazlığıyla reddedildi. Kontrolün otomatik testleri, iş, yol, görsel ve ölçüm uyumsuzlukları ile eski biçimi de sınar. [Çalıştırma özeti](PHOTO_PAIRED_HOLDOUT_SUMMARY.json), [topoloji özeti](PHOTO_PAIRED_HOLDOUT_TOPOLOGY.json), [topoloji satırları](PHOTO_PAIRED_HOLDOUT_TOPOLOGY_ROWS.json) ve [ölçüm satırları](PHOTO_PAIRED_HOLDOUT_ROWS.json) saklandı; fotoğraflar depoya eklenmedi.

Bu özetler yanlışlıkla dosya karışmasını saptar; imzalı kayıt, eşzamanlı kötü niyetli değişikliğe karşı koruma veya görüntünün giysi sınırına uygunluğu kanıtı değildir. Veri, çalışma boyunca değiştirilmemelidir. Tekrar için yukarıdaki komutlardan önce güncel `contour_benchmark.py` ile yeni boş klasörde bir kontur çalıştırması yapılmalıdır; eski özet ve topoloji dosyaları yeni özetleyiciyle karıştırılamaz.

Eşleştirme regresyonlarını veri indirmeden çalıştırmak için `python tools/PhotoTopologyBenchmark/test_pairing.py` kullanılabilir. Test verileri sentetiktir; test dosyaları `work` altında geçici olarak oluşturulur.
