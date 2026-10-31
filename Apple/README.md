# Mono Di Tree CAD Apple kontur görüntüleyici — başlangıç

Bu bağımsız SwiftUI başlangıcı iPhone, iPad ve macOS için yerel `.tcad` dosyası seçip parça konturlarını salt okunur gösterir. Eski JSON nesnesi ile 1–5 sürümlü `[sürüm, proje]` zarfındaki `Name`, `Pieces`, `Points`, `Closed` alanlarını okur. Dosyaya yazmaz. Dosya seçiciden iCloud Drive dahil erişilebilir dosyalar açılabilir.

## Xcode'da oluşturma

macOS, Xcode ve XcodeGen gereklidir. Terminalde `Apple` dizinine geçip `xcodegen generate` çalıştırın. Oluşan `TekstilCADViewer.xcodeproj` dosyasını Xcode'da açın; iOS veya macOS hedefini seçin. Uygulama adı Mono Di Tree CAD, bundle kimliği `com.berkdemirokk.monoditreecad` olarak ayarlanmıştır. Gerçek cihazda çalıştırmak için Signing & Capabilities altında bu kimliğe yetkili Apple geliştirme ekibini seçin. XcodeGen kurulumu ve Apple imzalama ayrıca gereklidir.

## Dosya erişimi

macOS uygulamasında App Sandbox açık olduğundan Xcode Signing & Capabilities > App Sandbox > File Access bölümünde User Selected File için Read Only yetkisi (com.apple.security.files.user-selected.read-only) etkinleştirilmelidir. Bu entitlement henüz proje ayarına eklenmedi; Mac üzerinde yapılandırılıp dosya seçiciyle doğrulanmalıdır. iOS ve iPadOS'ta sistem dosya seçicisinin sağladığı güvenlik kapsamlı erişim kullanılır; dosya paylaşımı veya iCloud Documents yeteneği bu temel görüntüleyici için ayrıca etkinleştirilmiş değildir.

## Sınırlar

Bu başlangıç Windows'ta yazıldı; Xcode'da derlenmedi veya iPhone/iPad/Mac üzerinde denenmedi. Üretim sürümü değildir. Proje biçimini masaüstündeki doğrulayıcı kadar kapsamlı doğrulamaz; yalnızca geometriyi önizler. Parça kimliği ve adı, meta veriler, iplik yönü, serileme, pastal, kaynak geçmişi, dışa aktarma, çizim araçları ve cihaz bağlantıları görüntülenmez veya işlenmez. Çizim ekrana sığdırılır; ölçüm ya da üretim için kullanılmamalıdır. Seçim filtresi tüm dosyaları gösterebilir, ancak yalnız `.tcad` uzantısı kabul edilir. UTF-8 JSON hedeflenmiştir; diğer kodlamalar doğrulanmamıştır.

Dağıtım öncesi en azından Xcode derlemesi, gerçek cihaz dosya sağlayıcıları ve masaüstü örnekleriyle uyumluluk, erişilebilirlik ve büyük dosya performansı doğrulanmalıdır.

## TestFlight hazırlığı (taslak)

TestFlight için macOS üzerinde Xcode ve XcodeGen ile projenin üretilmesi, doğru Apple Developer ekibiyle imzalanması ve App Store Connect'te `com.berkdemirokk.monoditreecad` kimliğiyle eşleşen uygulama kaydı gerekir. Arşivleme, yükleme ve TestFlight süreci gerçek Mac ve Apple hesabında doğrulanmalıdır. Otomasyon ileride kurulursa App Store Connect API anahtarı gibi gizli değerler depoya eklenmemeli, GitHub Secrets gibi bir gizli bilgi deposunda tutulmalıdır. Bu belgede otomatik yayın yapılandırılmamıştır.

## GitHub Actions ile derleme kontrolü

`.github/workflows/apple-ci.yml`, Apple kaynakları değiştiğinde veya Actions sekmesinden elle başlatıldığında GitHub'ın macOS çalıştırıcısında XcodeGen projesini üretir ve iOS Simulator ile macOS hedeflerini imzasız derler. İş akışı yalnızca derleme kontrolüdür: uygulamayı çalıştırmaz, gerçek cihazı doğrulamaz ve TestFlight'a yüklemez. Bu yapılandırma henüz GitHub üzerinde çalıştırılarak doğrulanmamıştır. Çalışmanın sonucu ve Xcode sürümü Actions günlüklerinde görülebilir.

TestFlight için ayrıca Apple Developer üyeliği, App Store Connect uygulama kaydı, dağıtım imzası ve arşiv/yükleme gerekir. İmzalama ve yükleme otomasyonu bu iş akışında bulunmaz; kimlik bilgilerini kaynak koduna veya günlüklere koymayın. Başvuru: [GitHub macOS çalıştırıcıları](https://docs.github.com/en/actions/how-tos/manage-runners/github-hosted-runners/customize-runners), [Apple simülatör hedefleri](https://developer.apple.com/documentation/xcode/building-and-running-an-app), [GitHub imzalama belgeleri](https://docs.github.com/en/actions/how-tos/deploy/deploy-to-third-party-platforms/sign-xcode-applications).
