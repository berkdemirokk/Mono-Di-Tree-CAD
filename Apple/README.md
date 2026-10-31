# Mono Di Tree CAD Apple kontur görüntüleyici — başlangıç

Bu bağımsız SwiftUI başlangıcı iPhone, iPad ve macOS için yerel `.tcad` dosyası seçip parça konturlarını salt okunur gösterir. Eski JSON nesnesi ile 1–5 sürümlü `[sürüm, proje]` zarfındaki `Name`, `Pieces`, `Points`, `Closed` alanlarını okur. Dosyaya yazmaz. Dosya seçiciden iCloud Drive dahil erişilebilir dosyalar açılabilir.

## Xcode'da oluşturma

macOS, Xcode ve XcodeGen gereklidir. Terminalde `Apple` dizinine geçip `xcodegen generate` çalıştırın. Oluşan `TekstilCADViewer.xcodeproj` dosyasını Xcode'da açın; iOS veya macOS hedefini seçin. Uygulama adı Mono Di Tree CAD, bundle kimliği `com.berkdemirokk.monoditreecad` olarak ayarlanmıştır. Gerçek cihazda çalıştırmak için Signing & Capabilities altında bu kimliğe yetkili Apple geliştirme ekibini seçin. XcodeGen kurulumu ve Apple imzalama ayrıca gereklidir.

## Dosya erişimi

iOS ve iPadOS'ta sistem dosya seçicisinin sağladığı güvenlik kapsamlı erişim kullanılır; dosya paylaşımı veya iCloud Documents yeteneği bu temel görüntüleyici için ayrıca etkinleştirilmiş değildir. Mac sürümü bu aşamanın kapsamında değildir.

## Sınırlar

Önceki sürüm GitHub'da iOS simülatörü için imzasız derlendi; bu son değişiklikler ve gerçek cihazlar henüz doğrulanmadı. Üretim sürümü değildir. Proje biçimini masaüstündeki doğrulayıcı kadar kapsamlı doğrulamaz; yalnızca geometriyi önizler. Parça kimliği ve adı, meta veriler, iplik yönü, serileme, pastal, kaynak geçmişi, dışa aktarma, çizim araçları ve cihaz bağlantıları görüntülenmez veya işlenmez. Çizim ekrana sığdırılır; ölçüm ya da üretim için kullanılmamalıdır. Seçim filtresi tüm dosyaları gösterebilir, ancak yalnız `.tcad` uzantısı kabul edilir. UTF-8 JSON hedeflenmiştir; diğer kodlamalar doğrulanmamıştır.

Dağıtım öncesi en azından Xcode derlemesi, gerçek cihaz dosya sağlayıcıları ve masaüstü örnekleriyle uyumluluk, erişilebilirlik ve büyük dosya performansı doğrulanmalıdır.

## TestFlight hazırlığı (taslak)

TestFlight için macOS üzerinde Xcode ve XcodeGen ile projenin üretilmesi, doğru Apple Developer ekibiyle imzalanması ve App Store Connect'te `com.berkdemirokk.monoditreecad` kimliğiyle eşleşen uygulama kaydı gerekir. Arşivleme, yükleme ve TestFlight süreci gerçek Mac ve Apple hesabında doğrulanmalıdır. Otomasyon ileride kurulursa App Store Connect API anahtarı gibi gizli değerler depoya eklenmemeli, GitHub Secrets gibi bir gizli bilgi deposunda tutulmalıdır. Elle tetiklenen yükleme taslağı aşağıda açıklanmıştır; otomatik yayın yapılandırılmamıştır.

## GitHub Actions ile derleme kontrolü

`.github/workflows/apple-ci.yml` yalnızca Actions sekmesinden elle başlatıldığında GitHub'ın macOS çalıştırıcısında XcodeGen projesini üretir, iPhone simülatöründe derler ve temel arayüz testini çalıştırır. Her değişiklikte otomatik çalışmaz. Test uygulamanın açılışını ve dosya seçicisinin iptal edilmesini kontrol eder; dosya içe aktarmayı, gerçek cihazı veya TestFlight yüklemesini doğrulamaz. GitHub kullanım kotasını önce kontrol edin.

