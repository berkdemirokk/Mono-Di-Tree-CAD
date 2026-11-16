# App Store hazırlığı — kalan adımlar

Bu klasör iPhone/iPad uygulamasını App Store'a göndermek için gereken metin taslaklarını tutar. Hepsi **taslaktır**; App Store Connect'e girilmeden önce uygulama sahibi tarafından okunmalıdır.

| Dosya | İçerik |
|---|---|
| `metadata-tr.md`, `metadata-en.md` | Ad, alt başlık, tanıtım metni, açıklama, anahtar kelimeler (karakter sayılarıyla), kategori, yaş derecelendirmesi yanıtları, 1.03 "Neler var?" |
| `privacy-policy.md` | Türkçe ve İngilizce gizlilik politikası taslağı ve App Store gizlilik etiketi yanıtları |
| `review-notes.md` | App Review için not (giriş gerekmez, ana akış adım adım) |
| `screenshots.md` | Gereken ekran görüntüsü boyutları, CI görüntülerinden hangilerinin kullanılabileceği, eksikler |

Bu dalda koda yakın iki değişiklik de var:

- `Apple/TekstilCADViewer/PrivacyInfo.xcprivacy` — gizlilik bildirimi: izleme yok, toplanan veri yok, dosya zaman damgası API'si için C617.1, DDA9.1, 3B52.1 gerekçeleri. XcodeGen bilinmeyen uzantılı dosyaları kaynak klasöründen "Copy Bundle Resources" aşamasına koyar; ayrıca ayar gerekmez.
- `Apple/project.yml` — `INFOPLIST_KEY_ITSAppUsesNonExemptEncryption: NO`. Apple'ın Xcode derleme ayarları belgesinde ("App Uses Non-Exempt Encryption") bu ayar, `GENERATE_INFOPLIST_FILE` açıkken `Info.plist`'e `ITSAppUsesNonExemptEncryption` anahtarını yazar. App Store Connect her TestFlight derlemesinde şifreleme sorusunu bu sayede sormamalıdır.

## Yalnız uygulama sahibinin yapabileceği adımlar

