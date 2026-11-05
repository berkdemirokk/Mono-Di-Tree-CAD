# Mono Di Tree CAD Apple kontur görüntüleyici — başlangıç

Model adı önerisi tarif ve ölçü mesajında sınırlı sayıdaki açık Türkçe model adını arar; belirsizlik göstergeleri bulunursa öneri sunmaz ve kullanıcı onayı olmadan model seçmez. Bu kurallar tüm doğal dildeki belirsizlikleri veya olumsuzlukları tanımaz; kullanıcı öneriyi kendi ifadesiyle karşılaştırmalıdır. Kalıp üretmez. Bu yeni akış Xcode, simülatör ve gerçek cihazda henüz doğrulanmadı.

Tasarım isteği V2 deneysel olarak seçilebilir; V1 kaydetme seçeneği korunur. V2 ayrı model kimliği saklar, ancak bu iPhone değişiklikleri Xcode, simülatör veya gerçek cihazda henüz denenmedi. Windows ile iki yönlü dosya ve görsel uyumluluğu doğrulanmış değildir; eski uygulama sürümleri V2 açamayabilir. V1'e aktarım model kimliğini ayrı korumaz. Dosya seçici mevcut hedefin üzerine yazmayı uygulama düzeyinde engellemez; yeni bir ad seçin.

Bu bağımsız SwiftUI başlangıcı iPhone, iPad ve macOS için yerel `.tcad` dosyası seçip parça konturlarını gösterir. Eski JSON nesnesi ile 1–5 sürümlü `[sürüm, proje]` zarfındaki `Name`, `Pieces`, `Points`, `Closed` alanlarını okur. Modeli telefonun kütüphanesine veya adı değiştirilmiş bir kopya olarak kaydedebilir. Dosya seçiciden iCloud Drive dahil erişilebilir dosyalar açılabilir.

## Xcode'da oluşturma

macOS, Xcode ve XcodeGen gereklidir. Terminalde `Apple` dizinine geçip `xcodegen generate` çalıştırın. Oluşan `TekstilCADViewer.xcodeproj` dosyasını Xcode'da açın; iOS veya macOS hedefini seçin. Uygulama adı Mono Di Tree CAD, bundle kimliği `com.berkdemirokk.monoditreecad` olarak ayarlanmıştır. Gerçek cihazda çalıştırmak için Signing & Capabilities altında bu kimliğe yetkili Apple geliştirme ekibini seçin. XcodeGen kurulumu ve Apple imzalama ayrıca gereklidir.

## Dosya erişimi

iOS ve iPadOS'ta sistem dosya seçicisinin sağladığı güvenlik kapsamlı erişim kullanılır; dosya paylaşımı veya iCloud Documents yeteneği bu temel görüntüleyici için ayrıca etkinleştirilmiş değildir. Mac sürümü bu aşamanın kapsamında değildir.

**Açık modeli dışa aktar** deneysel bir akıştır: sistemin dosya kaydetme penceresini açar ve açık proje yüklenirken okunan tam baytları kullanır. Hedefi ve varsa mevcut dosyayı değiştirme kararını kullanıcı sistem penceresinde seçer; iptal açık modeli ve uygulama kütüphanesini değiştirmez. Başarı mesajı yalnız sistem kaydetme işlemini başarılı bildirdiğinde gösterilir. Bu yeni akış henüz Xcode'da derlenmedi veya gerçek cihaz ve dosya sağlayıcılarıyla denenmedi; özellikle `.tcad` türü ve uzantısı, belge ömrü, dosya adı, iptal, hedef değiştirme ve büyük dosya belleği doğrulanmalıdır.

iPhone ana ekranı kaydırılabilir; önizleme alanındaki sürükleme hareketiyle sayfa kaydırmasının etkileşimi henüz simülatör veya cihazda doğrulanmadı.

Kontur önizlemesinde Y koordinatı masaüstü tuvaliyle aynı yönde aşağı doğru artar. Bu düzeltme ve çentik işaretlerinin görünümü henüz Xcode veya gerçek cihazda doğrulanmadı.

Sürüm 6 önizlemesi için hem kaynak dosya hem doğrulamada kullanılan geçici UTF-8 metin en fazla 8 MB olabilir. BOM'lu UTF-16/UTF-32 girdiler için geçici dönüşüm yapılır; saklama ve dışa aktarma özgün baytları kullanır. Büyük v6 başlığı tam JSON ayrıştırmasından önce, ek bir metin kopyası oluşturmadan ve en fazla 64 MB'lık kaynak üzerinde aranır. Dosyanın okunması başarılı olup önizlemesi başarısız olursa doğrulanmamış ham kurtarma kaydı önerilir. Bu kodlama ve kurtarma yolları Apple ortamında henüz doğrulanmadı.

