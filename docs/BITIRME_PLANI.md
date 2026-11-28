# Mono Di Tree CAD: bitirme planı

16 Kasım 2026. Kaynak: altı araştırma ajanı ve iki eleştirmen ajanın, 16 Kasım’da `main` dalına alınan `integration/rc-trial` dalını ([PR #47](https://github.com/berkdemirokk/Mono-Di-Tree-CAD/pull/47)) yalnızca okuyarak çıkardığı bulgular; ham bulgular bu depoda tutulmuyor, aşağıdaki özet yeterli. Bu belge onaylanmadan kod yazılmaz.

## 1. Dürüst durum

Ürün "az kaldı" noktasında değil. Çekirdek sağlam: komut kutusuna yazınca önizleme çıkıyor, onaylayınca uygulanıyor, geri alınıyor, kaydediliyor; bunlar CI'da penceresiz kontrollerle kanıtlı. Ama hiç bilmeyen bir terzi bugün "programı indirdim"den "kesime hazır kalıbım var"a tek başına varamıyor:

| Adım | Bugün | Neden |
|---|---|---|
| Programı indirip açmak | Kısmen | İmzasız ZIP; birçok bilgisayarda Windows engeller ya da uyarır. Yükleyici yok. |
| İlk ekranı anlamak | Çalışıyor | Basit, açık görünüm; "Nereden başlamak istersiniz?" |
| Yazarak parça çizmek | Yalnız dikdörtgen, kare, çizgi | Yamuk, yaka, kol evi gibi gerçek parça yazarak çizilemiyor. |
| Ölçü değiştirmek | Yalnız düz dikdörtgende | Diğer parçalarda "silip yeniden çizin" deniyor. |
| Taşımak, geri almak, hata düzeltmek | Çalışıyor | |
| Dikiş payı | Kısmen | Komut teknik bir pencereye götürüyor; önizleme yok. |
| Ad vermek | Parça: çalışıyor. Proje: hayır | Proje adı hep "Yeni proje" kalıyor. |
| Kaydetmek, yeniden açmak | Çalışıyor (pencere elle hiç denenmedi) | |
| Yazdırmak ya da kesime dosya hazırlamak | Kısmen | Yazdır komutu yok; dosya yazılıyor ama açılmıyor; altı teknik biçim soruluyor. |
| Adını bilmediği aracı bulmak | Bulmak çalışıyor | Bulunan araçların çoğu uzman penceresi. |
| Sesle komut | Kısmen | Düğme yalnızca Windows'un Win+H özelliğine yönlendiriyor; gerçek Türkçe konuşmayla hiç denenmedi. |

İki genel gerçek:
- Windows’ta örnek proje, dikdörtgen çizme, geri alma ve Farklı Kaydet penceresini açma gözlemlendi. Pencereden kaydın tamamlanması ve gerçek operatörün uçtan uca kabulü açık. [Masaüstü deneme kaydı](reviews/ui-smoke.md)
- **Birim tuzağı:** yazılan çıplak sayı milimetre sayılıyor; terzi santim düşünür.

## 2. Hatalar neden tekrarladı ve kural olarak ne değişiyor

1. Parçalar ayrı dallarda yazılıp birlikte hiç denenmedi → **tek gövde**, aynı anda **tek iş**.
2. Kontrolü kodu yazan ajan yazdı → **kabul kontrolünü başka bir ajan, kullanıcı adımından yola çıkarak yazar**; kod ondan sonra yazılır.
3. Kontroller parçayı tek başına sınadı → **yolculuk kontrolü**: gerçek açılış yolu, gerçek tuş olayları, uçtan uca.
4. "Bitti" tanımsızdı → bu belgedeki yolculuk tablosu ve **yayın kapısı** tanımdır.
5. Yeni özellik eklemek dondu: yalnızca bu plandaki işler yapılır.

## 3. Önce sizin kararlarınız (kod gerektirmez, her şeyi belirler)

| # | Karar | Önerim |
|---|---|---|
| K1 | İlk sürüm yalnız Windows mu, iPhone da mı? | Önce Windows. iPhone ayrı hat, TestFlight denemesiyle sonra. |
| K2 | İlk sürüm ücretsiz önizleme mi, satılacak mı? Kaynak kodu açık mı kapalı mı? | Ücretsiz önizleme. Lisans metni buna göre yazılır; seçimi ben yapamam. |
| K3 | Dağıtım yolu: Microsoft Store mu (paketi Store imzalar; hesap sizin kimliğinizle açılır), yoksa satın alınan sertifika + yükleyici mi? | Önce Store. Türkiye'den bireysel hesabın uygunluğu doğrulanmadı; sizin bakmanız gerekir. |
| K4 | Çıplak sayı santimetre mi milimetre mi sayılsın? | Santimetre. |
| K5 | Programı nerede deneyeceksiniz? Bu bilgisayarda Akıllı Uygulama Denetimi imzasız derlemeyi engelliyor. | Yedek bir bilgisayar. Denetimi kapatmak sizin kararınız; ben dokunmam. |
| K6 | Acemi nereden başlasın: yalnız çizim ve dosya açma mı (A), yoksa ölçüden hazır kalıp (etek, beden) mı (B)? | İlk sürümde A, açıkça söylenerek; B sonraki sürüm. |
| K7 | Sesle kullanım: Windows'un Win+H özelliği yeterli mi (internet ister), yoksa uygulama içi tanıma mı (büyük iş)? | İlk sürümde Win+H, önce sizin 10 cümlelik denemenizle. |
| K8 | #47 `main`'e tek birleştirmeyle girsin, eski 32 PR kapatılsın mı? | Evet; onay sizde. |

**Karar (16 Kasım 2026):** sahibi K1–K8'in hepsinde önerileri kabul etti: önce Windows; ücretsiz önizleme; önce Microsoft Store; çıplak sayı santimetre; deneme yedek bilgisayarda; ilk sürümde yalnız çizim ve dosya açma; ses için Win+H; #47 `main`'e tek birleştirme ve eski PR'ların kapatılması. Açık kalan: K2'nin "kaynak kodu açık mı kapalı mı" yarısı.

Ayrıca sizden gerekenler: 5–10 gerçek kalıp dosyası (DXF ve varsa PLT), ilk müşterilerin kullandığı program ve yazıcı/kesici bilgisi, deneme için 3–5 kişi (en az biri terzi), destek için yayımlanacak e-posta.

## 4. Aşamalar

Her iş sırayla yapılır. Her iş için: kabul kontrolü (başka ajan) → kod (küçük ajan) → inceleme (ayrı ajan) → CI → ekran görüntüsüne bakış. Bir iş yeşil olmadan sonraki başlamaz.

### Aşama 1: Gövdeyi sabitle (yaklaşık 1 gün)
- #47’nin son Windows kontrolü geçti, iPhone simülatör kontrolü başarısız oldu. #30’un son sürümü hem Windows hem iPhone simülatör kontrollerini geçti ve 20 Kasım 2026’da `538774ff` ile `main` dalına birleşti. [#47 iPhone sonucu](https://github.com/berkdemirokk/Mono-Di-Tree-CAD/actions/runs/43908897596) · [#30 Windows sonucu](https://github.com/berkdemirokk/Mono-Di-Tree-CAD/actions/runs/44346805350) · [#30 iPhone sonucu](https://github.com/berkdemirokk/Mono-Di-Tree-CAD/actions/runs/44346805738)
- Eskimiş belgeleri düzelt: `docs/RELEASE_CHECKLIST.md`, `CLAUDE.md` durum bölümü.
- `AGENTS.md`'ye dal kuralları: tek gövde `main`, dallar hep güncel `main`'den, üst üste PR yok, birleştirme yalnız sahibinde.
- #47, 16 Kasım’da `main` dalına birleşti. Kalan dal yönetimi işleri: `main` için kural (zorunlu Windows kontrolü, zorla gönderme yok); eski PR'ları #47'ye işaret ederek kapat.
- Birleşmeden sonra `main` üzerindeki CI'ın doğrulamayı ve ekran görüntülerini ürettiğini doğrula.

### Aşama 2: Kabul düzeneği (2–3 gün; ürün kodu değişmez)
- **Yolculuk doğrulaması:** gerçek açılış yolunu (`App.OnStartup`) ve gerçek tuş olaylarını kullanan, bölüm 1'deki her adımı uçtan uca sınayan kontrol dosyası. Bugün geçmeyen adımlar "bekleniyor" olarak görünür.
- Doğrulama ilk hatada durmak yerine bütün hataları raporlasın.
- CI'da Türkçe (tr-TR) kültürle bir tur: ondalık virgül, ı/İ harfleri.
- [Windows operatör kabul formu](OPERATOR_KABUL_FORMU.md) ile kurulum, kaydet/aç pencereleri, Win+H ile 10 cümle, yazıcıda 50 mm ölçüm ve bağımsız DXF kopyasını deneyin.
- **Yayın kapısı:** otomatik kapı yeşil + elle betik sizin imzanızla, aynı sürüm için.

### Aşama 3: Yolculuk boşlukları (yaklaşık 1–2 hafta; sırayla)
1. Birim (K4) ve bütün ipuçlarının aynı birimi söylemesi.
2. Proje adı: ilk kayıtta dosya adından; "projenin adı … olsun".
3. Yazarak gerçek parça: nokta listesiyle çokgen, "yamuk alt 60 üst 40 yükseklik 50".
4. Her parçada ölçü değiştirme (ölçekleme), çıkmaz ret yerine.
5. Dikiş payı gerçek komut: tuvalde önizleme, onayla, tek geri alma adımı; desteklenmeyen parçada anlaşılır ret.
6. "Çıktı al" akışı: "Kâğıda yazdır (A4)" ya da "Kesim için dosya"; yazınca PDF'i ya da klasörü aç; uzun teknik onay "Ayrıntılar" altına.
7. Rehberin devamı: ad ver, dikiş payı, çıktı al.
8. Doğrulanmamış araçlara "Deneysel" etiketi; makine simülatörü ve kalıp isteği basit görünümden gizlenir.
9. Açılıp denenebilecek 2–3 örnek kalıp.

### Aşama 4: Paketleme ve yayın hazırlığı (yaklaşık 1 hafta + mağaza onay süresi)
- Sürüm numarası düzeni; okunur dosya adları; değişiklik günlüğü.
- K3'e göre Store paketi (MSIX) ya da imzalı yükleyici; `.tcad` dosyasına çift tıklayınca açılma; Başlat menüsü; kaldırma.
- Paketlenmiş derlemeyi CI'da açıp doğrulayan duman testi.
- Lisans/kullanım koşulları (K2), gizlilik ve KVKK metni, üçüncü taraf bildirimleri.
- Türkçe indirme ve destek sayfası; yazdırılabilir kısa başlangıç kılavuzu; "Destek dosyası hazırla" düğmesi.
- En düşük sistem gereksinimi: eski bir bilgisayarda ölçülerek yazılır.

### Aşama 5: Gerçek deneme (sizin takviminiz)
- Elle deneme betiğini siz yürütürsünüz.
- 3–5 kişi, yardım almadan 5 görev; nerede takıldıkları not edilir.
- Bulunanlar Aşama 3 kurallarıyla düzeltilir; kapı yeniden çalışır.

### Aşama 6: İlk yayın
- "Windows önizleme" etiketiyle; önce kapalı küçük grup, sonra herkese açık.

### Aşama 7: iPhone (ayrı hat, Windows'tan sonra)
- Gerçek cihazda deneme (sizde iPhone gerekir).
- iPhone ile Windows arasında dosyanın baş aşağı görünüp görünmediğini asimetrik bir örnekle doğrula (koddan şüphe var, kanıt yok).
- Gizlilik ve destek adresleri, App Store formları (yalnız siz), mağaza ekran görüntüleri.
- iPhone CI'ı hızlandır (bugün 40–60 dakika).

### İlk sürümden sonraya bırakılanlar
Kapalı eğriler (kol evi, yaka), çentik çizme aracı, tam AAMA, kesici/plotter çıktısı, ölçüden hazır kalıp blokları, lisans anahtarı ve ödeme, uygulama içi ses tanıma, Yüksek Karşıtlık ve ekran okuyucu tam desteği, otomatik güncelleme.

## 5. Doğrulanmamış iddialar (plan bunlara dayanıyorsa önce doğrulanır)

- Win+H'nin bu programın kutusuna Türkçe yazdığı hiç denenmedi.
- Store'un imzasız-engel sorununu çözdüğü ve paketin veri klasörlerini bozmadığı denenmedi.
- Türkiye'den bireysel geliştirici hesabı ve sertifika seçeneklerinin uygunluğu ve fiyatı doğrulanmadı.
- “Seçili parçayı A4 PDF çıkar…” seçili parçayı yazar. Dışa aktarım penceresinde “PDF A4 sayfalar” bütün projeyi yazar. Gerçek yazıcıda ölçek için Aşama 2’deki 50 mm ölçümü açık.
- Dikiş payının gerçek, eğri kenarlı parçalarda çalışıp çalışmadığı yalnız belgeden biliniyor.
- iPhone–Windows dosya yönü uyumsuzluğu yalnız koddan okundu.
- CI'ın gerçek bir pencereyi sürüp kaydet penceresini doldurabileceği denenmedi; olmazsa etkileşimli kanıt yalnız sizin elle denemenizdir.
- Gerçek iPhone’da kurulum ve testçi grubuna dağıtım doğrulanmayı bekliyor. Apple, 1.03 (14) derlemesini 16 Kasım’da kabul etti. [TestFlight yükleme kaydı](https://github.com/berkdemirokk/Mono-Di-Tree-CAD/actions/runs/43797711367)


## 6. Komutla kullanım ve profesyonel ürün sırası (28 Kasım 2026)

Sahibin yeni hedefi: temel işlemler Türkçe yazılı komutla, mümkün olduğunda sesli dikteyle yapılabilsin. İlk yayın kapıları gevşetilmez; Windows önceliği, ücretsiz önizleme, tek gövde, tek iş, WearCAD salt-okunur kuralı ve sahip onayıyla birleştirme korunur.

**Ortak kabul sözleşmesi:** Her komut yardım kataloğunda örneklenir; Türkçe yazım, ondalık virgül, cm/mm ve belirsiz hedefler test edilir. Önizleme kaynak belgeyi değiştirmez; açık onay aynı planı en fazla bir kez uygular; düzenleme tek geri alma adımıdır. Kilitli katman ve üretim metaverisi sessizce değiştirilmez. Desteklenmeyen istek gerekçeli reddedilir. Dosya ve makine işlemlerinde hedef ve sonuç ayrıca doğrulanır; gerçek makineye otomatik iş gönderilmez. Çok adımlı komutlar ayrı güvenlik ve işlem bütünlüğü testleri tamamlanana kadar reddedilir.

### Şu anki tek iş — Aşama 3.4

- İlk kapsam: üretim metaverisi olmayan geçerli kapalı doğrusal çokgen. Analitik eğri, pens, çentik, iplik yönü ve serileme dönüşümleri ayrı iş olarak kalır.
- Kabul: `yamuk alt 60 cm üst 40 cm yükseklik 50 cm`, ardından `30 x 25 cm yap`. 600 × 500 mm kontur 300 × 250 mm olur; sol üst sınır, kimlik, kapanış ve nokta sırası korunur.
- Test önce: ana pencerenin kendi Ctrl+K komut kutusu, önizleme/onay, tam koordinat karşılaştırması, Ctrl+Z ve Ctrl+Y. Penceresiz çalışma fiziksel klavye ve operatör kabulü değildir.
- Mevcut `PlanResize` yalnız `ResizeRectangle` çağırıyor; ürün düzeltmesi olmadan kabulün geçmesi beklenmiyor. Mevcut dikdörtgen ve metaveri koruma kontrolleri zayıflatılmaz.
- Ürün kodu ayrı geliştirme oturumunda yazılır; bağımsız inceleme ve CI gerekir.

### İlk sürümden sonraki sıra

| İş | Hedef komut örneği (mevcut destek iddiası değil) | Kabul kanıtı |
|---|---|---|
| Kapalı analitik kontur ve düğüm düzenleme | Kol evini eğriyle çiz | Segment türü, uçlar ve tolerans korunur |
| Pens, çentik ve eğrisel dikiş payı | 1 cm dikiş payı ekle | Gerçek kalıp, işaret korunumu, dikim provası |
| AAMA/ASTM DXF aktarımı | Kalıbı DXF olarak dışa aktar | Bağımsız CAD ile ölçü, beden ve işaret round-trip |
| Kısıtlı poligon pastal, yön ve maliyet | 150 cm kumaşa iki ön, bir arka yerleştir | Çakışma ve yön kontrolü; mevcut raf motoruna karşı ölçüm |
| Ölçüden parametrik kalıp ve çok adımlı komut | Ölçülerimle etek oluştur, 36–44 serile | Bağımsız kalıp referansı ve gerçek dikim |
| Fotoğraftan dijital kalıp | Fotoğraftaki kalıbı tara | Kalibrasyon ve bağımsız cetvel ölçümü |
| iPhone/iPad düzenleme ve Apple Pencil | Kol parçasını 2 cm uzat | Gerçek cihaz ve Windows ile dosya turu |
| 3D önizleme ve prova | Elbiseyi mankende göster | Bağımsız dikilmiş örnekle karşılaştırma |

Ücretli API zorunluluğu getirilmez. Açık kaynak bileşenlerin lisansı ve güvenliği kullanımdan önce değerlendirilir. Doğal dil katmanı geometri uydurmaz; doğrulanmış komutları deterministik geometri motoru yürütür. Gerçek veri, cihaz ve fiziksel çıktı olmadan ticari üretime hazır iddiası yapılmaz.
