# iPhone Kalıp Atölyesi ve Görüntüleyici Test Rehberi

Bu belge, `Apple/` altındaki Mono Di Tree CAD iOS/macOS kontur görüntüleyicinin yerel olarak nasıl derleneceğini ve test edileceğini açıklar.

## Ön koşullar

- macOS 15+ (GitHub Actions `macos-15` çalıştırıcısı kullanılır)
- Xcode 15+
- XcodeGen (`brew install xcodegen`)
- iPhone simülatörü (Xcode ile birlikte gelir)

## Yerel derleme ve test

### 1. XcodeGen ile proje dosyasını oluşturun

`Apple` dizinine geçin:

```bash
cd Apple
xcodegen generate
```

Bu komut `TekstilCADViewer.xcodeproj` dosyasını üretir.

### 2. Xcode'da açın

```bash
open TekstilCADViewer.xcodeproj
```

- **Scheme**: `TekstilCADViewer`
- **Hedef**: iPhone simülatörü (ör. iPhone 16) veya macOS
- **İmza**: `Automatic` (yerel geliştirme için) veya elle dağıtım sertifikası (TestFlight için)

### 3. Komut satırından derleme ve test

iPhone simülatöründe UI testleri çalıştırmak için:

```bash
cd Apple
xcodegen generate

DEVICE_ID=$(xcrun simctl list devices available | grep "iPhone.*Booted" | head -1 | sed 's/.*(\(.*\))/\1/')

xcodebuild -project TekstilCADViewer.xcodeproj \
  -scheme TekstilCADViewerCI \
  -configuration Debug \
  -destination "platform=iOS Simulator,id=$DEVICE_ID" \
  CODE_SIGNING_ALLOWED=NO \
  test
```

CI'de kullanılan spec dosyası:

```bash
cat project.yml Tests/ci-overlay.yml > project.ci.yml
xcodegen generate --spec project.ci.yml
```

### 4. Test fixture'ları

`Apple/Tests/Fixtures/` altındaki `.tcad` dosyaları UI testlerinde kullanılır:

| Fixture | Açıklama |
|---------|----------|
| `selection.tcad` | 2 parça, açık/kapalı kontur, iplik yönü, metadata |
| `empty-project.tcad` | Boş parça listesi |
| `minimal-piece.tcad` | 2 nokta, açık kontur (minimum geçerli) |
| `turkish-utf8.tcad` | Türkçe karakterler, metadata, iplik yönü |
| `no-grainline.tcad` | Iplik yönü yok ("Belirtilmemiş") |
| `open-contour.tcad` | Açık kontur (Closed: false) |
| `large-coordinates.tcad` | Koordinatlar ~1e8 sınırı altında |
| `invalid-json.tcad` | Geçersiz JSON (hata beklenir) |
| `missing-name.tcad` | Proje adı yok (hata beklenir) |
| `missing-pieces.tcad` | Parça listesi yok (hata beklenir) |
| `missing-closed.tcad` | Kapalı alan alanı yok (hata beklenir) |
| `nan-coordinate.tcad` | NaN koordinat (hata beklenir) |
| `many-pieces.tcad` | Çok parçalı proje |
| `mobile-simple.tcad` | iPhone tarafından üretilen yalın çizim; güvenli düzenlenebilir kopya testi |

## Test sınıfları

| Sınıf | Test metotları |
|-------|---------------|
| `ViewerUITests` | Başlangıç, örnek çizim, taslak kurtarma, dosya seçici, bozuk alan ve arama testleri |
| `FixtureLoadingTests` | Sürüm zarfı, veri sınırları, parça seçimi, yalın iPhone çizimini kopya açma ve metaverili fabrika dosyası reddi |

Güncel test ve fixture sayıları için `Apple/Tests/` kodunu temel alın; eski sabit sayıların tekrar edilmesi doğru değildir.

## TestFlight dağıtımı

`.github/workflows/apple-testflight.yml` workflow'u el ile tetiklenir. Gerekli secrets:

- `APPLE_TEAM_ID` — Apple Developer Team ID
- `APPLE_DISTRIBUTION_P12_BASE64` — Dağıtım sertifikası (Base64)
- `APPLE_DISTRIBUTION_P12_PASSWORD` — Sertifika şifresi
- `APPLE_APPSTORE_PROFILE_BASE64` — App Store provisioning profili (Base64)
- `APPLE_KEYCHAIN_PASSWORD` — CI keychain şifresi
- `ASC_KEY_ID` — App Store Connect API key ID
- `ASC_ISSUER_ID` — App Store Connect API issuer ID
- `ASC_PRIVATE_KEY_BASE64` — API özel anahtarı (Base64)

Workflow girdileri:

- `marketing_version` — Örn. `1.0`
- `build_number` — Örn. `1`

## Sınırlar