Sürüm 6'da proje, parça, kontur noktası, işaret, işaret konumu, gösterilen parça bilgisi veya iplik yönü için tanınmayan alan varsa yanlış geometri veya işaret sunmamak amacıyla önizleme reddedilir. Bu denetim masaüstündeki tüm proje, serileme, geçmiş ve üretim doğrulamalarının yerini tutmaz; dosya seçiciden açılan uygun boyuttaki reddedilmiş veri isteğe bağlı ham kurtarma kaydı olarak korunabilir.

Modellerim ekranında her kaydın yanındaki model işlemleri menüsünden kayıtlı kopya silinebilir; silme işleminden önce kayıt kimliğiyle onay istenir. Bu menü ve erişilebilirlik davranışı henüz Mac, simülatör veya cihazda doğrulanmadı.

Kopya klasöründeki tanınmayan dosyalar silmeyi engeller; kontrol ile silme arasında eşzamanlı değişiklik yarışı henüz giderilmedi. Silme akışı Mac veya cihazda doğrulanmadı.

Listelenen kayıt kimliği silme öncesinde yeniden kontrol edilir; ancak bu kontrol ve silme atomik değildir ve aynı dosyada her değişikliği yakalayacağı garanti edilmez.

Listelenen model açılırken veya ham kopyası kurtarılırken kimlik okuma öncesi ve sonrasında kontrol edilir. Bu kontroller atomik değildir ve eşzamanlı her değişikliği yakalama garantisi vermez; Apple ortamında doğrulanmadı.

64 KiB üzerindeki kayıt bilgileri sınırlı okunur; sürümü doğrulanamayan kopya normal açılmaz. Kullanıcı ham proje baytlarını ayrı dışa aktarmayı seçebilir; kayıt bilgisi bu kopyaya dahil edilmez. Bu kurtarma akışı henüz Mac veya cihazda doğrulanmadı.

Köşe işareti seçicisinde ilk 500 eşleşme listelenir; sonraki işaretler daha dar bir aramayla bulunabilir. Arama yeni sorguda iptal edilen arka plan işi olarak hazırlanır. Bu değişiklik ve çok büyük modellerde cihaz performansı henüz Apple ortamında doğrulanmadı.

Sürüm 7 döndürmeli pastal dosyalarında görünür konturlar ve işaretler önizlenebilir. Döndürme izi, geçmiş-kopya eşleşmeleri ve üretim uygunluğu telefonda doğrulanmaz; kaynak dosya baytları kaydetme ve dışa aktarmada korunur. Dosya veya doğrulama için dönüştürülen metin 8 MB'ı aşarsa önizleme yapılmaz; seçilen en fazla 64 MB dosyanın ham kurtarma kopyası saklanabilir. Sürüm 7'de proje adı, parça adı ve nokta düzenleme desteklenmez. Masaüstünde üretilmiş sentetik fixture eklenmiştir; Apple derlemesi, cihaz davranışı ve gerçek fabrika verileri henüz doğrulanmamıştır.

Görünür parça kimlikleri ile geçmişteki yerleştirme kimlikleri farklıysa telefon ayrıca uyarır. Kimliklerin eşleşmesi, geometrinin değişmediğini veya geçmişin doğruluğunu kanıtlamaz.

`ExperimentalJSONStream` üretime bağlı olmayan ilk artımlı UTF-8 JSON yapı denetimi denemesidir; belgeyi veya v7 izini yorumlamaz. Parçalı girdide anahtar yinelemesini, sözdizimini ve sınırlı kaynak kullanımını denetler; UTF-16/32 ve BOM dönüşümü henüz yoktur. 8 MB önizleme sınırını değiştirmez. Apple derlemesi, performans ve cihaz belleği henüz doğrulanmamıştır.

`ExperimentalV7PieceReader` aynı akış üzerinde v7 zarfı, kök alanlar ve pastal/döndürme geçmişi üst alanlarının ad ve temel türlerini kontrol edip görünür `Pieces` nesnelerinin özgün UTF-8 baytlarını sınırlı bellekte çıkaran ayrı bir prototiptir. Tam v7 şema ve geçmiş doğrulaması yapmaz, çizim üretmez ve çıktısı güvenilir proje olarak kullanılamaz; üretime bağlı değildir. Parça başına 512 KB, toplam çıkarılan parçalar için 16 MB ve 10.000 parça sınırı kullanır. Bu sınırlar gerçek veri ve cihazda henüz ölçülmemiştir.

`ExperimentalV7Contours`, çıkarılan parça baytlarından sınırlı bir kontur dönüşümü denemesidir; üretim okuyucusuna bağlı değildir. Deneysel belge, çağıranın bellekte tuttuğu en fazla 64 MB özgün `Data` ile `Drawing` adayını birlikte tutabilir; fedilen baytlarla eşleşme SHA-256 ile denetlenir. Bu ek bellek gerektirir; dosyayı doğrulamaz veya gerçek cihazda bellek güvenliği kanıtlamaz. Parça düzeyinde kimlik, nokta, işaret ve desteklenen bilgi alanlarını kontrol eder; v7 geçmişi veya masaüstü parmak izleri bütünüyle doğrulanmaz. Bu aday üretimde kullanılmaz ve 8 MB sınırını artırmak için yeterli değildir.

