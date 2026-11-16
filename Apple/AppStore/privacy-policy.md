# Gizlilik politikası / Privacy policy — TASLAK / DRAFT

> **Bu bir taslaktır.** Uygulamanın iPhone/iPad kodu (16 Kasım 2026, `ios/gap-store` dalı) okunarak yazıldı; hukuki görüş değildir. Kullanıcı (uygulamanın sahibi) metni gözden geçirmeli, köşeli parantezli yerleri doldurmalı ve **herkese açık bir adreste** yayımlamalıdır (ör. GitHub Pages). App Store Connect, uygulama kaydında bu adresi "Privacy Policy URL" olarak ister. Uygulamaya ağ bağlantısı, hesap, analiz, reklam ya da üçüncü taraf kitaplık eklenirse bu metin ve App Store gizlilik yanıtları **yayından önce** güncellenmelidir.
>
> **This is a draft**, written from the iPhone/iPad source code (16 November 2026, branch `ios/gap-store`). It is not legal advice. The app owner must review it, fill in the bracketed parts and publish it at a **public URL** (for example GitHub Pages); App Store Connect asks for that URL. If networking, accounts, analytics, ads or third-party libraries are ever added, update this text and the App Store privacy answers **before** release.

Kodda doğrulananlar / Verified in the code:

- Ağ yok: `URLSession`, `URLRequest`, `Network`, web görünümü ya da başka bir ağ API'si yok. / No networking API or web view.
- Analiz, izleme, reklam, hesap, giriş yok; üçüncü taraf kitaplık yok (`project.yml` içinde paket bağımlılığı yok). / No analytics, tracking, ads, accounts or third-party libraries.
- Konum, kamera, mikrofon, fotoğraflar, kişiler, takvim, sağlık verisi istenmez; `Info.plist` içinde hiçbir kullanım açıklaması (`NS…UsageDescription`) yok. / No permission prompts of any kind.
- `@AppStorage` (UserDefaults) yalnız iki görünüm tercihi için kullanılır: görünüm seçimi (`appearance`) ve atölyedeki kaydırma ipucunun gösterilip gösterilmediği. Cihazdan çıkmaz; manifestte CA92.1. / `@AppStorage` (UserDefaults) holds only two display preferences (appearance choice, whether the pan hint was shown); it never leaves the device; declared as CA92.1.
- Veriler uygulamanın kendi kapsayıcısında, `Application Support/MonoDiTreeCAD/` altında JSON dosyası olarak durur; paylaşım için hazırlanan `.tcad` dosyası geçici klasöre yazılır. / Data lives as JSON files in the app's own container; a file prepared for sharing is written to the temporary folder.

---

## Türkçe

**Mono Di Tree CAD Gizlilik Politikası**
Yürürlük tarihi: [GG.AA.YYYY]
Geliştirici: [ad veya şirket adı], iletişim: [e-posta adresi ya da iletişim sayfası]

### Kısaca

Mono Di Tree CAD hiçbir kişisel veri toplamaz. Uygulamada hesap yoktur, internet bağlantısı kullanılmaz; reklam, analiz ve izleme yoktur. Çizimleriniz yalnız sizin cihazınızda saklanır.

### Cihazınızda saklanan bilgiler

- **Çizimler ve projeler.** Oluşturduğunuz çizimler, kaydettiğiniz projeler ve otomatik kaydedilen son taslak, uygulamanın cihazdaki kendi klasöründe saklanır. Bu bilgiler bize ya da başka birine gönderilmez.
- **Proje tarihleri.** Son projeler listesinde her projenin son değiştirilme tarihi gösterilir. Bu tarih, cihazdaki dosyadan okunur ve yalnız ekranda gösterilir.
- **Paylaşım için hazırlanan dosya.** "Paylaş veya Dosyalara kaydet" dediğinizde dosya cihazın geçici klasörüne yazılır; sistem bu klasörü zamanla temizler.
- **Görünüm tercihleri.** Seçtiğiniz görünüm (Açık, Koyu, Telefona uy) ve bir ipucunun gösterilip gösterilmediği cihazda saklanır.

### Açtığınız dosyalar

"Dosya aç (.tcad)" ile seçtiğiniz dosyayı uygulama yalnız sizin seçtiğiniz anda, sistemin dosya seçicisinin verdiği izinle okur ve önizler. Kaynak dosya hiçbir zaman değiştirilmez. "Kopyasını düzenle" seçerseniz çizimin bir kopyası cihazınızda yeni taslak olarak saklanır.

### Paylaşma

Bir dosyayı yalnız siz "Paylaş" menüsünü kullandığınızda, sizin seçtiğiniz yere (ör. Dosyalar, AirDrop, e-posta) gönderilir. Bu aktarımı uygulama değil, seçtiğiniz hizmet yapar; o hizmetin kendi gizlilik koşulları geçerlidir.

### Yedekler

Uygulamanın klasörü, cihazınızın ayarlarına göre iCloud ya da bilgisayar yedeğine dahil olabilir. Yedeklemeyi Apple ve siz yönetirsiniz; biz bu yedeklere erişemeyiz.

