# iPhone elbise isteği doğrulama planı

Durum: Plan ve test kodu hazırlanmıştır; Xcode derlemesi, simülatör, cihaz, VoiceOver ve dosya sağlayıcısı testleri çalıştırılmamıştır. TestFlight veya üretim uygunluğu kanıtı değildir. Kullanıcı verisi veya WearCAD dosyaları kullanılmaz.

## Yerel Mac hazırlığı

- Xcode ve XcodeGen kurulu bir Mac, ayrı test simülatörü ve sentetik dosyalar kullanın. Ücretli CI veya servis başlatmayın. Cihaz üzerinde silme, zorla kapatma ve depolama baskısı senaryoları yalnız ayrılmış test verisiyle yapılır.
- `Apple` klasöründe `cat project.yml Tests/ci-overlay.yml > project.ci.yml` ve `xcodegen generate --spec project.ci.yml` ile CI test projesini oluşturun. `xcodebuild -list -project TekstilCADViewer.xcodeproj` ve `xcrun simctl list devices available` ile mevcut şema ve simülatör kimliğini doğrulayın.
- `xcodebuild -project TekstilCADViewer.xcodeproj -scheme TekstilCADViewerCI -destination 'platform=iOS Simulator,id=<simulator-UUID>' test` çalıştırın; komut, Xcode/iOS sürümü, commit ve test sonuçlarını kaydedin. Release için ayrıca ayrı Release build alın. Simülatörün temiz uygulama verisiyle başlatıldığını doğrulayın; UI testi önceki taslak bulursa geri yüklemeyi seçer.
- `Tests/Fixtures/dress-draft-v1.json` yalnız sentetik istek içerir. XCTest kaynakları `DesignDraftStoreTests.swift`, UI testi `testDressBriefModelAndMeasurementNavigation`; tamamı çalıştırılmayı bekliyor.

## Sıralı doğrulama

1. Model seçimi, tarifi ve ölçüleri yazın; ileri/geri gidip tüm alanların korunduğunu doğrulayın. Seçimin kalıp üretmediği uyarısı görünmeli. Normal istek dosyası ve ayrı kurtarma kopyasını yeni adlarla kaydedin; iptalde ekrandaki verinin kaldığını kontrol edin.
2. Boş, yalnız ölçü içeren ve 16.385 UTF-16 birimlik metinli taslakları ayrı kurtarma kopyasına kaydedip yeniden açın. Geçersiz/gelecek sürüm ve sınır üstü kopya açılırken mevcut ekran ve eski kayıt değişmemeli. Sentetik PNG/JPEG, geçersiz görsel ve 8 MiB sınırını kontrol edin; görsel baytlarını karşılaştırın.
3. Önceki taslağı geri yüklemeyi kabul ve reddedin. Metni tamamen silip yeniden açıldığında eski metnin dönmediğini kontrol edin. Yazma hatası ve depolama doluluğu için ayrılmış test ortamında önceki sağlam kaydı ve ekrandaki metni doğrulayın; hata, yeniden deneme ve ayrı kopya seçenekleri görünmeli.
4. Aynı kaydı iki sahnede açın; farklı değişiklikleri kaydedin. Çakışma sessizce üzerine yazmamalı; iki giriş ayrı kurtarma kopyalarıyla korunabilmeli. Devam eden yazı sırasında kopya açma ve kapatma, eski/yeni uyarı onayı ve bozuk ledger onarımını sınayın. Taslak dosyası silinmemeli; bozuk ledger yedeğinin kendisi de incelenmeli.
5. Büyük görselle hızlı yazın; son değişiklikten hemen sonra arka plana geçirin, tekrar açın ve ayrıca test cihazında zorla sonlandırmayı deneyin. Kaydedildi göstergesi, hata, süre aşımı ve son veri durumunu kaydedin. iOS süreç süresi veya UserDefaults'un fiziksel kalıcılığı garanti değildir; anlık sonlandırma testi başarıyla geçse bile garanti çıkarılmaz. Aynı işlemi düşük pil/güç ve kilitli cihaz koşullarında, uygun test ortamında tekrarlayın; süre, bellek ve enerji sonuçlarını ölçmeden performans iddiası vermeyin.
6. Yerel dosyalar ve izin verilen iCloud test belgeleriyle seçici iptali, erişim iptali, bozuk dosya ve sağlayıcı değişikliği sınayın. Sistem seçicisinin mevcut dosya üzerine yazma davranışını gerçek cihazda ayrıca doğrulayın.
7. iPhone dar ekran/büyük metin, açık/koyu görünüm, VoiceOver, harici klavye ve yatay/dikey yönü inceleyin. Her alanın adı, seçili kartın durumu, hata ve adım geçişi anlaşılır olmalı; kontrol kırpılması veya erişilemeyen eylem bulunursa kaydedin.

