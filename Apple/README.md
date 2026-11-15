# Mono Di Tree CAD iPhone — başlangıç ve Kalıp Atölyesi

Bu bağımsız SwiftUI başlangıcı iPhone, iPad ve macOS için yerel `.tcad` dosyası seçip parça konturlarını salt okunur gösterir. Eski JSON nesnesi ile 1–8 sürümlü `[sürüm, proje]` zarfındaki `Name`, `Pieces`, `Points`, `Closed` alanlarını **yalnız kontur önizlemesi amacıyla** okur. Sürüm 6–8 için zorunlu bazı zarf alanları kontrol edilir; Windows doğrulayıcısının tüm şema ve geometri kuralları uygulanmaz. Dosyaya yazmaz. Dosya seçiciden iCloud Drive dahil erişilebilir dosyalar açılabilir.

## İlk açılış: yeni başlayan çalışma alanı

Ana ekranda **Yeni proje oluştur** ve **Örnekle dene** düğmeleri bulunur. Bu seçenekler `EasyCADWorkspace.swift` içindeki gerçek, yerel bir çizim atölyesini açar; bağlantı, hesap veya ücretli API gerektirmez.

- Yeni dikdörtgen veya çizgi ekle; parçayı seç ve değiştir.
- Dikdörtgenin en ve boyunu, çizginin uzunluğunu ve parçanın X/Y konumunu milimetre cinsinden düzenle.
- Parçayı 10 mm sağa/sola taşı, 90 derece döndür, çoğalt veya sil.
- Son 50 değişiklik için geri al/ileri al.
- `.tcad` dosyası hazırla; sistem Paylaş menüsünden Dosyalara Kaydet, AirDrop veya diğer seçeneği kullan.
- Çizimi yakınlaştır ve kaydır; seçili parçanın ölçüsünü çizimin üstünde oku (aşağıda "Çizimi yakınlaştırma, kaydırma ve ölçü etiketleri").

Ana ekranda "Nasıl çalışır?" başlığı altında üç adım (Şekil ekle, Ölçüyü yaz, Kaydet ve paylaş) gösterilir. Atölyede çizimin altında, çizimin durumuna göre değişen tek cümlelik bir ipucu bulunur: çizim boşken şekil eklemeyi, parça seçili değilken parçaya dokunmayı, parça seçiliyken ölçüleri milimetre olarak yazmayı söyler.

**Önemli:** Atölye yeni çizimlerin yanında **daha önce iPhone'un ürettiği basit, sürümsüz, üretim metaverisi içermeyen .tcad çizimlerini** de `Kopyasını düzenle` seçeneğiyle açabilir. Yalnızca adı, boş serileme listesi, benzersiz parça kimlikleri ve X/Y konturlarından oluşan katı şema kabul edilir. Sürüm zarfları, katman, çentik, geçmiş, pastal, ölçü kuralı veya başka üretim verileri bulunan fabrika dosyaları salt okunur kalır; kaynak dosya asla yeniden yazılmaz. İçe aktarılan çizim cihazda yeni taslak olarak saklanır ve eski taslağı değiştirmeden önce onay alınır. Dışa aktarılan dosyalar kontur/kimlik verisi ve boş serileme listesi içerir; üretim, kesim ya da gerçek CAD uyumluluğu sertifikası değildir. Dosyanın Windows uygulamasında açılıp tekrar okunması ve gerçek iPhone testleri ayrıca yapılmalıdır.


## Çizimi yakınlaştırma, kaydırma ve ölçü etiketleri

Atölyedeki çizim varsayılan olarak ekrana sığdırılır. Çizimde en az bir parça varsa:

- **Yakınlaştırma:** iki parmakla sıkıştırın ya da çizimin sağ alt köşesindeki **Yakınlaştır** ve **Uzaklaştır** düğmelerini kullanın. Düğmeler iki parmak hareketine bağlı kalmamak için vardır. Yakınlaştırma 1× ile 24× arasındadır; düğmeyle her dokunuş 1,6 kat değiştirir. Yakınlaştırma çizimin değil, çizim alanının merkezine göredir. Çizimin altındaki satır yakınlaştırılmışken örneğin "2.6× yakınlaştırıldı" yazar.
- **Kaydırma:** yakınlaştırılmışken boş kâğıdı sürükleyin. Bir parçanın üzerinden sürüklemek, daha önceki gibi parçayı taşır. Çizim, en fazla 48 noktası görünür kalacak kadar uzaklaştırılabilir; tamamen ekran dışına itilemez. Sürüklemeden kaydırmak için **Seçili parçayı göster** düğmesi seçili parçayı çizim alanının ortasına getirir (yakınlaştırılmış ve bir parça seçiliyken görünür). İki parmakla yapılan hareket sıkıştırma sayılır; parça ya da görünüm sürüklenmez.
- **Ekrana sığdır:** yakınlaştırılmışken düğmeler arasında **Ekrana sığdır** da görünür; yakınlaştırmayı ve kaydırmayı sıfırlar. Uzaklaştırarak 1×'e inmek de kaydırmayı sıfırlar. Klavye açılınca çizim küçüldüğü için görünüm ekrana sığdırılır; son parça silinince de sıfırlanır.
- **Parça taşıma:** yakınlaştırılmışken parmağın ekranda aldığı yol, yakınlaştırma oranına bölünerek milimetreye çevrilir; yani parça parmağın altında kalır. Parça taşınırken sıkıştırma hareketi yok sayılır.
- **Ölçü etiketleri:** seçili parçanın en ve boyu (açık çizgide uzunluğu) milimetre olarak çizimin üstünde küçük etiketlerle yazılır. Çizim alanı 150 nokta yüksekliğinden küçükse gösterilmez; bu, klavye açıkken ve telefon yatayken olur. Etiketler çizim alanının dışına taşmaz. Yakınlaştırma düğmeleri de klavye açıkken gizlenir.
- **Titreşim:** parça seçildiğinde ve taşınan parça bırakıldığında hafif bir dokunsal geri bildirim verilir (iOS dışında etkisizdir).

