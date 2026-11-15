# Önizleme yayını kontrol listesi

Bu belge yalnız **Windows x64 geliştirme önizlemesinin** GitHub'da ön sürüm (pre-release) olarak yayımlanmasını anlatır. Ticari yayın koşulları `docs/PRODUCT_RELEASE_PLAN.md` içindedir; bu liste onların yerine geçmez.

## Önizleme yayını nasıl çıkar

1. `src/TekstilCAD.Desktop/TekstilCAD.Desktop.csproj` içindeki `Version` değerini belirleyin (ör. `0.4.0`).
2. Aşağıdaki kapıların tümü `main` üzerinde geçmiş olmalı.
3. `main` üzerindeki bir commit'e sürümle eşleşen etiketi gönderin: `v0.4.0` ya da `v0.4.0-preview.1`. Etiket sürümle eşleşmezse ya da etiketli commit `main` üzerinde değilse iş akışı durur.
4. `.github/workflows/windows-release.yml` derler, birim testlerini ve penceresiz masaüstü doğrulamasını çalıştırır, `scripts/Package.ps1` ile kaynak ZIP, Windows x64 ZIP ve SHA-256 manifestini üretir. Doğrulama `PASS` değilse paket üretilmez. Yayın ayrı bir işte, yalnız etiket gönderiminde yapılır; depoya yazma yetkisi yalnız o iştedir.

Etiket göndermeden denemek için iş akışını elle başlatın (`workflow_dispatch`): paketler yalnız iş akışı çıktısı olarak yüklenir. Elle başlatma, hangi dal ya da etiket üzerinde çalıştırılırsa çalıştırılsın yayın oluşturmaz.

Yerelde paket: `.\scripts\Package.ps1` (çıktı `work\packages`). Windows PowerShell 5.1 ve PowerShell 7 ile çalışır; çalışma kopyası temiz olmalıdır.

## Yayın öncesi kapılar

| Kapı | Nasıl denetlenir |
| --- | --- |
| Derleme 0 hata | `dotnet build TekstilCAD.slnx -c Release` |
| Birim testleri | `dotnet test tests/TekstilCAD.Tests/TekstilCAD.Tests.csproj -c Release --no-build` |
| Penceresiz masaüstü doğrulaması `PASS` | `TekstilCAD.Studio.exe --verify <depo>\work\<ad>.json` |
| Paket içeriği | `scripts/VerifyArchive.ps1` (Package.ps1 çağırır) |
| Paketten çalıştırma | ZIP'i ayrı klasöre açıp `--verify` ve elle açılış |

Masaüstü doğrulaması CI'da çalışmaz; yayın öncesi elle çalıştırılmalıdır. Depo yolu uzunsa (yaklaşık 190 karakterden fazla) sabit bağlantı (hard link) kontrolü Windows yol sınırı yüzünden başarısız olur; kısa bir yolda çalıştırın.

## 15 Kasım 2026 durumu (`f440bd8` üzerine)

- Derleme: 0 hata, 36 uyarı. Birim testleri: 3.016 başarılı, 0 başarısız, 1 atlanan.
- Masaüstü doğrulaması `main` üzerinde **FAIL**: yay aracının üç kontrolü (`Arc preserves the chosen start coordinate exactly`, `Arc preserves an exactly equal-radius end coordinate`, `Arc start remains attached through undo and redo`). Yay aracı parametrik eğriye geçtikten sonra başlangıç noktası tıklanan koordinatla bit düzeyinde aynı kalmıyor ve nokta sayısı toleransa göre değişiyor. Doğrulama ilk hatada durur: bu daldaki normal derleme yay kontrolüne kadar 623 kontrolü geçti. Kalan kontroller, yalnız tanılama amacıyla hatada durmayan geçici bir derlemeyle çalıştırıldı: `main` üzerinde 2.468 kontrolden, bu dalın bir önceki düzeninde (Hakkında düğmesi başlığın sağındayken) 2.484 kontrolden bu üçü dışında hepsi geçti. Düğme marka altına taşındıktan sonraki tanılama derlemesini bu bilgisayardaki Akıllı Uygulama Denetimi engelledi (`0x800711C7`); son düzen için tam çalıştırma kanıtı yoktur. Geçici derleme depoya girmedi.
- Bu kapı kapanmadan önizleme yayımlanmamalıdır: ya yay aracı kesin başlangıç/bitiş noktasını yeniden korumalı ya da beklenti bilinçli bir kararla güncellenmelidir.

## Önizleme için açık kararlar

- **Ürün lisansı:** depoda `LICENSE` yok ve depo herkese açık. İndirilebilir paket yayımlanmadan önce kullanım koşulu belirlenmeli.
- **Kod imzası:** paketler imzasızdır. Windows SmartScreen uyarı verir; Akıllı Uygulama Denetimi (Smart App Control) açık bilgisayarlarda imzasız dosyalar hiç çalışmayabilir. İmza sertifikası alınana kadar bu, sürüm notlarında açıkça yazılmalıdır.
- **Kurulum ve güncelleme:** yalnız ZIP vardır; yükleyici, kaldırma ve güncelleme yoktur.
- **Gerçek kullanıcı kabulü:** görünür pencereyle fare/klavye kabulü, DPI/çoklu ekran ve ekran okuyucu denemesi yapılmadı.
