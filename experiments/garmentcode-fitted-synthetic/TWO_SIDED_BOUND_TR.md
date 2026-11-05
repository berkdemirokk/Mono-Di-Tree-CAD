# İki taraflı küçük uç değişimi: sentetik fizibilite

Bu çalışma, seçili sabit kolsuz üst/kalem etek tasarımında dört sentetik C# bedeni ve ilk ikisinin Python kaynak fixture’ları içindir. Gerçek ölçü, kumaş veya prova kullanılmadı. Ön ve arka bel–yan uçlarının **her biri için ±5 mm yatay değişim** yalnız açık bir araştırma varsayımıdır; fiziksel olarak makul veya güvenli olduğu doğrulanmış bir sınır değildir. Dikişte tam uzunluk eşitliği ve katsayı `1` hedeflenir; fiziksel rahatlık payı belirlenmiş değildir.

## İncelenen değişiklik ailesi

Sağ ön ve sağ arka panelin beldeki yan uçları bağımsız olarak yatay hareket edebilir. Her panelde bu uç, yan dikişin bir doğrusal kenarının bir ucudur; diğer yan dikiş kenarı, pensler, kol evi ve diğer noktalar sabit varsayılır. Hareket eden uçlara bağlı bel kenarları değişmek zorundadır; bu aile dışında ara noktaların büyük hareketi, eğri eklenmesi, penslerin değişmesi, rahatlık katsayısı veya başka tasarım alternatifleri **incelenmemiştir**.

`TwoSidedEndpointStudy`, önce yalnız yan uzunluğunun dahi eşitlenip eşitlenemediğini araştırır. Bir doğrusal kenarın başlangıç yatay farkı `x`, düşey farkı `y`, izin verilen yatay hareket büyüklüğü `M` ise uzunluğu şu aralıkla sınırlanır:

`√(max(0, |x| − M)² + y²) ≤ uzunluk ≤ √((|x| + M)² + y²)`

Diğer sabit yan kenarın uzunluğu da aralığa eklenir. Ön ve arka için bulunan aralıklar ayrık ise bu **özel değişiklik ailesinde** yan dikişler eşitlenemez; dolayısıyla bel ve yan hedeflerini birlikte sağlayan aday da yoktur. Bu, bel hedefinin tek başına imkânsızlığı veya farklı değişiklik aileleri için genel imkânsızlık değildir. Aralıklar temsil edilen binary64 koordinatlar için tam dyadic tamsayı ve aşağı/yukarı karekök sınırlarıyla hesaplanır; ekrandaki ondalıklar yaklaşık gösterimdir.

## Sonuçlar ve sapma alt sınırı

C# adaylarında her iki uç için ±5 mm varsayımında aşağıdaki yan uzunluğu aralıkları birbirinden ayrık kaldı:

| Sentetik örnek | Ön yan aralığı, cm | Arka yan aralığı, cm | Yan eşitliği için azami tek uç hareketine kanıtlı alt sınır |
|---|---:|---:|---:|
| temel | 19,1698–19,2623 | 18,8127–18,8823 | > 21,47 mm |
| farklı oran | 21,2699–21,4013 | 20,8059–20,8518 | > 27,98 mm |
| üçüncü | 20,1477–20,2362 | 19,7333–19,7997 | > 24,85 mm |
| beşinci | 20,1324–20,2639 | 19,6733–19,7293 | > 26,25 mm |

Son sütun, aralıklar ayrık kaldığı sürece `M` artırılarak elde edilmiş **alt sınırdır**: bu sınırlı ailede eşit yan uzunluğu için `max(|ön kayma|, |arka kayma|)` en az bu miktarı aşmalıdır. `|ön kayma| + |arka kayma|` da aynı alt sınırı aşar; bu, toplam veya azami sapmanın gerçek minimumunun bulunduğu ya da bu sınırın üzerinde çözüm bulunduğu anlamına gelmez. Ön/arka bel hedefi de eklenirse gerekli değişim bu kanıttan daha küçük olamaz. Tam eşitlik ve yalnız yatay uç hareketi varsayımları burada belirleyicidir.

İki orijinal Python kaynak fixture’ının ön/arka doğrusal yanları, büzgü katsayısı `1` ve bölüm kapsamı doğrulanarak aynı ±5 mm hesaplamasıyla ayrıca sınandı; ikisinde de aralıklar ayrık bulundu. C# ve Python sınırları, kaynak optimizerinin farklılığı nedeniyle ayrı sayılardır.

Kaynakta `right` / `BodiceHalf` / `1` dikişinin sıralı kenarları ön için `right_ftorso` 7 ve 4, arka için `right_btorso` 7 ve 8 olarak doğrulanır; her kenarda `flipped` ve `right_wrong` değerleri `false` olmalıdır. Ölçüm aynı referansları kullanır. Her iki tarafta yanlış panel, kenar, sıra ve yön alanları negatif regresyonlarla reddedilir; bu güvence yalnız bu fixture’lara ilişkindir.

## Önce/sonra, topoloji ve komşu kenarlar

Önceki C# sağ ön ve arka konturları dört sentetik örnekte kapanış ve temsil edilen eğri çifti denetiminden geçti. Başlangıç yarım arka bel uzunlukları sırasıyla yaklaşık 18,1785; 17,1577; 19,1200; 17,5750 cm, hedefleri 18; 17; 19; 17,5 cm idi. ±5 mm ailesinde yan eşitliği zaten dışlandığından **sonrası için aday üretilmedi**; son uzunluk veya topoloji uydurulmaz. Bel yan uçları gerçekten taşınsaydı komşu bel kenarları da değişecekti ve yeni konturlar/dikiş grafiği ayrıca doğrulanacaktı.

Eşik eşitliği/ayrıklığı, negatif veya sonlu olmayan sınır, bilinmeyen ve kaynak optimizer uyuşmazlığı bilinen 30° örneği, boş/eksik/kapsam dışı kaynak büzgü bölümleri negatif testlerle kontrol edilir. Aralıklar örtüşürse yöntem başarı bildirmez; uygulanmış sertifikalı optimizer olmadığı için açık hata verir. Gerçek dikişteki rahatlık, kaynak optimizeri, ölçüm protokolü ve beden provası hâlâ çözülmedi. Tüm sonuçlar `Unverified`/`unresolved`, kompozisyon, üretim ve plotter kapalıdır.
