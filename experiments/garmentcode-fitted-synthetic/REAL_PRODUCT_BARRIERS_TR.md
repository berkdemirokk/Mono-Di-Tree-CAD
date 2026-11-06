# Gerçek elbise kalıbına ilerleme: sentetik dikiş aralığı ve engel

Bu belge yalnız sabit kolsuz `FittedShirt + PencilSkirt` araştırma yolundaki iki sentetik fixture içindir. Kaynak insan ölçülerinden hazır giysi üretildiği veya bu üst–etek yolunun her elbise modelini kapsadığı iddia edilmez. Araştırma sonucu her koşulda `unresolved`, `Accepted=false`; gerçek ölçü, kalıp çıktısı, kompozisyon, üretim ve plotter kapalıdır.

## En büyük teknik engel

Burada en büyük **ürüne geçiş engeli**, kaynakta tarif edilen 20 ölçünün insan üzerinde tekrarlanabilir alınması ile bu ölçülerden üretilen tüm panel, dikiş, çentik ve kumaş davranışının gerçek bir bütün olarak bağımsız doğrulanması arasındaki kanıt zincirinin eksikliğidir. [Ölçüm kanıt matrisi](MEASUREMENT_EVIDENCE_TR.md) işaret noktaları, yol, duruş ve 3B/mezura eşdeğerliğinde kalan boşlukları gösterir. Alanların dolu ve teknik aralıkta olması, anatomik doğruluk veya fiziksel uyum kanıtı değildir. Bu araştırma gerçek kişi ölçüsü alan bir ürün giriş yolu açmaz.

İki sentetik bedenin kaynak dikiş kimliği ve büzgü bölümleri fixture ile karşılaştırılır; değişen altı panelin temsil edilen topoloji kanıtı ve sayısal dikiş uzunlukları araştırılır. Ancak kaynak optimizerinin tüm etkileri, gerçek çentikler, dikiş sıralaması/üç boyutlu yerleşim, kumaşın esnemesi ve rahatlık, fiziksel prova ile tüm beden ve tasarım kapsamı doğrulanmamıştır. Ön omuz eğrisinin yaklaşık 6,7–6,9 mm sapması (önceki omuz uzunluğunun yaklaşık %10’u), sayısal eşleşmenin şekil uygunluğu anlamına gelmediğini özellikle gösterir. Kaynakla ilişkilerin eşleşmesi, değiştirilmiş geometrinin kaynak optimizerinden geldiğini veya fiziksel olarak dikilebilir olduğunu kanıtlamaz.

## 23 kuralda yeni sayısal tanı

`SeamNumericalStudy`, önceden hesaplanmış projeksiyon uzunluğu aralıkları için üç sonuç döndürür. Kaynak bağlantısını kendisi doğrulamaz; bu deneyde çağıran [CombinedGraphStudy](CombinedGraphStudy.cs), iki fixture'ın 23 kaynak kaydını ve araştırma konturlarını ayrıca denetler. Büzgü katsayıları dahil her tarafın temsil edilen aralığı tam tamsayı/kesir ile karşılaştırılır. Kullanılan `0.0001 cm` yalnızca açıkça seçilmiş **sayısal araştırma bandıdır**; fiziksel dikiş toleransı değildir ve kayan nokta `double` olarak temsil edilen değerdir.

- `Within`: İki aralıktaki herhangi iki gerçek temsil değeri arasındaki en büyük olası fark bu bandın içinde veya sınırındadır.
- `Outside`: İki aralık arasındaki en küçük olası fark banttan kesinlikle büyüktür.
- `Unresolved`: Aralıklar ve bant, önceki iki karardan birini kanıtlamaz. Birbiriyle örtüşen aralıklar otomatik başarı sayılmaz.

| İki fixture’ın her biri | Sayısal bant içinde | Bant dışında | Belirsiz |
|---|---:|---:|---:|
| Eğriler için alt bölme derinliği 6 | 21 | 0 | 2 |
| Eğriler için alt bölme derinliği 9 | 23 | 0 | 0 |

Derinlik 6'da belirsiz iki ilişki `BodiceHalf/left/1` ve `BodiceHalf/right/1` yan dikişleridir. Daha sıkı dyadik eğri aralıklarında her iki fixture’da da 23 ilişkinin **tüm olası temsil değerleri** seçilmiş sayısal bandın içine girer. Bu, tüm dikişlerin matematiksel olarak eşit olduğunu veya bu bandın kumaş üzerinde kabul edilebilir olduğunu göstermez. Önceki 8/6 tam ayrık pens aralığıyla çelişmez: bunlar sıfırdan farklı fakat bu araştırma bandından küçük boşluklardır. Doğrusal kenarlar tam dyadik uçlardan doğrudan sınırlanır; alt bölme eğrilerde uygulanır. Önceki kayan nokta işlemleri, kaynak optimizeri ve fiziksel ölçüm hatası bu aralıkların dışındadır.

Kenar sayısı ve kimliği, kaynak belgesiyle eşleme, ayrık pens araştırması ve 23 kuralın sayısal değerleri [birleşik aday raporunda](SHOULDER_GRAPH_RESEARCH_TR.md), [kaynak pens izinde](SKIRT_DART_ROUNDING_TR.md) ve [pens farkı karşılaştırmasında](DART_RESIDUAL_SOURCE_TR.md) ayrıntılıdır. Bu kontrol aralık analizi olarak başarılı olsa bile sonuç nesnesi `Accepted=false` döndürür ve kompozisyonu reddeder.

## Sonraki araştırma adımı ve sınamalar

Ölçüm noktaları ve yolları, aynı kişide tekrarlanabilirlik ve yöntem farkları için kaynak tanımlarla çelişmeyen yazılı bir protokolün alan bazında hazırlanması; ardından değişmiş konturların tüm dikiş uç/sıra/yön/büzgü ilişkilerine ve gerçek çentik verisine bağlanması gerekir. Bunlardan sonra farklı bedenlerde bağımsız fiziksel prova ve kumaş özellikleri için ayrı kanıt gereklidir. Bu adımlar henüz yapılmadı; burada gerçek kişilerden veri toplanmaz ve ölçü tahmin edilmez.

Otomatik doğrulama iki fixture'da yukarıdaki 21/2 → 23/0 sayılarını ve belirsiz kuralların kimliğini sınar. Ek sentetik testler bant sınırında eşitlik, kesin dışarılık, aralık örtüşmesine rağmen belirsizlik, bozuk aralık/bant, eksik/tekrar kural, geçersiz alt bölme ve üretim/kompozisyon kapılarını kontrol eder. Bu testler fiziksel dikilebilirlik denemesi değildir.