- **İçe alınan fabrika projesi** salt okunur gösterilir. Ayrı başlangıç atölyesi yeni kontur oluşturur ve yalnız yalın iPhone `.tcad` dosyalarının düzenlenebilir kopyasını açabilir. Serileme, pastal ve üretim çıktıları desteklenmez.
- Maksimum dosya boyutu: 64 MB
- Maksimum parça sayısı: 10.000
- Maksimum nokta sayısı (parça başına): 10,000
- Maksimum toplam nokta sayısı: 200,000
- Sadece `.tcad` uzantılı dosyalar kabul edilir
- Gerçek cihaz testleri henüz yapılmamıştır (yalnızca iOS simülatörü)

## GitHub Actions

- `apple-ci.yml` — Elle veya Apple dosyalarını etkileyen ana dal PR'larında tetiklenir, iPhone simülatöründe derleme + UI testi; aynı PR'daki eski denemeler iptal edilir
- `apple-testflight.yml` — Sadece elle tetiklenir, IPA derleme + App Store Connect yükleme

Kimlik bilgilerini **kaynak koda, sohbete veya günlüklere asla eklemeyin**; GitHub Secrets aracılığıyla saklayın.


## Yeni başlayanlar için kabul senaryosu (manuel)

1. Uygulamayı ilk kez aç: iki başlangıç seçeneği net görünmeli; mevcut dosya görüntüleyici çalışmalı.
2. **Yeni proje oluştur** > **Dikdörtgen ekle**: önizlemede bir parça görünmeli.
3. Parçanın adını ve en/boyunu (`100`/`150` mm), X/Y konumunu değiştirip uygula; önizleme güncellenmeli.
4. Parçayı sağa taşı, 90° döndür, kopyala, sil; her işlemde geri al/ileri al doğru sonucu vermeli.
5. **Dosyayı hazırla** > **Paylaş veya Dosyalara kaydet** ile gerçekten `.tcad` dosyası dışa aktar. Windows masaüstünde açıp kimlik, ölçü ve geometriyi bağımsız doğrula.
6. **Örnekle dene** seçeneği iki örnek parçayla açılmalı; yeni kullanıcı önceki CAD bilgisi olmadan işlemleri bulabilmeli.
7. Boş projede dışa aktar reddedilmeli; 0/negatif/büyük, bozuk veya sonsuz sayılar kabul edilmemeli.
8. VoiceOver, Dynamic Type, küçük ekran, iPhone/iPad, Türkçe/İngilizce sistem dili, Dosyalar ve iCloud sağlayıcıları ile gerçek cihaz kabulü yapılmalı.

`ViewerUITests.swift` başlangıç ve ilk düzenleme yolunu otomatik kontrol eder; testlerin varlığı fiziksel cihaz doğrulaması anlamına gelmez.

## Dokunmatik ve çevrimdışı taslak kabulü

1. Yeni proje aç, dikdörtgen ekle. Dikdörtgenin içine dokun, seçimin değiştiğini doğrula.
2. Parmağınla seçili parçayı sürükle; çizim hareket esnasında önizlenmeli, bırakınca yeni konum kalıcı olmalı. 5 mm'ye hizala seçeneğinin açık/kapalı sonuçlarını karşılaştır.
3. Sürüklemeden sonra Geri Al ve İleri Al orijinal ve yeni noktaları aynen geri getirmeli.
4. Editörü kapat; **Son çizime devam et** ile aynı parçalar geri gelmeli. Uygulamayı kapatıp tekrar açıldığında da yerel taslak geri yüklenmeli.
5. Kayıtlı taslak varken Yeni Proje/Örnekle Dene eski taslağın değiştirileceğini sormalı. İptal edildiğinde dosya korunmalı.
6. Aşırı büyüklükte koordinat, sıfır uzunluk, bozuk taslak ve disk alanı hatalarında üretim dosyaları etkilenmemeli; kullanıcıya açık hata gösterilmeli.
7. Büyük parça yoğunluğunda UI takılması, erişilebilirlik ve uygulama yeniden başlatılması test edilmeli; taslak 250 parça, 20.000 nokta ve 2 MB sınırına sahiptir.
8. Bu özellik ücretli dış servis veya gizli API anahtarı gerektirmemeli.

UI simülatör testleri lokal taslak test verisini özel bir debug argümanıyla sıfırlar; bu argüman yayın uygulamasında çalışmaz.

## Yeni test: iPhone çizim kopyasını yeniden düzenleme

