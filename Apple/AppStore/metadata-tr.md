# App Store metin taslağı — Türkçe (birincil dil önerisi)

Durum: **taslak**. Kullanıcı gözden geçirmeden App Store Connect'e girilmemelidir.
Uygulama arayüzü yalnız Türkçe olduğu için App Store Connect'te birincil dil olarak **Türkçe** önerilir.

Açıklamadaki her özellik `Apple/TekstilCADViewer/*.swift` ve `Apple/README.md` ile karşılaştırıldı (16 Kasım 2026, `ios/gap-store` dalı). Kodda olmayan bir şey yazılmadı. Yeni özellik eklenir ya da biri kaldırılırsa bu metin de güncellenmelidir.

Karakter sayıları `count.py` benzeri bir betikle Unicode karakter olarak sayıldı (App Store Connect de böyle sayar). Kod blokları tam olarak yapıştırılacak metindir.

### Uygulama adı (16/30)

```text
Mono Di Tree CAD
```

### Alt başlık (30/30)

```text
Basit kalıp çizimi, mm ölçüyle
```

### Tanıtım metni (145/170)

Tanıtım metni sürüm göndermeden değiştirilebilir; arama dizinine girmez.

```text
Dikdörtgen, çizgi ya da parmakla dokunarak şekil ekle; ölçüleri milimetreyle yaz. Hesap yok, internet yok: projelerin yalnız bu cihazda saklanır.
```

### Açıklama (1980/4000)

```text
Mono Di Tree CAD, basit tekstil kalıp parçalarını telefonda ya da tablette hızlıca çizmek için yapılmış küçük bir çizim atölyesidir. Bir şekil ekle, ölçüsünü milimetre olarak yaz, projeni kaydet ve istersen dosya olarak paylaş.

ÇİZ
• Dikdörtgen ya da çizgi ekle.
• Parmakla şekil çiz: 300 × 300 mm'lik alanda köşelere sırayla dokun; şekli kapalı ya da açık bırak. İstersen noktalar 5 mm'lik adımlara oturur.
• Hazır iki parçalı örnekle uygulamayı hemen dene.

DÜZENLE
• Dikdörtgenin enini ve boyunu, çizginin uzunluğunu, parçanın X/Y konumunu ve adını milimetre cinsinden yaz.
• Köşe noktalarının koordinatlarını tek tek düzenle.
• Parçayı sürükleyerek taşı (isteğe bağlı 5 mm adım) ya da 10 mm'lik düğmelerle sağa, sola, yukarı, aşağı kaydır.
• 90 derece döndür, kopyala, sil.
• Son 50 değişikliği geri al ya da ileri al.

GÖR
• İki parmakla ya da düğmelerle 24 kata kadar yakınlaştır, kaydır, tek dokunuşla ekrana sığdır.
• Seçili parçanın eni ve boyu çizimin üstünde milimetre olarak yazılır.

KAYDET VE PAYLAŞ
• Çalışman otomatik olarak cihazda taslak olarak saklanır; ana ekrandan "Son çizime devam et" ile döndüğün yerden sürdür.
• Projelerini cihazdaki listede sakla, adını değiştir ya da sil.
• Çizimi .tcad dosyası olarak hazırla; Paylaş menüsünden Dosyalar'a kaydet, AirDrop ile gönder.

DOSYA AÇ
• Dosyalar uygulamasından (iCloud Drive dahil) .tcad proje dosyası açıp parça konturlarını salt okunur önizle. Kaynak dosya hiçbir zaman değiştirilmez.
• Bu uygulamada çizilmiş basit dosyaların kopyasını oluşturup düzenleyebilirsin.

GİZLİLİK
Hesap gerekmez, internet bağlantısı kullanılmaz, reklam ve izleme yoktur. Çizimlerin yalnız senin cihazında durur; sen paylaşmadıkça hiçbir yere gönderilmez.

BİLMEN GEREKENLER
• Arayüz Türkçedir.
• Bu uygulama basit kontur çizimi içindir. Dikiş payı, çentik, serileme (beden büyütme) ve pastal yerleştirme yoktur.
• Ekranda gösterilen ölçüler ve çizimler bilgi amaçlıdır; kesim ya da üretim öncesinde ölçüleri ayrıca kontrol et.
```