### Apple'ın topladığı bilgiler

Cihaz ayarlarında "Uygulama Geliştiricileriyle Paylaş" seçeneğini açtıysanız Apple, çökme raporları gibi tanılama bilgilerini geliştiricilerle paylaşabilir. Bu, Apple'ın kendi sistemidir ve Apple'ın gizlilik politikasına tabidir; uygulamanın içinde böyle bir veri toplama kodu yoktur. Bu seçeneği Ayarlar > Gizlilik ve Güvenlik > Analizler ve İyileştirmeler bölümünden kapatabilirsiniz.

### Verilerinizi silme

Bir projeyi uygulamadaki Son projeler listesinden silebilirsiniz. Uygulamayı cihazdan sildiğinizde, uygulamanın klasöründeki tüm çizimler ve projeler de silinir. Daha önce paylaştığınız ya da Dosyalar'a kaydettiğiniz dosyalar, kaydettiğiniz yerde kalır.

### Çocuklar

Uygulama hiç kimseden kişisel veri toplamadığı için çocuklardan da toplamaz.

### Değişiklikler

Bu politika değişirse güncel metin bu adreste, yeni yürürlük tarihiyle yayımlanır.

### İletişim

Sorularınız için: [e-posta adresi ya da iletişim sayfası]

---

## English

**Mono Di Tree CAD Privacy Policy**
Effective date: [YYYY-MM-DD]
Developer: [name or company], contact: [email address or contact page]

### In short

Mono Di Tree CAD does not collect any personal data. There is no account and the app does not use an internet connection; there are no ads, analytics or tracking. Your drawings are stored only on your device.

### What is stored on your device

- **Drawings and projects.** The drawings you make, the projects you save and the automatically saved latest draft are kept in the app's own folder on your device. They are not sent to us or to anyone else.
- **Project dates.** The recent projects list shows when each project was last changed. The date is read from the file on your device and is only shown on screen.
- **Files prepared for sharing.** When you share a .tcad file, it is written to the device's temporary folder, which the system clears over time.
- **Display preferences.** Your appearance choice (light, dark, follow the phone) and whether a hint was already shown are stored on the device.

### Files you open

When you choose a file with "Dosya aç (.tcad)" (Open file), the app reads and previews it only at that moment, with the permission the system file picker gives. The original file is never changed. If you choose "Kopyasını düzenle" (Edit a copy), a copy of the drawing is stored on your device as a new draft.

### Sharing

A file leaves the app only when you use the Share sheet, and it goes to the destination you pick (for example Files, AirDrop or email). That transfer is done by the service you choose, under its own privacy terms.

### Backups

Depending on your device settings, the app's folder may be included in iCloud or computer backups. Backups are managed by Apple and by you; we have no access to them.

### Information Apple collects

If you turned on "Share With App Developers" in your device settings, Apple may share diagnostic information such as crash reports with developers. This is Apple's own system and falls under Apple's privacy policy; the app itself contains no code that collects such data. You can turn it off in Settings > Privacy & Security > Analytics & Improvements.

### Deleting your data

You can delete a project from the recent projects list in the app. Deleting the app from your device deletes all drawings and projects in the app's folder. Files you shared or saved to Files earlier stay where you saved them.

### Children

The app collects no personal data from anyone, including children.

### Changes

If this policy changes, the updated text will be published at this address with a new effective date.

### Contact

Questions: [email address or contact page]

---

## App Store gizlilik etiketi yanıtları / App Store privacy "nutrition label" answers

App Store Connect > App Privacy:

| Soru / Question | Yanıt / Answer |
|---|---|
| Do you or your third-party partners collect data from this app? | **No, we do not collect data from this app.** |
| Sonuç etiketi / Resulting label | **Data Not Collected** (Veri Toplanmıyor) |
| Tracking (ATT) | Not used. `NSPrivacyTracking` is `false` in `PrivacyInfo.xcprivacy`; no `NSUserTrackingUsageDescription`. |
| Privacy Policy URL | The public address where this file is published (required). |
| User Privacy Choices URL | Optional; leave empty. |

Dayanak / Basis (Apple, [App privacy details](https://developer.apple.com/app-store/app-privacy-details/)): Apple tanımına göre "toplamak", veriyi cihazdan dışarı, geliştiricinin ya da ortaklarının gerçek zamanlı istek için gerekenden uzun süre erişebileceği biçimde iletmektir; yalnız cihazda işlenen veri "toplanmış" sayılmaz. Apple'ın topladığı veriler için geliştirici sorumlu değildir. / Apple defines "collect" as transmitting data off the device so that the developer or partners can access it longer than needed to serve the request; data processed only on device is not collected, and developers are not responsible for disclosing data collected by Apple.

Bu yanıtlar şu varsayıma dayanır: yayımlanan derleme bu daldaki kodla aynıdır ve ağ, analiz, reklam ya da üçüncü taraf SDK içermez. / These answers assume the shipped build matches this code and contains no networking, analytics, ads or third-party SDKs.