## iPhone erişilebilirlik ve odaktan çıkış matrisi

Aşağıdakiler beklenen davranıştır; hiçbiri cihazda doğrulanmış sonuç değildir. Ayrılmış küçük ekranlı iPhone ve daha geniş iPhone, varsayılan ve en büyük erişilebilirlik yazı boyutları, açık/koyu mod ayrı ayrı kaydedilir.

- **Büyük yazı / dar ekran:** Model kartı adı ve seçili durumu kesilmeden okunmalı; yatay kart listesine ve tüm alt eylemlere kaydırarak ulaşılmalı. Geri, devam ve kaydet düğmeleri görünür ve kullanılabilir olmalı. Uzun Türkçe adlar ve ekran döndürme ayrıca denenmeli.
- **VoiceOver:** Kartın adı, seçili/seçili değil durumu ve kalıp oluşturmadığı ipucu okunmalı. Her ölçü alanının adı ve birimi farklı olmalı; adım değiştirildiğinde yeni başlığa ve hata metnine ulaşılabilmeli. Sadece renge dayalı durum olmamalı. Elle VoiceOver rotor, ileri/geri kaydırma ve odak sırası kaydı tutulur.
- **Ekran klavyesi:** Tarif, dört temel ölçü ve 15 ölçü ekranında giriş yapın; `Klavyeyi kapat` ile klavye kapanmalı, metin korunmalı ve sonraki adım erişilebilir olmalı. Klavye açıkken üst/alt alanlara kaydırma denenmeli; alt gezinme alanı odak varken gizlenebilir, odaktan çıkınca geri gelmeli.
- **Donanım klavyesi:** Tab/Shift-Tab ile model kartı, tarif, klavyeyi kapatma veya başka odaklanabilir alana ve gezinme düğmelerine ulaşın. Klavye araç çubuğu donanım klavyesiyle görünmüyorsa gizli alt gezinmeden çıkışın mümkün olup olmadığını özellikle sınayın. Odak tuzağı veya görünmeyen devam düğmesi FAIL olarak kaydedilir; şu an koddan çalıştığı varsayılamaz.
- **Dokunma:** Model kartları ve eylemlerin tam yüzeyi kullanılabilmeli; 15 ölçü alanlarında küçük ekran ve büyük metinde yanlış alanın seçilmediği kontrol edilmeli. Standart öğeler dahil ölçülmüş hedef boyutları kaydedilir.
- Otomatik `testDressBriefModelAndMeasurementNavigation` seçili kart, tarif ve ölçü yazma, klavye kapatma ve geri dönüşte metnin korunmasını sınamak üzere hazırlandı; büyük yazı, VoiceOver veya donanım klavyesini bu test kanıtlamaz.

## Süreç kapanışı ve veri sınırı

Kaynakta seçili model, tarif, dört ölçü, ek notlar ve seçilmiş görsel `Draft` anlık görüntüsüne dahil edilir. Normal kayıtta doğrulama/atomik yazı ve revizyon kontrolü, başlangıçta kullanıcı onaylı geri yükleme bulunur. Arka plana geçişte hemen kayıt istenir; bekleyen işaretler ve iOS ek süre isteği ani süreç sonlandırmasında kalıcılık garantisi vermez. Kayıt hatası veya başka sahne çakışmasında ayrı kurtarma kopyası hazırlanabilir; seçicinin gerçek cihaz davranışı doğrulanmalıdır.