**Doğrulanmadı:** Bu değişiklikler Swift kodunda var, ancak Swift kodu yerelde derlenmedi; derleyen tek ortam iPhone CI'dır. Yakınlaştırma düğmeleri ve ipucu simülatör arayüz testlerinde, yakınlaştırma/kaydırma hesabı birim testlerinde var; **iki parmakla sıkıştırma ve boş kâğıdı sürükleyerek kaydırma hareketi simülatör testinde yoktur**. Ölçü etiketlerinin görünümü ve yakınlaştırma sırasında konumları otomatik denetlenmez. Gerçek cihazda ve VoiceOver ile henüz denenmedi. Etiketlerdeki ölçüler görüntü amaçlıdır; üretim ya da kesim için kullanmayın.

## Erişilebilirlik (kod düzeyinde; cihazda doğrulanmadı)

Çizim tuvalinin ekran okuyucu değeri parça sayısını, seçili parçanın adını ve ölçüsünü ve yakınlaştırma düzeyini söyler; yukarı/aşağı kaydırma (ayarlanabilir eylem) yakınlaştırır. Yakınlaştırma ve ölçü değişimi ekran okuyucuya duyurulur. Bölüm başlıkları başlık olarak işaretlidir, "Nasıl çalışır?" adımları numarasıyla okunur, hareket azaltma tercihi klavye animasyonunu kapatır. Soluk gri ve turuncu metinler koyulaştırıldı. VoiceOver ile gerçek cihazda sınanmadı; tuval içine çizilen ölçü etiketleri ekran okuyucuya yalnız bu değer üzerinden ulaşır.

## Görünüm

Uygulama yalnız açık temayla çalışır (`preferredColorScheme(.light)`); renkler sabittir ve sistem koyu moddayken de açık kalır. Koyu görünüm desteği yoktur. Bu, `ScreenshotTourTests` içindeki `testTour06SystemDarkStaysLight` ekran turuyla simülatörde görüntü olarak üretilir; görüntülerin gözle incelenmesi gerekir, kontrast ve okunabilirlik gerçek cihazda ve Dynamic Type ile ayrıca denenmedi.

## Xcode'da oluşturma

macOS, Xcode ve XcodeGen gereklidir. Terminalde `Apple` dizinine geçip `xcodegen generate` çalıştırın. Oluşan `TekstilCADViewer.xcodeproj` dosyasını Xcode'da açın; iOS veya macOS hedefini seçin. Uygulama adı Mono Di Tree CAD, bundle kimliği `com.berkdemirokk.monoditreecad` olarak ayarlanmıştır. Gerçek cihazda çalıştırmak için Signing & Capabilities altında bu kimliğe yetkili Apple geliştirme ekibini seçin. XcodeGen kurulumu ve Apple imzalama ayrıca gereklidir.

## Dosya erişimi

iOS ve iPadOS'ta sistem dosya seçicisinin sağladığı güvenlik kapsamlı erişim kullanılır; dosya paylaşımı veya iCloud Documents yeteneği bu temel görüntüleyici için ayrıca etkinleştirilmiş değildir. Mac sürümü bu aşamanın kapsamında değildir.

## Sınırlar

Uygulama GitHub'da iOS simülatöründe derlenip temel arayüz testlerinden geçti; gerçek cihazlar henüz doğrulanmadı. Üretim sürümü değildir. Proje biçimini masaüstündeki doğrulayıcı kadar kapsamlı doğrulamaz. Sürüm 6–8 için uygulama görünür bir uyarı verir: çentikler, döndürülmüş pastal izleri, katman görünürlüğü ve üretim semantiği dikkate alınmaz. Dolayısıyla Windows'ta gizli olan bir katman iPhone önizlemesinde görülebilir; bu görüntü üretim dosyası veya kullanıcı yetkilendirmesi değildir. Seçili parçanın adı, bazı meta verileri ve iplik yönünün kayıt veya kontrol durumu gösterilir; kayıtlı iplik çizgisi ve yönü önizlemede gösterilebilir; üretime uygunluk doğrulanmaz. **Salt okunur fabrika dosyası görüntüleyicisinde** serileme, pastal, kaynak geçmişi, üretim dışa aktarma ve cihaz bağlantıları işlenmez. **Ayrı basit çizim atölyesi** kontur oluşturma, düzenleme ve .tcad dosyası paylaşma sağlar. Çizim varsayılan olarak ekrana sığdırılır, istenirse yakınlaştırılıp kaydırılabilir; ölçüm ya da üretim için kullanılmamalıdır. Ölçü etiketleri de görüntü amaçlıdır. Dosya seçicisi tüm dosyaları gösterebilir, ancak yalnız `.tcad` uzantısı kabul edilir. UTF-8 JSON hedeflenmiştir; diğer kodlamalar doğrulanmamıştır.

