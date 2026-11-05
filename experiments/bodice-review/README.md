# Geliştirici araştırması: üst beden görüntüsü

Bu klasör, uygulamadan bağımsız, yalnız `127.0.0.1` üzerinde geçici bir Node sunucusu ve tarayıcı görünümüdür. Ana CAD programına eklenmemiştir. Açık birim, 15 ölçü ve kullanıcı onayı gereklidir. Sabit seçenekler: ön birincil göğüs pensi 06:00, ikincil pens yok, arka omuz ve bel pensleri kapalı. Motorun diğer seçenekleri deney değerleridir; sayfada tamamı görülebilir ve kişisel uyum önerisi değildir. Santimetre/mm çevrimi dışında ölçü türetilmez; omuz eğimi derecedir. Bu, etek veya tam elbise değildir.

Her alanın Türkçe ölçüm açıklaması mevcut araştırma ölçüm kapısının tanımına dayanır; sahada ölçüm veya uzman doğrulaması yapılmamıştır. Kalça kemiği referansı ile en geniş kalça çevresi hizası farklı olabilir, omuz eğiminin ölçülüşü de bilinmiyorsa uzmanla teyit edilmelidir. İstek metni ve geçmişiyle güvenilir alan bağı kurulmadığından hiçbir ölçü önerilmez veya önceden doldurulmaz. On beş değer, birim ve onay başlangıçta boştur; her değişiklik önceki görüntüyü ve onayı geçersiz kılar.

Ön ve arka Breanna `paths.seam` yollarındaki çizgi ve kübik eğri koordinatları yerel mm olarak alınır; canvas'a yerleştirme ve ölçek yalnız görüntü amaçlıdır. İşaret, dikiş payı, kumaş katı ve kesim semantiği aktarılmaz. Bilinen topoloji, nokta/işaret kimliği, bel–etek dikişi, kesim ve kullanım uygunluğu doğrulanmamıştır. Kaydetme, projeye aktarma, veri indirme, fiziksel çıktı, cihaz veya üretim işlevi yoktur; işletim sistemi ve tarayıcı ekran yakalama yetenekleri engellenemez. Ölçüler bellekte işlenir; uygulama bunları dosyaya yazmaz veya harici servise göndermez. Yerel Node sürecini yalnız güvenilir makinede kullanın.

## Başlatma (geliştirici ortamı)

Bu çalışma ağacının kökünde, Node 24 ile:

```powershell
& 'C:\Users\pc\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' experiments/bodice-review/server.mjs
```

Terminalde gösterilen `http://127.0.0.1:<port>/` adresini kullanın; bitince terminalde Ctrl+C. Sunucu pencereyi otomatik açmaz. `draft.mjs` ve onun yerel paketleri `C:\Projects\TekstilCAD\work\dress-research-c494587\experiments\breanna-4.10.2` içinden salt okunur yüklenir; bu çalışma ağacı bağımsız dağıtım paketi değildir. Çalışan kaynak/modül ve bütün transitif bağımlılıkların byte kimliği, lisans ve bildirimleri doğrulanmamıştır. Bu nedenle uygulamaya dağıtım veya üretim için hazır değildir.

## Test

`node --test experiments/bodice-review/server.test.mjs`: dört sentetik test; sabit seçenekle gerçek ön/arka kaynak eğrilerinin aynen korunması, cm/mm eşitliği, zorunlu ölçü ve geçersiz durumların reddi, localhost isteği/CSRF ve boyut sınırı ile export rotasının yokluğu. `ui-headless.test.mjs` ortam değişkenleri `BODICE_PLAYWRIGHT` ve `BODICE_EDGE` verilirse tarayıcıyı başsız açar; sentetik form, canvas ve değişiklikte sonucun silinmesini sınar. Bunlar uzman kalıp incelemesi, gerçek kullanıcı, ekran okuyucu, tüm ekran boyutları, fiziksel numune/prova veya gerçek baskı doğrulaması değildir. `work/evidence/bodice-review.png` yalnız sentetik başsız testin yerel çıktısıdır, git'e dahil değildir.