- [ ] **Lisans kararı.** Ürün lisansı açık karardır. App Store'da Apple'ın standart son kullanıcı sözleşmesi (EULA) mı, özel bir sözleşme mi kullanılacağına karar verin; özel sözleşme App Store Connect'te "License Agreement" alanına girilir. Deponun kendi lisansı ayrı bir karardır.
- [ ] **Apple Developer Program ve sözleşmeler.** Üyeliğin etkin olduğunu, App Store Connect > Business bölümünde gerekli sözleşmelerin kabul edildiğini kontrol edin. Ücretli olacaksa "Paid Apps" sözleşmesi, banka ve vergi bilgileri gerekir.
- [ ] **App Store Connect uygulama kaydı.** Apps > "+" > New App: platform iOS, ad "Mono Di Tree CAD" (ad App Store'da başka bir uygulamada kullanılıyorsa kabul edilmez), birincil dil Türkçe, bundle kimliği `com.berkdemirokk.monoditreecad`, kendi seçeceğiniz bir SKU.
- [ ] **AB tacir (trader) beyanı.** Uygulama AB ülkelerinde dağıtılacaksa App Store Connect, Dijital Hizmetler Yasası gereği tacir olup olmadığınızı sorar. Tacirseniz adres, telefon ve e-posta mağaza sayfasında herkese açık gösterilir. Bu beyan yapılmadan AB'de yayın olmaz.
- [ ] **Gizlilik politikasını yayımlama.** `privacy-policy.md` dosyasındaki köşeli parantezli yerleri doldurun ve herkese açık bir adreste yayımlayın (ör. GitHub Pages). Depo özelse depo bağlantısı işe yaramaz. Adresi App Information > Privacy Policy URL alanına girin.
- [ ] **Destek adresi.** Erişilebilir bir destek sayfası (zorunlu) ve isteğe bağlı pazarlama adresi.
- [ ] **App Privacy anketi.** "Data Not Collected" (Veri Toplanmıyor) yanıtı; gerekçesi `privacy-policy.md` içinde.
- [ ] **Yaş derecelendirmesi anketi.** Önerilen yanıtlar `metadata-tr.md` içinde; beklenen sonuç 4+.
- [ ] **İçerik hakları sorusu.** "Uygulama üçüncü taraf içeriği içeriyor, gösteriyor ya da ona erişiyor mu?" — kodda böyle bir şey yok; yanıt büyük olasılıkla "Hayır".
- [ ] **Fiyat ve bulunabilirlik.** Ücretsiz mi ücretli mi, hangi ülkeler. Ücretli olacaksa uygulamada satın alma yok; fiyat yalnız indirme fiyatıdır.
- [ ] **Telif hakkı satırı.** Ör. `2026 <hak sahibi>`.
- [ ] **Ekran görüntüleri.** `screenshots.md`'ye göre en az 6.3 inç iPhone ve 13 inç iPad seti. Uygulama iPad'de de çalıştığı için iPad seti zorunludur.
- [ ] **Derleme yükleme.** `apple-testflight.yml` yalnız elle çalışır; ne zaman çalıştırılacağına siz karar verirsiniz. Sürüm `1.03` ve bu sürümde kullanılmamış bir derleme numarası girin.
- [ ] **TestFlight'ta kontrol.** Yüklenen derlemede "Missing Compliance" (eksik uyumluluk) uyarısı çıkmamalı. Çıkarsa anahtar `Info.plist`'e yazılmamış demektir; aşağıdaki "Doğrulanmadı" bölümüne bakın.
- [ ] **Gerçek cihaz denemesi.** iPhone ve iPad'de, VoiceOver ve büyük yazıyla; dosya açma ve Dosyalar'a kaydetme. App Review uygulamayı iPad'de de açabilir.
- [ ] **İnceleme notu ve gönderim.** `review-notes.md` metnini App Review Information > Notes alanına yapıştırın, iletişim bilgilerinizi girin, sürümü incelemeye gönderin. Yayın biçimi için "elle yayınla" seçeneği kararı size bırakır.

## Doğrulanmadı

- Bu bilgisayarda Xcode ve XcodeGen yok; proje üretilmedi ve derlenmedi. Değişiklikler ilk kez iPhone CI'da (`apple-ci.yml`) derlenecek.
- `PrivacyInfo.xcprivacy` dosyasının `.app` paketinin köküne kopyalandığı derlenmiş uygulamada görülmedi. Kontrol: arşivde Xcode > Organizer > "Generate Privacy Report" ya da `.app` içinde dosyanın varlığı.
- `ITSAppUsesNonExemptEncryption = NO` değerinin derlenen `Info.plist`'e yazıldığı görülmedi. Apple belgesi ayarı listeliyor; 2021'de Xcode 13 için "çalışmıyor" diyen tek bir Stack Overflow sorusu var. Kontrol: `plutil -p <App>.app/Info.plist | grep ITSApp`. Yazılmamışsa yedek yol, yalnız bu anahtarı içeren küçük bir `Info.plist` dosyasını `INFOPLIST_FILE` ile vermektir; Xcode `GENERATE_INFOPLIST_FILE` açıkken o dosyayı üretilen anahtarlarla birleştirir.
- Dikkat: `project.yml`'deki `INFOPLIST_KEY_UIFileSharingEnabled`, Apple'ın aynı belgesindeki `INFOPLIST_KEY_` listesinde **yok**. Xcode bu ayarı yok sayıyor olabilir; yani "Dosyalar uygulamasında görünme" ayarı derlenmiş uygulamada bulunmayabilir. Uygulama verisini zaten `Application Support` altında tuttuğu için mağaza metinleri buna dayanmaz. Ayrıca kontrol edilmeli.
- Yaş derecelendirmesi ve gizlilik anketlerinin soru adları App Store Connect'te farklı görünebilir.
