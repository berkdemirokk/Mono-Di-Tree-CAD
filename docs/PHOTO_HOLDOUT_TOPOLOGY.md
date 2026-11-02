# Ayrılmış fotoğraf önerilerinde gerçek piksel geometri kontrolü

2 Kasım 2026'da [ayrılmış görüntü deneyinin](PHOTO_HOLDOUT_EXPERIMENT.md) mevcut 12 aralıklı yol sonuçları yeniden incelendi. Araç, masaüstündeki gerçek `PhotoTraceCanvas.SetAllPathSuggestions` ve `ProposedTraceSnapshot` kodunu, ardından pencerenin kullandığı `LargeOpenPathValidator`, `LargeContourValidator` ve `PhotoPathCrossingDiagnostic` kodunu çalıştırır. Yalnız çizim renkleri boş yer tutucudur; görsel çizim, pencere veya fiziksel cihaz açılmaz. Her fotoğrafta iki örnek referans noktası eklenir; ölçek dönüşümü yapılmaz. Kontrol kapanış dahil piksel geometrisindedir.

| Ölçüm | Anotasyon uçları | İki piksel yapay kaydırma |
|---|---:|---:|
| Tuvalin kabul ettiği toplu öneri | 80 / 80 | 80 / 80 |
| Piksel geometri denetiminde uyarı | 8 / 80 | 11 / 80 |
| Uyarıda bulunabilen ilk kenar çifti | 8 / 8 | 11 / 11 |
| Önceki dar sayacın bulduğu sıkı kesişim | 1 | 3 |
| Sıkı kesişim olup geometri uyarısı veya kenar çifti bulunmayan | 0 | 0 |

Ek uyarıların nedeni tek tek sınıflandırılmadı; dar sayaç yalnız içten çapraz geçişleri sayarken gerçek denetim temas, çakışma ve diğer geçersizlikleri de inceleyebilir. Geometri kontrolünü geçmek gerçek kumaş sınırının, piksel hatasının veya üretim uygunluğunun doğrulandığı anlamına gelmez. Araç, ürünün piksel inceleme mantığını çağırır; ana pencerenin asenkron durum yönetimi, fareyle kullanım ve milimetreye dönüştürülmüş sonuç bu veriyle sınanmadı. Noktalar anotasyondan veya yapay kaydırmadan gelir; kullanıcı seçimi ve farklı veri kaynağı test edilmedi.

[Referans özeti](PHOTO_HOLDOUT_TOPOLOGY_REFERENCE.json), [kaydırma özeti](PHOTO_HOLDOUT_TOPOLOGY_JITTER.json), [referans satırları](PHOTO_HOLDOUT_TOPOLOGY_REFERENCE_ROWS.json) ve [kaydırma satırları](PHOTO_HOLDOUT_TOPOLOGY_JITTER_ROWS.json) sonuçları içerir. Referans koşulu iki kez çalıştırıldığında satır dosyaları bayt düzeyinde aynıydı. Görüntüler depoya eklenmedi.

Tekrarlamak için önce [önceki deneydeki](PHOTO_HOLDOUT_EXPERIMENT.md) iki çalışma klasörünü oluşturun; sonra Windows'ta .NET 10 ve Python 3.12 ile, her koşulun kendi dosyalarını eşleştirerek:

```powershell
dotnet build tools/PhotoTopologyBenchmark/PhotoTopologyBenchmark.csproj -c Release
dotnet tools/PhotoTopologyBenchmark/bin/Release/net10.0-windows/PhotoTopologyBenchmark.dll <klasör/jobs.json> <klasör/results.json> <yeni-topoloji.json>
python tools/PhotoTopologyBenchmark/summarize.py <yeni-topoloji.json> <klasör/measurements.json> <klasör/summary.json> <yeni-özet.json>
```

Önceki arşivin SHA-256 değeri `af8e0a57bbd7dd7933e9210e2956b9a3e6a2fb0f030cfbeb7d12fb55af470ee6` idi. Dosyaların eşleşmesini araç çalıştıran kişi sağlamalıdır; sıra numarası tek başına farklı koşulların karıştırılmasını saptamaz.
