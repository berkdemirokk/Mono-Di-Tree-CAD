# Sentetik dikiş farklarının kaynak araştırması

Bu analiz sabitlenmiş GarmentCode `d449629979028123a5c4dc9e732a2ec19b7fce31`, seçili **kolsuz FittedShirt + PencilSkirt** tasarımı ve beş yapay beden varyasyonuna aittir. Ölçüler gerçek müşteri verisi değildir. Kaynak hesapları, C# yaklaşık optimizeri ve kumaş davranışı birbirinden ayrıdır. Buradaki gözlemler fiziksel prova veya üretim toleransı değildir.

## Kaynakta doğrudan görülenler

- [Ön/arka gövde kaynağı](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/garment_programs/bodice.py) ön ve arka gövdeyi ayrı formüllerle kurar; `add_sleeves` kol evi köşelerini, ardından `add_collars` yaka köşelerini keser. Omuz ve yan bağlantıları bu işlemlerden sonra kurulur. Ön yanın `side_len` ayarına ilişkin kaynak yorumu kol uyumunu amaçlar; bu, ortaya çıkan iki nihai dikiş uzunluğunun eşitliğini kanıtlamaz.
- Kaynak `PencilSkirt`, arka kalça hattı uzantısında `hipline_ext=1.05` kullanır ve bu bölümün arka yan arayüzüne katsayı olarak uygular. [Arayüz kodu](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/interface.py), izdüşüm uzunluğunda ilgili bölümün uzunluğunu katsayıya böler. Bu, kodda **açıkça belirtilmiş** bir düzenlemedir; gerçek kumaşın nasıl davranacağını veya oranın üretimde uygunluğunu kanıtlamaz.
- Seçili sentetik örneklerde omuz, gövde yanı ve üst–etek bel bağlantılarının kaydedilen büzgü katsayıları `1`’dir. Kaynak, bu farklar için ayrıca büzgü/rahatlık niyeti belirtmiyor. Bundan yazarın hiçbir rahatlık istemediği sonucu çıkmaz; fiziksel ürün şartnamesi olmadan bu niyet bilinmiyor.
- [Bağlayıcı](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/connector.py), izdüşüm uzunluklarının toplamını eşitlemek yerine kenar paylarını ve bölünme oranlarını eşleştirir. Toplam farkı yalnız belirli koşul ve `verbose` altında uyarı olarak ele alınır. Kaynak uyarı sayısı, giysi üretim toleransı değildir.

## Bel farkının ölçülen ayrışımı

Sabitlenmiş kaynak aşamalarında ön yarım bel `(<bel> − <arka bel>)/2`, etek izdüşüm beli toplam beden beli olarak gözlendi. İki yarımın arka bel arayüzünün her birinde, kaynak eğrisi ve iki pens kesimi sonrasında hedef `<arka bel>/2` değerinden fazla uzunluk kalıyor. Bu örneklerde tam bel farkı, iki arka yarımın bu fazlasına eşit; kol evi ve yaka kesimleri ölçülen bel arayüzünü değiştirmiyor.

| Sentetik örnek | Bir arka yarımın bel fazlası | Bunun eğri uzunluğu − yatay uç farkı bölümü | Yatay uç farkının hedef arka yarıma göre kalan fazlası | Üst − alt toplam bel |
|---|---:|---:|---:|---:|
| temel | 1,7855 mm | 1,5342 mm | 0,2514 mm | 3,5711 mm |
| farklı oran | 1,5779 mm | 1,3692 mm | 0,2087 mm | 3,1557 mm |

Bu ayrışım kaydedilen üç kaynak Bézier kenarının uzunlukları ve uçlarının yatay farklarından hesaplanır; yalnız bu iki fixture için sayısal kimliktir. Fazlanın hangi bölümünün kaynak optimizeri, pens yerleşimi veya tasarım niyeti açısından **doğru olması gerektiği** bu verilerden çıkarılamaz. Arka eğri kontrolünün kaynak BFGS sonucu C# adayından da farklı olabilir.

