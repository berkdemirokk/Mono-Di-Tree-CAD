# Mono Di Tree CAD iPhone — TestFlight ve App Store Yayın Kontrol Listesi

## 1. Yapılandırma (Release)

| Ayar | Değer | Not |
|------|-------|-----|
| Bundle ID | `com.berkdemirokk.monoditreecad` | `Apple/project.yml` |
| Gösterilen sürüm | `1.0` | `CFBundleShortVersionString` |
| Derleme numarası | `1` | `CFBundleVersion`, TestFlight'da artırılmalı |
| Dağıtım hedefi | iOS 16.0+ | `deploymentTarget.iOS` |
| Kod imzalama | TestFlight için Manual | `apple-testflight.yml` ile |
| App Sandbox | Etkin | `ENABLE_APP_SANDBOX: YES` |
| Uygulama simgesi | `Assets.xcassets/AppIcon.appiconset/AppIcon.png` | 1024×1024 PNG |

### Görev
- [ ] `project.yml` sürüm ve dosya türü ayarları eklendi (yapıldı)
- [ ] App Store Connect'te aynı bundle ID ile uygulama kaydı oluşturulacak (gerçek Apple hesabı gereklidir)
- [ ] Release derlemesi Xcode'da simülatörde başarıyla oluşturulmalı (CI'de `Build Release` adıyla eklendi)

## 2. Gizlilik ve Veri (Apple inceleme gereklidir)

Uygulama şu veri erişimine ihtiyaç duyar:

| Anahtar | Açıklama (Türkçe) | Kullanım |
|--------|-------------------|----------|
| `NSFileProviderUsageDescription` | Dosyalar uygulaması üzerinden .tcad projelerini açmak ve kaydetmek için dosya erişimine ihtiyaç duyar. | Dosya seçici / paylaşım |

- Uygulama **çevrimdışı çalışır**, ağ erişimi yapmaz, kullanıcı verisini buluta göndermez.
- Kullanıcı verisi yalnız cihazın `ApplicationSupport/MonoDiTreeCAD` dizininde saklanır.
- İçe alınmış fabrika `.tcad` dosyaları değiştirilmez; yalnız yeni çizimler dışa aktarılır.
- Reklam, analiz, kullanıcı takibi veya üçüncü taraf SDK yok.

### Gizlilik politikası (taslak)
Uygulama herhangi kişisel veri toplamaz. Yalnız cihaz yerel depolamasında proje dosyaları saklanır. Veri paylaşımı yalnız kullanıcının "Paylaş veya Dosyalara kaydet" eylemiyle Dosyalar uygulamasına gider. İçerik, üretim veya kesim onayı değildir.

## 3. App Store metadata (Türkçe)

### Uygulama adı
Mono Di Tree CAD

### Alt başlık (30 karakter)
Tekstil kalıp çizim editörü

### Açıklama (örnek)
Mono Di Tree CAD, iPhone için basit, çevrimdışı bir tekstil kalıp çizim aracıdır. Dikdörtgen, çizgi, kubik Bézier eğrisi ve dairesel yay çizebilir; parçaları seçip taşıyabilir, kopyalayabilir, 90° döndürebilir ve silebilir. Ölçüler milimetre cinsinden gösterilir. Çizimler cihazda otomatik taslak olarak kaydedilir ve `.tcad` dosyası olarak Dosyalar uygulamasına aktarılabilir.

Özellikler:
- Yeni proje oluştur ve örnek çizimle dene
- Dikdörtgen, çizgi, çoklu çizgi, Bézier ve yay araçları
- Parça seç, taşı, kopyala, 90° döndür, sil
- Kontrol noktalarını düzenle
- Geri al / ileri al (son 50 değişiklik)
- İki parmakla yakınlaştır, sürükleyerek kaydır
- `.tcad` dosyası dışa aktarım ve Dosyalar uygulaması entegrasyonu
- Türkçe arayüz, VoiceOver ve Dynamic Type desteği

Önemli: Uygulama üretim CAD veya kesim onayı değildir. İçe alınmış fabrika dosyaları salt okunur görüntülenir.

### Anahtar kelimeler
CAD, tekstil, kalıp, çizim, Bézier, yay, mm, 모노, di, tree, çizim aracı

### İçerik hakları / yaş derecesi
4+

## 4. Ekran görüntüsü planı

En az 3, en fazla 10 ekran görüntüsü (iPhone 6.5 inç):

