# Test raporu — 31 Ekim 2026

## Son kaydedilmiş kanıt

- 31 Ekim 2026, yaklaşık 07:59 yerel saatte biten tam test çalışmasında **1.144 test geçti, 4 test başarısız oldu, 0 test atlandı** (`work/dxf-source-check-full.log`). Bu sonuç sonraki kod değişiklikleri için tekrar çalıştırılmadan geçerli sayılmamalıdır.
- Dört başarısızlığın ikisi, üç loblu yoğun içbükey konturun dikiş payı işleminde iki kontur yönü için reddedilmesidir (`SmoothAllowanceRegressionTests`). Diğer ikisi, üretimde kullanılmayan bağımsız kapsama tanısının aynı konturu iki yönde tam kapsanmış raporlayamamasıdır (`OffsetShadowCoverageTests`). Bu kontrolleri gevşetmek küçük çentik kaybını gizleyebilir; sorun çözülmüş değildir.
- Yaklaşık 08:01 yerel saatte Release derlemesi 0 uyarı/0 hata ile tamamlandı ve penceresiz masaüstü doğrulamasında 744 kontrol geçti (`work/dxf-retry-position-build.log`, `work/dxf-retry-position.json`). Bunlar gerçek kullanıcı, ekran okuyucu, fabrika veya fiziksel plotter kabul testi değildir.
- Dosya temelli ölçümler ve tarayıcı/ekran görüntüleri yalnız belirtilen senaryoları kapsar. Uygulama ticari üretime hazır veya sertifikalı değildir.

## Tarihsel ilk sürüm kanıtı — mevcut sonucu temsil etmez

### O zamanki doğrulanmış sonuçlar

- .NET SDK 10.0.403, Windows 11 x64.
- `dotnet build TekstilCAD.slnx -c Release`: 0 uyarı / 0 hata.
- `dotnet test ... -c Release --no-build`: 156 başarılı / 0 başarısız / 0 atlanan.
- TRX: work/test-results/verification-final.trx (üretilen çıktı, kaynak ZIP'e dahil değil).
- Geometri, koordinat/çalışma sınırları, geçmiş, JSON/DXF/SVG/HPGL, serileme, pastal, dikiş payı/nokta, makine simülasyonu ve imzalı lisans testleri.
- Gerçek WearCAD DXF: ayrı kopyada 100 x 100 mm / 10000 mm² / kapalı kontur; özgün dosya SHA256 önce/sonra aynı: 74B74B88B93ACB4A53BDD5225926F2A3F0DCCD2010E6295F3068B77AEDF84214.
- `TekstilCAD.Studio.dll --verify ...`: 12 arka plan kontrolü PASS. Pencere göstermeden yükle/ekle/undo/redo, geçersiz işlemi geri döndürme, kanvas hata işleme, gerçek SaveTo ile kayıt/açma, WPF render ve iki araç penceresi oluşturma/layout kontrol edildi.
- İlk sürümde görünür UI üzerinde dikdörtgen çizimi ve undo görüldü; kayıt diyaloğu açıldı. Diyalogdan kayıt tamamlanmadı, tam UI kabul testi sayılmaz.

### O zamanki inceleme düzeltmeleri

A11 sayısal taşma -> koordinat alanı ve sonlu sonuç sınırları.
A23 yol sonu boşluk/nokta takma adları -> paylaşılan yazma koruması; guard-only testleri.
A23 PID kurtarma çakışması -> GUID; geri alma belleği -> 50 kayıt / 2 milyon nokta.
A24/A25/A26 isim ve işlem bütçeleri -> girdi kontrolleri ve regresyon testleri.
A30 kanvas InvalidDataException kaçışı -> yakalama ve arka plan regresyon kontrolü.
Makine simülasyonu değiştirilmiş dosya -> Failed durumunun kalıcılaştırılması düzeltildi.

### Tamamlanmamış doğrulamalar

Fiziksel makine, gerçek üretim kesimi, tam AAMA/ASTM, mobil, ödeme, kurulum imzası, tüm UI diyalogları, geniş veri/performance korpusu doğrulanmadı. Pencere göstermeyen render kontrolü etkileşimli kullanıcı kabulünün yerine geçmez.

## Tarihsel paket doğrulaması — 001f866dfb1a

001f866dfb1a kaynak checkpointi için Windows x64 self-contained ZIP ve kaynak ZIP oluşturuldu. Kaynak arşivi 67 dosya içeriyor; bin/obj/.tools/work veya gizli anahtar yok. ZIP üyeleri ve SHA256 değerleri doğrulandı. Kaynak ZIP'i ayrı temiz klasöre açılıp 0 hata/uyarıyla derlendi; 156 test yeniden geçti. Windows ZIP'i ayrıca açılarak SDK bağımsız EXE --verify modu çalıştırıldı: exit 0, 12 kontrol PASS, görünür pencere yok. Paketler outputs klasöründe; ayrıntılı arşiv kanıtı work/archive-verification.json.

## Tarihsel 0.2 arayüz sürümü

Release derlemesi 0 hata/0 uyarı. 156 xUnit testi yeniden geçti. Pencere göstermeyen masaüstü doğrulamasında 20 kontrol geçti: komut aktifliği, belge değişiminde önceki kayıt yolunun temizlenmesi, serileme düğmesinin gerçek belge oluşturması, yerel makine işi/özet doğrulaması ve uygulama veri dizini yalıtımı dahil. 1440x900 ve1024x640 çalışma alanı ile serileme/makine ekranları render edilip görsel incelendi. Serileme ana eylemi varsayılan boyutta sabit alana taşındı; metin/teal zıtlığı düzeltildi. Etkileşimli klavye/OS diyalog kabulü ve tam üretim sertifikasyonu hâlâ ayrı iş.

## 0.3 geliştirme önizlemesi — 31 Ekim 2026
15 uzman ajan dalgalar halinde çalıştı (aynı anda en çok üç alt ajan). Belge oturumu/savepoint, no-op redo, tam hassasiyetli ölçüler, atomik kayıt çakışması, oturum kilitli kurtarma, temel bedenden ardışık serileme, klavyeyle hareket, seçili parça çıktısı, gerçek çıktı önizlemesi, kuyruk yeniden yükleme ve HPGL nokta çakışması koruması eklendi/düzeltildi. İçe aktarım özeti belge değiştirilmeden gösteriliyor; kompakt serileme tablosu okunabilir.
Release: 0 uyarı / 0 hata. Regresyon: 267 geçti / 0 başarısız / 0 atlanan. Gizli masaüstü ve kaynak/render incelemesi PASS; ayrıntılı kontroller docs/implementation-2026-10-31/IMPLEMENTATION_RECORD.json. Gerçek etkileşimli UI, bağımsız OS süreç çökmesi, fiziksel cihaz ve fabrika kabulü doğrulanmadı. Serileme kural taslakları henüz projede saklanmıyor. Bu sürüm satışa hazır değildir. Önceki denetim sonuçları tarihsel kanıttır.
