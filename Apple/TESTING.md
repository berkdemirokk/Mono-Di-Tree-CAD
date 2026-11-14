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

## Test sınıfları

| Sınıf | Test metotları |
|-------|---------------|
| `ViewerUITests` | 3 test (başlatma/dosya seçici iptali, bozuk alanlar, arama/seçim) |
| `FixtureLoadingTests` | 7 test (fixture yükleme, hata durumları) |

Toplam: **10 UI testi**, **7 fixture dosyası**.

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

- Uygulama **salt-okunur** önizleme sürümüdür; düzenleme, serileme, pastal ve üretim çıktıları desteklenmez
- Maksimum dosya boyutu: 64 MB
- Maksimum parça sayısı: 10.000
- Maksimum nokta sayısı (parça başına): 10,000
- Maksimum toplam nokta sayısı: 200,000
- Sadece `.tcad` uzantılı dosyalar kabul edilir
- Gerçek cihaz testleri henüz yapılmamıştır (yalnızca iOS simülatörü)

## GitHub Actions

- `apple-ci.yml` — Sadece elle tetiklenir (`workflow_dispatch`), iPhone simülatöründe derleme + UI testi
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