TestFlight için ayrıca Apple Developer üyeliği, App Store Connect uygulama kaydı, dağıtım imzası ve arşiv/yükleme gerekir. İmzalama ve yükleme otomasyonu bu iş akışında bulunmaz; kimlik bilgilerini kaynak koduna veya günlüklere koymayın. Başvuru: [GitHub macOS çalıştırıcıları](https://docs.github.com/en/actions/how-tos/manage-runners/github-hosted-runners/customize-runners), [Apple simülatör hedefleri](https://developer.apple.com/documentation/xcode/building-and-running-an-app), [GitHub imzalama belgeleri](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications).

## TestFlight'a elle yükleme iş akışı (doğrulanmamış)

`.github/workflows/apple-testflight.yml` yalnız GitHub Actions > **Upload iOS to TestFlight** > **Run workflow** ile çalışır. Seçilen sürümün kodunu derler, iOS cihaz arşivini mevcut dağıtım sertifikası ve profiliyle imzalar, IPA dışa aktarır ve `altool` ile App Store Connect'e yükler. Bu işlem TestFlight'ta testçilere otomatik dağıtım veya App Store yayını yapmaz. İş akışı gerçek Apple hesabı ve GitHub Actions üzerinde henüz denenmedi; ilk çalışmanın başarılı olması garanti değildir. Yalnız güvendiğiniz, gözden geçirilmiş dalı seçin: o dalın workflow ve kaynak kodu gizli bilgilere erişebilir.

Önce Apple Developer ve App Store Connect'te aynı takıma bağlı, `com.berkdemirokk.monoditreecad` bundle kimlikli iOS uygulama kaydını oluşturun. Özel anahtarını içeren geçerli **Apple Distribution** `.p12` sertifikası ile bu sertifikayı ve uygulama kimliğini kapsayan geçerli **App Store dağıtım provisioning profile** `.mobileprovision` dosyası önceden sağlanmalıdır. Bu iş akışı sertifika veya profil oluşturmaz, yenilemez veya Apple Developer hesabında yetki değişikliği yapmaz. App Store Connect'te yüklemeye yetkili bir **team API key** gerekir; özel `.p8` anahtarı sadece oluşturulduğunda indirilir. Gerekli roller ve mevcut sözleşmeler hesapta doğrulanmalıdır.

GitHub deposunun Settings > Secrets and variables > Actions bölümüne şu secrets'ları ekleyin (dosyaları satır sonu olmayan Base64 metnine çevirin):

- `APPLE_TEAM_ID`: Apple Developer Team ID (10 karakter).
- `APPLE_DISTRIBUTION_P12_BASE64`: özel anahtarı da içeren `.p12` dosyasının Base64 içeriği.
- `APPLE_DISTRIBUTION_P12_PASSWORD`: `.p12` dışa aktarım parolası.
- `APPLE_APPSTORE_PROFILE_BASE64`: App Store dağıtım `.mobileprovision` dosyasının Base64 içeriği.
- `APPLE_KEYCHAIN_PASSWORD`: CI için ayrı, güçlü geçici keychain parolası.
- `ASC_KEY_ID` ve `ASC_ISSUER_ID`: App Store Connect team API key kimliği ve issuer ID.
- `ASC_PRIVATE_KEY_BASE64`: indirilmiş `.p8` dosyasının Base64 içeriği.

macOS'ta örneğin `base64 -i dosya.p12 | tr -d '\n' | pbcopy` ile çıktıyı panoya alıp ilgili secret alanına yapıştırabilirsiniz; `.mobileprovision` ve `.p8` için dosya adını değiştirin. Değerleri depoya, sohbete veya günlüklere yapıştırmayın. Gizli anahtarı güvenli saklayın; sızıntıda Apple'da iptal edip değiştirin. GitHub ortam korumaları ve gerekli onaylar ayrıca yapılandırılabilir.

Elle başlatırken `marketing_version` ve o sürüm için App Store Connect'te henüz kullanılmamış `build_number` girin (ör. `1.0` ve `1`). Yükleme başarıyla bittiğinde Apple derlemeyi işler; TestFlight sekmesindeki durumu, uyumluluk bilgilerini ve testçi atamalarını ayrıca kontrol edin. Başarılı CI sonucu tek başına cihazda çalışma veya TestFlight'ta kullanılabilirlik anlamına gelmez.

Kaynaklar: [Apple: derleme yükleme](https://developer.apple.com/help/app-store-connect/manage-builds/upload-builds/), [Apple: dağıtım ve arşiv](https://developer.apple.com/documentation/xcode/distributing-your-app-for-beta-testing-and-releases), [GitHub: imzalama sertifikası ve profil](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications).