### Anahtar kelimeler (87/100)

Virgülden sonra boşluk yok. UTF-8 olarak 90 bayt; alan bayt sayarsa da sınırın altında. Ad ve alt başlıktaki kelimeler (mono, di, tree, cad, basit, kalıp, çizimi, mm, ölçüyle) tekrar yazılmadı; Apple onları zaten dizine alır. "pastal" bilerek yazılmadı: uygulamada pastal yok.

```text
patron,dikiş,terzi,modelist,moda,tekstil,konfeksiyon,kumaş,giyim,tasarım,milimetre,tcad
```

### Kategori önerisi

- Birincil: **Grafik ve Tasarım** (Graphics & Design). `project.yml` içindeki `LSApplicationCategoryType: public.app-category.graphics-design` ile aynıdır.
- İkincil (isteğe bağlı): **Verimlilik** (Productivity).

### Yaş derecelendirmesi yanıtları

App Store Connect'in güncel yaş derecelendirme anketi (2025'te yenilendi; 4+, 9+, 13+, 16+, 18+). Kod incelemesine göre yanıtlar:

| Soru grubu | Yanıt | Gerekçe |
|---|---|---|
| Uygulama içi denetimler (ebeveyn denetimi, yaş doğrulama) | Hayır / Yok | Böyle bir özellik yok. |
| Yetenekler: sınırsız web erişimi | Hayır | Uygulamada tarayıcı ya da web görünümü yok. |
| Yetenekler: kullanıcı tarafından üretilen içerik, mesajlaşma, sohbet | Hayır | Kullanıcılar arasında içerik paylaşımı yok. Paylaş menüsü yalnız kullanıcının kendi dosyasını sistemin paylaşım ekranına verir. |
| Yetenekler: reklam | Hayır | Reklam yok. |
| Tıbbi ya da sağlık bilgisi, sağlıklı yaşam konuları | Yok | — |
| Şiddet (gerçekçi ya da çizgi film), korku | Yok | — |
| Cinsellik, çıplaklık, olgun içerik | Yok | — |
| Küfür ya da kaba mizah | Yok | — |
| Alkol, tütün, uyuşturucu | Yok | — |
| Kumar, kumar benzetimi, yarışmalar, ödüllü çekilişler | Yok | — |
| Silahlar | Yok | — |

Beklenen sonuç: **4+**. Anketteki soru adları App Store Connect'te farklı görünebilir; kullanıcı anketi kendisi doldurmalıdır.

### Destek ve pazarlama adresleri (doldurulacak)

- Destek URL'si (zorunlu): `https://<doldurulacak>/destek` — kullanıcının açıp gerçekten erişilebilir tutacağı bir sayfa (ör. GitHub Pages ya da deponun herkese açık bir sayfası). Depo özel ise oraya bağlantı vermeyin.
- Pazarlama URL'si (isteğe bağlı): `https://<doldurulacak>`
- Gizlilik politikası URL'si (zorunlu): `privacy-policy.md` herkese açık bir adreste yayımlandıktan sonra o adres.
- Telif hakkı satırı: `2026 <hak sahibinin adı>` — kullanıcı yazacak.

### Bu sürümde neler var? — 1.03 (taslak)

Not: App Store'daki **ilk** sürümde bu alan istenmez. 1.03 App Store'daki ilk sürüm olacaksa bu metin yalnız TestFlight "Neyi test etmeli?" alanında kullanılabilir. Metin, `ios/gap-store` dalında kodda bulunan son değişikliklere göre yazıldı; kullanıcının en son dağıttığı derlemeye göre kısaltılmalıdır.

### Neler var? 1.03 (440/4000)

```text
• Çizimi iki parmakla ya da düğmelerle yakınlaştır ve kaydır; "Ekrana sığdır" ile geri dön.
• Seçili parçanın ölçüleri artık çizimin üstünde milimetre olarak yazıyor.
• Parçayı 10 mm yukarı ve aşağı da taşıyabilirsin.
• Son projeler listesinde bir projenin adını değiştirebilir ya da silebilirsin.
• Köşe noktası düzenleyicisi "Uygula" sonrası açık kalır ve sıradaki noktaya geçer.
• Kaydetme durumu ve hatalı ölçü uyarıları daha anlaşılır.
```