| # | Ekran | Amacı |
|---|-------|-------|
| 1 | Ana ekran: "Yeni proje oluştur", "Örnekle dene", "Dosya aç" | Başlangıç |
| 2 | Çizim sekmesi: tuval, şekil ekle düğmeleri (dikdörtgen, çizgi, Bézier, yay) | Çizim araçları |
| 3 | Düzenle sekmesi: parça seçici, mm ölçü girişleri, taşı/döndür/sil | Düzenleme |
| 4 | Proje sekmesi: kaydet, farklı kopya, dışa aktar, paylaş | Dosya işlemleri |
| 5 | Yakınlaştırma ve pan tuvalde | Kamera kontrolleri |

## 5. Apple imzalama ve provisioning denetimi

TestFlight workflow'u (`.github/workflows/apple-testflight.yml`) şu gerektirir:

| Gereklilik | Durum |
|------------|-------|
| Apple Developer üyelik | Kullanıcı tarafından sağlanmalı |
| App Store Connect uygulama kaydı | `com.berkdemirokk.monoditreecad` |
| Dağıtım `.p12` sertifikası | GitHub Secret `APPLE_DISTRIBUTION_P12_BASE64` |
| App Store provisioning `.mobileprovision` | GitHub Secret `APPLE_APPSTORE_PROFILE_BASE64` |
| App Store Connect API anahtarı | GitHub Secrets `ASC_KEY_ID`, `ASC_ISSUER_ID`, `ASC_PRIVATE_KEY_BASE64` |
| Keychain parolası | `APPLE_KEYCHAIN_SECRET` |

### Önemli
- Gerçek sertifika, Apple hesabı veya kullanıcı onayı **olmadan TestFlight/App Store'a yayın gönderilmez**.
- Gizli anahtarlar **yalnız GitHub Secrets**'ta saklanır; kaynak koda, sohbete veya günlüklere asla eklenmez.
- `apple-testflight.yml` workflow'u `workflow_dispatch` ile el ile tetiklenir; otomatik yayın yok.

## 6. Gerçek cihaz test kontrol listesi

Simülatör testleri gerçek cihaz testi olarak gösterilmez. Aşağıdaki testler gerçek iPhone'da yapılmalı:

| # | Test | Kabul kriteri |
|---|------|---------------|
| 1 | Uygulama ilk açılış | Ana ekran iki seçenekle net görünür |
| 2 | Yeni proje → dikdörtgen ekle → önizleme | Parça görünür, mm ölçü doğrudur |
| 3 | Bézier ve yay ekle | Eğri çizilir, kontrol noktası düzenlenebilir |
| 4 | Parça seç, taşı, kopyala, döndür, sil | Tüm işlem çalışır, geri al/ileri al tutarlı |
| 5 | İki parmakla zoom, sürükle ile pan | Görünüm değişir, sıfırla düğmesi çalışır |
| 6 | Proje kaydet, uygulama kapat, yeniden aç | Taslak geri yüklenir, veri kaybı yok |
| 7 | `.tcad` dışa aktar, Dosyalara kaydet | Dosya oluşturulur, Windows ile açılabilir |
| 8 | Bozuk `.tcad` dosyası | Hata iletisi gösterilir, çökme yok |
| 9 | Büyük dosya (>64 MB) | Reddedilir, önceki dosya korunur |
| 10 | VoiceOver ile gezinti | Tüm düğmeler erişilebilir |
| 11 | Dynamic Type (büyük yazı) | Arayüz bozulmadan genişler |
| 12 | Türkçe sistem dili | Tüm metinler Türkçe görünür |
| 13 | Apple Pencil (varsa) | Dokunmatik kontrol zorunlu kalır; pencil ekstra |

## 7. App Store inceleme kuralları son kontrolü

| Kural | Durum |
|-------|-------|
| 2.1 - Performans: uygulama çökmeyecek | Simülatör testleri geçmeli |
| 5.1.1 - Veri saklama: yalnız yerel | Onay |
| 5.1.2 - Veri transferi: ağ yok | Onay |
| 3.1.1 - İş modeli: ücretsiz | Onay |
| 2.3.1 - Gereksiz: tek işlevi, üretim onayı yok | Açıklamada netleştirildi |
| 4.2 - Tasarım: erişilebilirlik | VoiceOver + Dynamic Type |

## 8. Bilinen sınırlamalar (V1.0)

- Gerçek cihaz testleri henüz yapılmamıştır (yalnız iOS simülatörü).
- Windows CAD dosyaları (tam şema) salt okunur; yalnız basit kontur kopyası düzenlenebilir.
- Apple Pencil desteği isteğe bağlıdır; dokunmatik kontrol zorunludur.
- TestFlight/App Store yayın gerçek Apple hesabı, sertifika ve provisioning ile kullanıcı onayı sonrası yapılacaktır.
- TestFlight workflow'u henüz gerçek cihazda denenmedi.