Deneysel akış kaynak ve yerleşim parçası yapısını, seçimi, adetleri, masaüstünün deterministik kopya kimliklerini, geçmiş eşlemelerini ve ortak geçmiş ayarlarını ayrıca sınar. Nokta ve iplik yönü için çeyrek dönüş sonrası tam öteleme vektörlerini ihtiyatla karşılaştırır; masaüstünde toleransla kabul edilen bazı geçerli dosyaları reddedebilir. Tarihsel parmak izi karşılaştırması ve signed-zero token bitleri deneysel olarak uygulanır; Apple ve tüm ondalık dönüşüm durumlarında doğrulanmamıştır. Üretim geometrisini kanıtlamaz; önizleme adayı kaydetme veya kurtarma yetkisi vermez.

Deneysel karşılaştırma kapalı kontur, iplik yönünün kontrol edilmiş olması ve seçilen tek/çift yön ile açı toleransına uymasını da arar. Bu, tarihsel verinin tam masaüstü doğrulaması değildir.

Deneysel yerleşim ayrıca parça çevre kutularının kumaş sınırında ve istenen aralıkta olduğunu denetler; olası çift sayısı iki milyonu aşarsa reddeder. Bu kutu kontrolü kontur topolojisini veya fiziksel üretim uygunluğunu kanıtlamaz.

Deneysel tarihsel konturlar için ayrıca sınırlı ve ihtiyatlı basit kontur taraması vardır; çakışan, tekrarlı veya sayısal olarak belirsiz kenarlar reddedilir. Bu tarama geçerli bazı konturları da reddedebilir ve masaüstü geometrisiyle tam eşdeğerlik veya üretim güvenliği kanıtı değildir. Toplam tarama bütçesi 50 milyon nokta-kare işlemidir.

`ExperimentalV7Fingerprint`, masaüstünün tarihsel parça özeti kodlaması için bağımsız bir denemedir. Deneysel akış, tarihsel parça JSON'undan sayısal token'ları ayrı çıkarıp açık IEEE-754 bitleriyle bu hesaplamaya bağlar. Masaüstü fixture özeti ve signed-zero farkı için testler tanımlıdır; Apple'da henüz çalıştırılmamıştır.

`ExperimentalJSONDoubleBits`, tek bir tam ASCII JSON sayı token'ını sınırlı ve iptal edilebilir biçimde binary64 bitlerine dönüştüren ayrı bir denemedir; `-0` işaretini korur ve sıfıra yuvarlanan sıfır dışı sayıları reddeder. Girdi byte'larından pointer saklamaz. Tüm ondalıkların masaüstüyle aynı yuvarlandığı ve büyük dosyadaki performans henüz doğrulanmamıştır.

## Sınırlar

- Model kaydında geçici yolun oluşturulduktan sonra başka bir işlemce değiştirilmesi hâlinde yanlış içerik yayımlanabilir. Tek dosyalı kayıtta yayın öncesi kimlik ve içerik denetimi bazı değişiklikleri yakalar; kopya dosyalarının yazılması açılmış dizine bağlanır ancak ilk açılıştan önce dizinin değiştirilmesi engellenmez; denetim ile yayın arasındaki yarış ve kopya klasörü kaydındaki yarış giderilmedi. Başarılı kayıtta da boş geçici klasör kalır; iptal edilen veya başarısız kaydın geçici verileri cihazda kalabilir.
- Tanınan geçici kayıt klasörlerindeki tek ve normal proje dosyası, Modellerim'de yalnız ham kurtarma olarak gösterilir. İçerik eksik veya bozuk olabilir; dışa aktarma öncesi ve sonrası kimlik kontrolü yapılır, geçici kayıt otomatik silinmez. Bu kontrol eşzamanlı değişikliklere karşı atomik değildir ve kayıt sırasında yanlış içerik yayımlanması yarışını çözmez. Apple cihazında doğrulanmadı.

Uygulamanın önceki hali GitHub'da iOS simülatöründe derlenip temel arayüz testlerinden geçti; gerçek cihazlar ve yeni dışa aktarma akışı henüz doğrulanmadı. Üretim sürümü değildir. Proje biçimini masaüstündeki doğrulayıcı kadar kapsamlı doğrulamaz. Seçili parçanın adı, bazı meta verileri ve iplik yönünün kayıt veya kontrol durumu gösterilir; kayıtlı iplik çizgisi ve yönü önizlemede gösterilebilir; üretime uygunluk doğrulanmaz. Serileme, pastal, kaynak geçmişi, DXF/SVG/HPGL üretim çıktısı, çizim araçları ve cihaz bağlantıları işlenmez. Çizim ekrana sığdırılır; ölçüm ya da üretim için kullanılmamalıdır. Dosya seçicisi tüm dosyaları gösterebilir, ancak yalnız `.tcad` uzantısı kabul edilir. UTF-8 JSON hedeflenmiştir; diğer kodlamalar doğrulanmamıştır.

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