## Omuz ve yan aşamaları

Temel sentetik sağ yarımda kaynak ön−arka omuz uzunluğu farkı, kol evi kesiminden önce yaklaşık **−64,712 mm**, kol evi kesiminden sonra **−1,794 mm** oldu; yaka kesiminden sonraki nihai fark yaklaşık **−1,794 mm** kaldı. Ön−arka yan farkı aynı aşamalarda yaklaşık **+38,215 mm**’den **+3,686 mm**’ye indi; izlenen yaka kesimi yan arayüzü değiştirmedi. Kesim öncesi değerler nihai dikiş uzunluğu değildir. Bu iz, bağımsız kesim geometrisinin nihai farkın ortaya çıkmasında rol oynadığını gösterir; kasıtlı rahatlık mı, eksik uyum kuralı mı olduğunu tek başına çözmez.

Aynı sentetik bedenin yalnız omuz açısı 18°, 22° ve 30° alındığında kaynak üst−alt bel farkları sırasıyla yaklaşık **2,313**, **3,571** ve **7,300 mm** oldu. Başka bir sentetik karşılaştırmada ön uzunluğun 1 cm değiştirilmesi bu kaydedilen bel toplamını değiştirmedi. Bunlar örnekleme bulgularıdır; bütün bedenler için monotonluk veya duyarsızlık garantisi değildir.

## İlişkiler için mühendislik yorumu

Kaynakta bir katsayı veya açık tasarım talimatı olmayan bağlantılarda omuz, yan ve üst–etek bel taraflarının istenen ilişkisinin (eşitlik mi, ölçüye/kumaşa göre tanımlı fark mı) ürün ve kalıp uzmanı tarafından açıkça belirlenmesi gerekir. Pens bacakları da eşleşme açısından ayrı değerlendirilmelidir. Bunlar **önerilen doğrulama hedefleri**, kaynak kodun fiziksel geçerlilik garantisi değildir. Arka etek yanındaki 1,05 ise kaynakta açıkça işaretlenmiş bölüm katsayısıdır; bunun gerçek dikimde uygulanabilirliği ayrıca prova gerektirir.

Ölçüye bağlı bir çözüm araştırılabilir: seçili hedef ilişki ve gerekirse açık rahatlık parametresi tanımlandıktan sonra arka bel eğrisi, pensler ve ilgili yan/omuz kesimleri birlikte yeniden hesaplanıp bağlantılar tekrar sınanabilir. Bu bir çözümün her bedende var olduğunu, kaynak biçimini veya uyumu koruyacağını kanıtlamaz. Ruffle katsayısını gizlice değiştirmek, bir kenarı tek başına ölçeklemek veya farkı sayısal toleransla yok saymak fiziksel geçerlilik sağlamaz. Algoritma, hedef ve fizibilite kanıtı bu çalışmada henüz uygulanmadı.

## Tekrar üretim ve sınır

`capture_seam_causality.py` mevcut sentetik kaynak üreticisinin commit/dosya kontrolünden sonra beş sabit örneği bellekte hesaplar; gövdenin sağ yarısında kol evi ve yaka öncesi/sonrası arayüzleri ve eteğin bel/yan izdüşümlerini kaydeder. `seam-causality.json` kaynak commit’i, NumPy/SciPy sürümleri ve yerel SHA-256 ile testte sabitlenmiştir; bağımsız imza değildir. Kaynak `back-waist-stages.json` ile bel ayrışımı ve iki mevcut kaynak grafiğindeki omuz/yan farkları regresyonda kontrol edilir. Yalnız Python soket çağrıları engellenmiştir; bu, işletim sistemi düzeyinde ağ izolasyonu iddiası değildir.

Tüm sonuçlar `Unverified`/`unresolved`; gerçek ölçüm protokolü, kumaş ve beden provası doğrulanmadı. Üretim, dışa aktarma ve plotter kapalıdır.
