# Taşıma ve WearCAD verileri

31 Ekim 2026 başlangıç incelemesinde `C:\Projects` yoktu; hedefli Documents/Downloads/Desktop aramalarında TekstilCAD kaynak veya yedeği bulunmadı. Bu nedenle `C:\Projects\TekstilCAD` yeni oluşturuldu. WearCAD kurulumu `C:\WearCadfe` altında bulundu.

## Korunan veriler

`C:\WearCadfe`, `C:\Hpgl`, `C:\Nester`, `C:\Nester1`, `C:\NST` geliştirme araçlarının yazma hedefi değildir. Dosyalar değiştirilmez, silinmez, taşınmaz veya yeniden adlandırılmaz. WearCAD üzerinden ayar değiştirme, kaydetme, dışa aktarım veya üretim işi gönderme yapılmaz. Kullanıcı talimatı `AGENTS.md` içinde kalıcıdır.

İncelenen mevcut ASCII DXF: `C:\WearCadfe\wearcad\kalip deneme\sssss\10x10cm_kare_test.dxf`. Dört LINE, 100 × 100 koordinatlı kare; birim başlığı yok. Test ajanı ayrı kopyada 100 × 100 mm, 10.000 mm² alan ve kapalı kontur doğruladı. Özgün dosyanın test öncesi/sonrası SHA256 değeri aynıydı: `74B74B88B93ACB4A53BDD5225926F2A3F0DCCD2010E6295F3068B77AEDF84214`.

`50 50.dxf` BLOCK/POLYLINE yapıları içeren ikinci referanstır. BLOCK tanım geometresinin okunması, INSERT dönüşümleri veya AAMA/ASTM semantiğinin desteklendiği anlamına gelmez. Yerel `.pcs`, `.mak`, `.rul`, `.skt` dönüştürücü yoktur. Korunan uygulama kodu, DLL veya UI varlıkları projeye alınmaz.

## Sürüm 10 iç pens işaretleri

- Seçili kapalı düz kontura `1 kenara 40 mm ağızlı 20 mm derinlikte pens ekle` komutuyla iki iç dikiş kolu eklenir; kumaşın dış kesim konturu değişmez.
- Pens işaretleri `.tcad` v10 olarak saklanır. Önceki sürümler v10 zarfını açmayı reddeder; v10 dosyasını eski sürüme düşürmeyin.
- İç kollar DXF dışa aktarımında kayıp olarak raporlanır. Pastal algoritması pensli parçaları reddeder; dikim, beden ve üretim uygunluğu operatörce ayrıca doğrulanmalıdır.
- Pens işaretleri bu sürümde Windows uygulamasında gösterilir. Gerçek fabrika kalıbı, dikiş provası ve kullanıcı kabulü henüz doğrulanmadı.

## Başka bilgisayara taşıma

Kaynak ZIP'ini yeni boş dizine çıkarın; mevcut kullanıcı projesi üzerine açmayın. `global.json` ile uyumlu .NET SDK kurun, `scripts\Build.ps1 -Test` çalıştırın, ardından Windows UI akışlarını doğrulayın. SDK/önbellek/bin/obj ve gerçek kullanıcı kalıpları kaynak yedeğinin parçası değildir. Derlenmiş taşınabilir uygulama paketi ayrıca oluşturulabilir. Recovery ve simülasyon çıktı yolları taşınan bilgisayarda ayrıca kontrol edilmelidir.