15 ölçü alt ekranındaki henüz onaylanmamış değerler ve birim, sürüm 2 yerel taslakta ayrı bölüm olarak saklanmaya çalışılır; ana ölçü notlarına yalnız açık onayla eklenir. Kaydedilmiş form yeniden açılırken kullanıcıdan geri yükleme seçimi istenir. Sürüm 1 taslaklar okunmaya devam eder; eski okuyucular sürüm 2 form taslağını reddetmelidir. Kayıt beklerken veya iOS aniden sonlandırırsa son değişiklik kaybolabilir; onay ve kaydetme durumları cihazda doğrulanmalıdır. Model/Unicode tarif, ölçü, 8 MiB sınırına yakın sentetik görsel ve kaydetme sonucu ayrı ayrı not edilerek kayıt tamamlandıktan sonra ve kayıt sürerken sonlandırma senaryoları yürütülür. Başarısızlıklar ve eski sürüm/bozuk kayıt korunması da kontrol edilir.
## Taşınabilir Base64 sınırı

Windows `DesignRequestStorage` ve iPhone `portableDraft`, kodlanmış Base64 metninin uzunluğunu boşlukları temizlemeden önce `((8 MiB + 2) / 3) * 4` sınırıyla karşılaştırır. Dolayısıyla sınırı dolduran 8 MiB görselin koduna tek boşluk eklenmesi, görsel baytları geçerli olsa bile iki tarafta da reddedilir; mevcut kurala göre bu platformlar arası fark değildir. Normal dışa aktarıcı boşluksuz kanonik Base64 üretir. iPhone ayrıca ImageIO okunabilirliğini aradığından, yalnız imzası geçerli bozuk görsellerde davranış yine farklı olabilir.

Sentetik sınır testi: tam 8 MiB geçerli görselin boşluksuz ve tek boşluk eklenmiş sürümlerini, dosya toplam 12 MiB sınırını da gözeterek her iki okuyucuda ayrı deneyin; kabul/ret ve eski taslağın korunmasını kaydedin. Bu senaryolar henüz çalıştırılmadı.

İleride daha esnek boşluk kabulü istenirse iki platformda birlikte, ham dosya ve ham Base64 üst sınırlarını koruyarak izin verilen boşlukların temizlenmesi, ardından normalize uzunluk ve çözülmüş bayt sınırlarının ayrı kontrolü tasarlanmalıdır. Eski dosyalar, Unicode, bellek tüketimi ve sınır testleriyle doğrulanmadan tek taraflı değiştirilmez.
## Eski iPhone taslak uyumluluğu

`Balenli korpus` ve `Minimal kılıf` yerel taslakta yeni `DressStyle` enum değerleridir. Bu değerleri tanımayan eski uygulama sürümü aynı yerel taslağı decode edemez; dosyayı otomatik dönüştürme veya düşürme yapılmaz. Eski sürüme dönmeden önce yeni sürümde taşınabilir `.tcad-request.json` olarak ayrı kopya kaydedin; model adı orada açıklama metni olarak korunur. Bu belge eski sürümde aynı kartın seçileceğini veya ölçü/görsel davranışının doğrulandığını iddia etmez. Eski sürümün bilinmeyen değerde dosyayı değiştirmediğini ve taşınabilir istek alışverişini gerçek eski/yeni cihaz derlemelerinde sınayın.
## Sonuç kaydı

Her senaryo için PASS/FAIL/BLOCKED, commit, cihaz, iOS/Xcode, test verisi, beklenen/gözlenen davranış ve hata kaydı tutulur. Gerçek ölçü veya görsel günlük/ekran görüntüsüne eklenmez. Başarısız veya çalıştırılmamış senaryo başarılı kabul edilmez. Kalıp üretimi, fiziksel plotter, ücretlendirme ve TestFlight yüklemesi bu planla doğrulanmaz.
