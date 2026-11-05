# Sentetik worker → strict okuyucu → WPF araştırma zinciri

Bu bağımsız WPF deneyi, yalnız üç sabit uydurma senaryo kimliği seçtirir; gerçek kişi ölçüsü alan veya worker'a ölçü gönderen bir arayüzü yoktur. Önceki `synthetic-worker-isolation-1105` çalışma ağacındaki `d0f59e5` worker'ı Node aracılığıyla subprocess olarak çalıştırır. Worker çağrısı stdin üzerinden yalnız sabit kimliği gönderir; stdout yanıtının dış zarfında senaryo kimliği kontrol edilir, iki parça verisi `BodiceReader` strict okuyucusundan geçirilir ve WPF'de kaynak isimlerine göre ön/arka eğriler çizilir. Ölçek ve yerleşim yalnız ekran çiziminde uygulanır.

Çalışma ana CAD uygulamasına bağlı değildir. CAD, üretim, kopyalama/çıktı, export, yazdırma ve cihaz işlevi yoktur; işletim sistemi veya başka uygulamaların ekran yakalaması önlenmez. Alt beden, dikiş bağlantısı, topoloji ve uyum doğrulanmadı. Fiziksel kalıp için uzman incelemesi ve numune prova gerekir.

Worker'a 15 saniye sınırı, iptal, stdout 2 MB/stderr 16 KB okuma sınırları ve başarısız süreçte sonuç üretmeme uygulanır. Sınırı aşan okuma, diğer akışların veya süreç çıkışının tamamlanmasını beklemeden temizliği başlatır. Senaryo değişimi, iptal veya yeni istek önceki nesli geçersiz kılar; geç biten eski sonuç yayınlanmaz.

Mevcut `windows-node-probe` deneyi bu protokole doğrudan uygun olmadığından süreç başlatma yöntemi uyarlandı: Windows süreci askıda oluşturulur, Job ataması doğrulanınca çalıştırılır; lider zaten çıkmış olsa bile temizlemede Job sonlandırma denenir ve etkin Job süreçleri ile akışların kapanışı beklenir. Temizlik doğrulanamazsa sonuç verilmez. Bu tek makinede sentetik olarak test edilmiştir; tüm Windows sürümleri, iç içe Job politikaları, beklenmeyen işletim sistemi hataları veya süreç kaçışlarına karşı güvenlik sertifikası değildir. İstisna içeriği UI'da gösterilmez. Job node ağını veya dosyalarını yalıtmaz.

## Önemli sınırlar

Worker için Node izin sistemi ağ erişimini kapatmaz; önceki deneyde loopback bağlantısı gözlenmiştir. Yüklenen bazı kaynak baytları imzasız yerel manifest ile karşılaştırılsa da çalışan runtime ve transitif bağımlılıkların tam kimliği ve lisansları **doğrulanmamıştır**. Bu nedenle yalnız sabit sentetik kimliklere izin verilir; gerçek kişi verisi kullanılmamalıdır. Okuyucu şemayı doğrular, geometri/dikiş semantiği, kaynak dürüstlüğü ve bedene uygunluğu kanıtlamaz. Bu çalışma dağıtım için hazır değildir.

## Tekrar çalıştırma

Bu ağacın kökünde:

```powershell
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' build experiments/wpf-synthetic-worker/SyntheticBridge.csproj -v:q -m:1
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' experiments/wpf-synthetic-worker/bin/Debug/net10.0-windows/SyntheticBridge.dll --verify
```

Başsız doğrulama iki farklı gerçek worker sentetik çıktısını, strict parça okuyucusunu, sahte worker ile bozuk yanıt ve yanlış kimlik reddini, ayrı iptal ve timeout süreçlerini, 2 MB üstü çıktıda sürenin dolmasını beklemeden hatayı, iptalden ve yeni senaryo seçiminden sonra eski sonucun bastırılmasını ve WPF ekran dışı eğri çizimini sınar. Senkronize alt süreç testi önce ana ve alt sürecin yaşadığını, ardından ana süreç çıkarken alt sürecin stdout borusunu açık tuttuğunu gözler; iptalden sonra alt sürecin çıkışını bekler. Test yalnız kendi uydurma PID/release dosyalarını `work/evidence` içinde oluşturup temizler. Görünür pencere açmaz; ekran okuyucu, DPI veya gerçek kullanıcı etkileşimini test etmez. Testler bu makinede 0 hata ve 0 derleme uyarısı ile geçti. Node ve eski worker için kaynak kodundaki mutlak geliştirici yolları, yerel manifest ve önceki deney ağacı gereklidir; bağımsız dağıtım paketi değildir.
