# sparrow ile pastal (deneysel, harici)

`TekstilCAD.Nesting.SparrowNesting`, açık kaynak **sparrow** pastal sezgiselini ayrı bir süreç olarak çalıştırır ve sonucunu kendi denetiminden geçirir. sparrow depoya ve dağıtıma dahil değildir.

## Neden

sparrow'un makalesindeki beklenen yoğunluklar (EJOR, arXiv:2509.13329, Tablo 2): ALBANO %89,47, DAGLI %89,26, MAO %86,14, MARQUES %90,93. Bunlar makaledeki uzun süreli koşulardır; bizim iş ölçülerimizde ve sürelerimizde ayrıca ölçülmelidir.

## Akış

1. `BuildInstanceJson` isteği doğrular ve jagua-rs SPP JSON'unu yazar. Her `PatternPiece` bir kesim kopyasıdır (`demand = 1`).
2. `RunAsync(exe, istek)` geçici klasörde `sparrow -i input.json -t <sn> [-s <tohum>]` çalıştırır, `output/final_tekstilcad.json` okur ve klasörü siler. İptalde süreç ağacı sonlandırılır.
3. `ReadSolution` sonucu bizim koordinatlarımıza çevirir ve bağımsız denetler: her parça tam bir kez, yalnız izinli açı (0° ya da izinliyse 180°), aynalama yok, kumaş/kenar payı sınırı ve parça aralığı (Clipper2, 0,001 mm ızgara, 0,01 mm tolerans). Denetimi geçmeyen sonuç `SparrowResultRejectedException` ile reddedilir; kısmi sonuç verilmez.

## Koordinat eşlemesi

sparrow sabit yükseklikli (Y) ve değişken genişlikli (X) şerit doldurur; bizde X kumaş eni, Y boy. Eşleme aynalama değil +90° dönüşümdür: parça yerel noktası `(u, v) = (y, -x)` olarak gider, yerleşmiş nokta `(x, y) = (En - KenarPayı - v, u)` olarak döner. Şerit yüksekliği `En - 2 × KenarPayı`. Dönüş açısı iki çerçevede aynı anlama gelir; 0° iplik yönünü korur.

## Sınırlar / doğrulanmadı

- Otomatik testler sparrow'u çalıştırmaz; yalnız JSON üretimi, eşleme ve denetim için elle yazılmış sparrow çıktılarını kullanır. `RunAsync` gerçek ikiliyle yalnız aşağıdaki duman testinde denendi (aralık 0, kenar payı 0). Sıfırdan büyük aralık ve kenar payı gerçek ikiliyle denenmedi.
- Adaptör yalnız 0° ve 180° gönderir. MAO ve MARQUES örnekleri 90°/270°'ye de izin verdiğinden bu ikisinde makale değerleriyle doğrudan kıyas yapılamaz.
- sparrow'un parça aralığını şerit kenarına da uygulayıp uygulamadığı doğrulanmadı. Şerit kenar payı kadar daraltıldığı için sonuç her durumda sınır içinde kalır; aralık kenarda da uygulanıyorsa kenarda en fazla `aralık/2` fazladan boşluk oluşur.
- Arayüz bağlantısı, ilerleme göstergesi, iplik yönü/çentik/serileme aktarımı yok. Sonuç üretim onayı değildir.

## sparrow'u Windows'ta derleme

Rust 1.86+ gerekir.

```
git clone https://github.com/JeroenGar/sparrow
cd sparrow
cargo build --release
```

İkili: `target\release\sparrow.exe`. İlk deneme: `sparrow.exe -i data\input\albano.json -t 60 -s 1`.

Visual Studio Build Tools 2022'nin C++ araçları (MSVC + Windows SDK) gerekir. Derleme `LNK1104 ... build_script_build.exe dosyası açılamıyor` ile bitiyorsa sebep büyük olasılıkla eksik linker değil, 260 karakteri aşan çıktı yoludur (`link.exe` uzun yolları açamaz). sparrow'u kısa bir yola klonlayın ya da hedef dizini kısa bir yola alın:

```
$env:CARGO_TARGET_DIR = 'C:\st'
cargo build --release
```

Bu durumda ikili `C:\st\release\sparrow.exe` olur.

## Duman testi (2026-11-15, Windows 11)

Gerçek ikiliyle `RunAsync` ilk kez çalıştırıldı: sparrow 0.3.0 (`67bfaf8`), rustc 1.100.0, MSVC 14.44. sparrow'un `data/input` örnekleri `SparrowNestingRequest`'e çevrildi (her talep kopyası ayrı parça, aralık 0, kenar payı 0, 180° izinli) ve sonuç `ReadSolution` denetiminden geçirildi. Çeviren küçük konsol programı depoya dahil değildir. Her örnek 60 sn, tohumsuz, tek koşu; değerler tekrarlanabilir bir ölçüm değildir.

| Örnek | Denetim | Verim | Makale | Parça | Not |
|---|---|---|---|---|---|
| ALBANO | kabul | %88,02 | %89,47 | 24 | |
| DAGLI | kabul | %87,42 | %89,26 | 30 | |
| MAO | kabul | %81,50 | %86,14 | 20 | yalnız 0/180°; örnek 90/270°'ye de izin verir |
| MARQUES | kabul | %87,69 | %90,93 | 24 | yalnız 0/180°; örnek 90/270°'ye de izin verir |

## Sıradaki adımlar

1. Sıfırdan büyük aralık ve kenar payıyla, gerçek kalıp ölçülerinde `RunAsync` denemesi.
2. ALBANO/DAGLI/MAO/MARQUES üzerinde mevcut yerleştirici ile sparrow'u 60 sn ve 600 sn'de karşılaştıran ölçüm betiği.
3. Pastal penceresine "derin optimizasyon (sparrow)" seçeneği; sonuç yalnız onaydan sonra projeye uygulanır.