Dağıtım öncesi en azından Xcode derlemesi, gerçek cihaz dosya sağlayıcıları ve masaüstü örnekleriyle uyumluluk, erişilebilirlik ve büyük dosya performansı doğrulanmalıdır.

## TestFlight hazırlığı (taslak)

TestFlight için macOS üzerinde Xcode ve XcodeGen ile projenin üretilmesi, doğru Apple Developer ekibiyle imzalanması ve App Store Connect'te `com.berkdemirokk.monoditreecad` kimliğiyle eşleşen uygulama kaydı gerekir. Arşivleme, yükleme ve TestFlight süreci gerçek Mac ve Apple hesabında doğrulanmalıdır. Otomasyon ileride kurulursa App Store Connect API anahtarı gibi gizli değerler depoya eklenmemeli, GitHub Secrets gibi bir gizli bilgi deposunda tutulmalıdır. Elle tetiklenen yükleme taslağı aşağıda açıklanmıştır; otomatik yayın yapılandırılmamıştır.

## GitHub Actions ile derleme kontrolü

`.github/workflows/apple-ci.yml` hem elle tetiklendiğinde hem de `Apple/**` dosyalarını değiştiren `main` hedefli pull request'lerde çalışır. GitHub'ın macOS çalıştırıcısında XcodeGen projesini üretir, iPhone simülatöründe derler ve UI testlerini çalıştırır. Aynı PR'daki eski çalışmalar yeni commit geldiğinde iptal edilir. Simülatör testleri uygulamanın açılışını, sistem dosya seçicisi iptalini, test fixture'larından kontur yükleme/hatalı dosya reddini ve yeni sürüm uyarısını kontrol eder; gerçek dosya sağlayıcısını, üretim-format round-trip'ini, gerçek cihazı veya TestFlight yüklemesini doğrulamaz. GitHub kullanım kotasını önce kontrol edin.

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


## Ücretsiz, çevrimdışı dokunmatik editör (deneysel)

Kalıp Atölyesi artık yalnızca düğmelerle değil, çizim tuvali üzerinden de kullanılabilir:

- Kalıbın içi veya çizgisine dokunarak parça seç; çizginin üstünden sürükleyerek taşı
- İsteğe bağlı **5 mm'ye hizalama** ile parmak hareketini CAD koordinatına çevir
- Seçilen parçayı aynı düzenleme kontrolleriyle hassas mm sayısal girişten de değiştir
- Her tamamlanan düzenlemede **2 MB sınırına sahip** yerel, atomik taslak kaydı oluştur; ana sayfada **Son çizime devam et** ile aç
- Eski taslağın üzerine yeni çalışma başlatmadan önce açık uyarı ve onay göster

Bu sistem yerel dosya depolamasına dayanır; ücretli AI, bulut servisi veya hesap gerektirmez. Otomatik taslak kurtarma **yalnızca uygulama tarafından oluşturulan basit kontur projeleri** içindir. İçe alınan karmaşık endüstriyel .tcad dosyaları salt okunur görüntüleyicidedir. Taslak dosyası harici yedek değildir: önemli işleri **Dosyalara Kaydet** üzerinden ayrıca dışa aktarın.

**Kalite kapısı:** Bu dalın değişikliklerinin iOS CI'da, gerçek dokunmatik iPhone'da ve VoiceOver ile doğrulanması gerekir. Hareket hassasiyeti üretim veya kesim toleransı onayı değildir.

## Güvenli çizim kopyası

`CADSketchImport.swift` özel bir "düzenlenebilir kopya" geçididir. Dosya ancak **2 MB altında**, sürümsüz ve tam olarak `Name/Pieces/GradingRules=[]` zarfında, yalnızca `Id/Name/Closed/Points` içeren parçalarla düzenlenebilir. UUID, boolean, sonlu koordinat, 250 parça ve 20.000 toplam nokta sınırı kontrol edilir. Tek bir bilinmeyen alan, dikiş payı, çentik, katman veya serileme kuralı dosyayı salt okunur yapar. Bu davranış kaynak dosyayı değiştirmeme garantisini korumak içindir; genel CAD uyumluluğu değildir.

`Apple/Tests/Fixtures/mobile-simple.tcad` pozitif dosya örneğidir. `FixtureLoadingTests.swift` içinde basit dosyanın düzenlenebilir kopyaya geçmesi ve metaverili fabrika dosyalarının düzenleme düğmesi göstermemesi test edilir. Gerçek iPhone dosya sağlayıcısıyla test hâlâ gereklidir.
