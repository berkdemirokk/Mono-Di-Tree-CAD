# iPhone → Windows taşınabilir istek fixture'ları

Bu sentetik fixture'lar iPhone v1 JSON anahtarlarını kullanır; gerçek iPhone çıktısı değildir. Referans Windows kaynak incelemesi `codex-stitch-desktop-20261101` HEAD `3f6a4e40cca709bd3a1bfb5e495a698f500d5e86`, `src/TekstilCAD.FileFormats/DesignRequestStorage.cs` ve `DressMeasurementParser.cs`; diğer sürümlerin davranışı ayrıca incelenmelidir. Windows okuyucusunun bu kaynaktaki kodu PowerShell/.NET ile bağımsız çalıştırıldı: üç geçerli dosya kabul edildi, iki geçersiz dosya InvalidDataException ile reddedildi; kabul edilenlerde metin ve referans baytları serialize/deserialize sonrasında korundu (5/5). Test derlemesinde kullanılmayan dosya yazma bağımlılıkları için stub kullanıldı; gerçek Windows uygulaması, UI, iPhone XCTest ve cihaz çalıştırılmadı.

| Dosya | Windows `DesignRequestStorage.Load` beklentisi | Kontrol |
|---|---|---|
| `ios-unicode-photo.tcad-request.json` | Kabul | Tarif `A kesim • İpek 👗\nÇift kat`; iki ölçü satırı; `örnek.png`, `image/png`; Base64 çözülmüş baytlar eksiksiz. |
| `ios-message-ve.tcad-request.json` | Kabul | Tarif ve üç ölçü satırı aynen korunur. Bu depolama kabulü, eski masaüstü ölçü ayrıştırıcısının `ve` ifadesini anladığı anlamına gelmez; belirsizlik uyarısı ayrıca kontrol edilir. |
| `ios-whitespace-notes.tcad-request.json` | Kabul | Ölçü metni tam olarak iki boşluk, sekme, satır sonu, boşluk; boş metne dönüştürülmez. |
| `ios-invalid-duplicate.tcad-request.json` | Red | `prompt` iki kez geçer; açık istek ve referansın değişmediği UI'da ayrıca kontrol edilir. |
| `ios-unsupported-version.tcad-request.json` | Red | Sürüm 2 taşınabilir istek desteklenmez; önceki istek korunur. Bu sürüm, ayrı iPhone yerel taslak sürümüyle karıştırılmamalıdır. |

## Bağımsız masaüstü doğrulama

1. Yalnız test/sentetik veride, doğrulanacak Windows sürümünün kendi `DesignRequestStorage.Load(path)` yöntemine her dosyanın **tam yolunu** verin. Fixture'ları salt okunur açın; `C:\WearCadfe`, `C:\Hpgl`, `C:\Nester`, `C:\Nester1`, `C:\NST` veya WearCAD'e yazmayın. Mevcut gerçek isteklerle denemeyin.
2. Üç kabul örneğinde `Prompt` ve `Measurements` değerlerini tabloyla karakter karakter karşılaştırın; fotoğrafta `Name`, `MediaType` ve çözülen baytları fixture'daki `dataBase64` ile karşılaştırın. Açık model kimliği v1'de ayrı alan değildir; yalnız prompt metni korunur.
3. İki ret örneğinde `InvalidDataException` beklenir. Ayrı bir UI senaryosunda iptal/ret öncesi mevcut istek ve görseli kaydedip işlem sonrasında karşılaştırın; bu fixture tek başına UI durumunu kanıtlamaz.
4. `ios-message-ve` için hem eski kurulumda hem test edilen güncel masaüstü sürümünde `DressMeasurementParser.Review(request.Prompt, request.Measurements)` sonucunu karşılaştırın. Depolama gidiş dönüşü ile ölçülerin yorumlanmasını ayrı sonuçlar olarak raporlayın. `ve` desteği değişebilir; tüm eski/yeni sürümlerde eşit kabul edildiği varsayılmaz.
5. Windows'un `Serialize(Load(path))` çıktısı geçerli JSON olsa da anahtar sırası/kaçışları iPhone'dan farklı olabilir; bayt-bayt JSON eşitliği değil, çözümlenmiş metin ve görsel bayt eşitliği ölçülür. Dosya çıktısı gerekiyorsa yalnız izole test klasöründe yeni dosya oluşturun.

## Sınırlar ve güvenlik

İki incelenen v1 okuyucuda tarif/ölçü 16.384 UTF-16 birimi, referans 8 MiB, toplam JSON 12 MiB sınırındadır. iPhone referans için ImageIO okunabilirliği de arar; Windows depolama okuyucusu imzayı kontrol eder. Dolayısıyla imzası var ama çözümlenemeyen bozuk görsel Windows'ta kabul edilip iPhone'da reddedilebilir; mevcut taslağın korunduğu cihazda sınanmalıdır. Base64 uzunluğu boşlukları temizlemeden sınırlandığından 8 MiB sınırındaki veriye eklenen boşluk her iki uygulamada da reddedilebilir. `ios-whitespace-notes` normal iPhone arayüzünden üretilebilen boşluk-only ek notu test eder; bunun ölçü kabulü olduğu iddia edilmez.

Elde edilen sonuçlar commit, Windows sürümü, iOS/Xcode sürümü ve PASS/FAIL/BLOCKED ile kaydedilmelidir. Fotoğraflar metadata içerebilir; yalnız bu sentetik fotoğraf fixture'ını kullanın. Kayıtlar kalıp üretimi, plotter uygunluğu veya TestFlight onayı değildir.