1. `mobile-simple.tcad` önizlemesinde **Kopyasını düzenle** düğmesi görünmeli. Başka bir yerel taslak yoksa direkt açılmalı; varsa mevcut taslağı değiştirmeden önce onay sorulmalı.
2. Açılan düzenleyicide kaynak çizimdeki iki parçanın adları, açık/kapalı bilgisi, UUID ve mm koordinatları korunmalı. Kaydedilen kopya Windows C# dosya okuyucusuyla tekrar yüklenmeli.
3. `mobile-simple.tcad` içinde `Metadata`, `Notches`, `Layers`, sürüm zarfı, dolu `GradingRules` veya bilinmeyen alan varsa **Kopyasını düzenle** görünmemeli. Dosya kontur önizlemesine uygun olsa bile metaveriler düzenleme yoluyla kaybolmamalı.
4. Düzenleyici kapatılınca yerel otomatik taslak **Son çizime devam et** ile geri açılmalı. Kaynak `.tcad` dosyasının baytları değişmemeli.
5. `CADSketchImport.swift` ayrık, yalnız basit format kabul eden bir geçittir; üretim için yetkili DXF/veri korunumu doğrulayıcısının yerini almaz.

## Taslak kayıt çakışması

- Otomatik kayıt artık yalnız proje UUID'sini değil, düzenleme başlamadan önce bilinen son disk içeriğini de kontrol eder. Aynı UUID'li eski bir ekranın yaptığı kaydetme girişimi reddedilir; daha yeni taslak sessizce değiştirilmez.
- Geri al/ileri al ve normal düzenleme aynı kontrolü kullanır. Çakışmada hata gösterilir, çalışma alanındaki değişiklikler kullanıcı tarafından dışa aktarılabilir.
- Bir projenin dosyası başka bir ekran tarafından değiştirilmişse önce projeyi yeniden açın; eski ekran üzerinden kaydetmeyi zorlamayın.
- Bu **iyimser sürüm kontrolü**, farklı süreçlerin aynı anda okuma/yazma yaptığı son derece dar bir yarış aralığı için işletim sistemi düzeyinde kilit sağlamaz. Çoklu süreç atomik yazma/kilit sözleşmesi ayrıca doğrulanmalıdır.
- `CADStorageTests` içinde aynı kimlikli eski sürüm ve beklenmeyen/silinmiş dosya regresyonları bulunur.

## Son projeler için eşzamanlı kayıt güvenliği

- **Projeyi kaydet**, açıldığı anda doğrulanmış kütüphane sürümüyle karşılaştırarak kaydeder. Arada başka bir oturum tarafından değiştirilmiş veya silinmiş dosya sessizce ezilmez.
- Çakışma uyarısı görülürse **Farklı kopya kaydet** yeni proje UUID'siyle ikinci bir bağımsız kayıt üretir. Mevcut kütüphane dosyası korunur; çalışma alanı da kapanmaz.
- **Farklı kopya kaydet** mevcut taslak veya başka bir proje üzerine yazmaz. Bir kopyanın oluştuğunu ana ekrandaki **Son projeler** listesinde doğrulayın.
- `CADStorageTests`, aynı UUID'ye sahip eski kayıt sürümü, yanlış UUID ve idempotent yeniden kaydetme senaryolarını çalıştırır.
- Taslak ve kütüphane kayıtlarında, uygulamanın kendi yazıcıları arasında `.json.lock` yan dosyasıyla işlem/kontrol/kayıt bir dosya kilidi altında tutulur; mevcut taslağın arşivlenmesi ve silinmesi de aynı kilitle yapılır. Yan kilit dosyaları başka açık süreçlerin kilidini geçersiz kılmamak için silinmez. Eşzamanlı eski-sürüm yazımlarını reddeden bir regresyon testi bulunur. Kilidi kullanmayan harici yazıcılar, farklı dosya alias'ları ve fiziksel cihaz testi bu garantinin dışındadır.

## Sürüm 9 parametrik WPF çizimleri

- Windows WPF Bézier/Yay aracı analitik kontrol noktaları ve örneklenmiş kontur önizlemesini ayrı tutar. Parametrik geometri içeren dosya `.tcad` v9 olarak kaydedilir. Önceki sürümlerin noktaları varsayılan olarak değiştirilmez.
- iPhone sürüm 9 için **yalnız kontur önizlemesi** yapar. Kaynak segment yapısında beklenen türler ve zorunlu alanlar denetlenir; kontrol noktaları iPhone üzerinden düzenlenmez ve kayıp yaşanmaması için **Kopyasını düzenle** görünmez.
- Önizleme, v9 matematiksel segmentleri yeniden örnekleyip Windows dosyasıyla geometrik olarak doğrulamaz. Kaynağı doğrulamak ve kontrol noktalarını düzenlemek için Windows CAD uygulamasını kullanın.
- Bozuk/eksik v9 `Curves` alanı, bilinmeyen segment türleri ve yanlış sürüm zarfı iPhone görüntüleyicide reddedilir. Dosya değiştirilmez.
- Gerçek iPhone üzerinde Dosyalar sağlayıcısı, ölçek/segment doğruluğu, erişilebilirlik ve kullanıcı kabulü ayrıca test edilmelidir.
