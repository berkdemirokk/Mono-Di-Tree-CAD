# Sentetik pens kalıntıları: kaynak ve aday karşılaştırması

Bu çalışma yalnız `synthetic-base` ve `synthetic-distinct` kaydedilmiş kaynak fixture’ları ile aynı kimlikten oluşturulan birleşik C# araştırma adayını kapsar. Gerçek ölçü veya fiziksel prova yoktur. [Önceki aday incelemesindeki](COMBINED_CANDIDATE_REVIEW_TR.md) son 8/6 ayrık kuralın tamamı incelendi; başka bir kalıp türüne genellenmez.

## Doğrudan doğrulananlar

- Her kalan kural için kaynak `owner`, tip, indeks, taraflar, panel/kenar sırası, yön ve büzgü bölümleri `SyntheticFullSeamGraph.Compare` ile doğrulanır. İki kenar aynı panele aittir, birer doğrusal kenardır, katsayılar 1'dir ve kaynakta **tam bir ortak uçları** vardır. Yanlış ama geçerli kenar veya kaydırılmış/kaybolmuş ortak uç negatif testte reddedilir. Bu, aranan kaynak kaydıyla kenar bağını doğrular; fiziksel dikiş işlemini doğrulamaz.
- Kaydedilmiş kaynak JSON uç koordinatları binary64 değerleri olarak okunur; her çizginin uzunluğu bu temsil edilen uçlardan tam dyadik aralıkla yeniden hesaplanır. Aday için mevcut 23 kural projeksiyonunun aynı çiftleri kullanılır. Kalan 8/6 kuralın **her birinde hem kaynak hem aday tarafları kesin ayrık** bulundu. Bunun ölçüm girdisindeki bir hatadan kaynaklandığı gösterilmemiştir: inceleme iki sabit sentetik gövdeye ilişkindir ve önceki doğrulamada gövde değerleri kontrol edilir. Bu sonuç gerçek ölçüm protokolünü de doğrulamaz.
- Kaynak `EdgeSeqFactory.dart_shape` [simetrik ilk üçgen](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/edge_factory.py#L313-L331) kurar; sonraki [kenara kesme ve yerleştirme](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/operators.py#L228-L258) ve temsil işlemleri vardır. Bu formül tek başına kaydedilen bütün binary64 uçların tam simetrik olduğunu ispatlamaz.
- Özellikle arka etek `skirt_back:3↔4` ve `:13↔14` için taban uçlarının temsil edilen Y değerleri tam eşittir; ortak pens ucunun X değeri iki taban X değerinin **tam dyadik orta noktası değildir**. Yatay tabanlı ve ortak uçlu bu üçgende bu, temsil edilen iki bacak uzunluğunun farklı olmasının geometrik nedenini doğrudan açıklar. Ölçümün hatalı olduğu veya bu asimetrinin hangi Python işlemi/yuvarlama adımında oluştuğu buradan çıkarılamaz.
- Aynı arka etek örneklerinde kaynak fixture’ın iki `length` alanı binary64 olarak eşittir, fakat temsil edilen uçlardan tam aralıklarla hesaplanan iki uzunluk ayrıdır. Dolayısıyla yalnız yazılmış/kayan noktalı uzunluklara bakmak bu farkı gizler. Arka gövde penslerinin tabanları eğimlidir; yatay orta nokta açıklaması onlara aktarılmaz. C# aday ve kaynak farkları sayısal olarak da özdeş değildir; tek bir ortak hata mekanizması kanıtlanmış değildir.

| Kalan grup | Kaynak yaklaşık ayrık aralık boşluğu, cm | Birleşik C# adayında yaklaşık boşluk, cm |
|---|---:|---:|
| Base ön gövde, iki pens ilişkisi | 1,41220615×10⁻¹⁶ | 1,83602774×10⁻¹⁶ |
| Base arka etek, iki ilişki | 3,52179610×10⁻¹⁶ | 1,76089805×10⁻¹⁶ |
| Base arka gövde, dört ilişki | 4,40680742×10⁻¹⁷–2,41234786×10⁻¹⁶ | 4,32310378×10⁻¹⁷–1,68381926×10⁻¹⁶ |
| Distinct arka etek, iki ilişki | 3,76016926×10⁻¹⁶ | 1,88008463×10⁻¹⁶ |
| Distinct arka gövde, dört ilişki | 5,12589122×10⁻¹⁸–8,35974486×10⁻¹⁷ | 3,12836908×10⁻¹⁷–2,33688607×10⁻¹⁶ |

Gösterilen boşluklar yaklaşık değerlerdir. Ayrıklık hesaplaması tam tamsayı/kesir karşılaştırmasıdır; kayıtlı kaynak ve C# uçları dışındaki belirsizlikleri kapsamaz. Yanlış kenar eşlemesi bu kontrol edilen fixture kayıtlarında gözlenmedi; bu, her modelde veya gerçek bedende eşlemenin doğru olduğunu kanıtlamaz.

## Karar ve test sınırı

Geometriyi veya eşiği bu küçük farklılıklara dayanarak otomatik değiştirmek güvenli bir düzeltme olarak kanıtlanmadı. Araştırma dalına yalnız kaynak–aday karşılaştırması ve kanıta dayalı rapor eklendi; ayrı `disjoint` sonucu gizlenmedi. Yapay tam simetrik bir üçgende aynı inceleme, ayrıklık bulunmadığını ve yatay orta noktanın tam olduğunu doğrular. Yanlış kaynak kenarı ve ortak ucu kırılmış geometri de reddedilir. Bu testler yalnız izole tanılardır; kaynak doğrulamasını taklit eden değiştirilmiş test referansı gerçek kaynak olarak kabul edilmez.

Omuz ve bel–yan şekil değişimleri, ölçüm protokolü, gerçek çentikler, fiziksel prova ve kaynak optimizer eşdeğerliği açık kalır. Durum `unresolved`; gerçek ölçü, kalıp çıktısı, üretim ve plotter kapalıdır.

## Her kalan kuralın kaydı

`source gap` ve `candidate gap` cm cinsinden yaklaşık gösterimdir. `horizontal centered=not-horizontal`, taban Y değerlerinin tam eşit olmadığını ve yatay orta nokta testinin uygulanmadığını belirtir.

```text
synthetic-base dart audit BodiceFrontHalf/left_ftorso/1 edges=1/2: source gap≈1.41220615E-16 cm candidate gap≈1.83602774E-16 cm; source reported equal=True; horizontal centered=not-horizontal; exact disjoint both; research only.
synthetic-base dart audit BodiceFrontHalf/right_ftorso/1 edges=10/9: source gap≈1.41220615E-16 cm candidate gap≈1.83602774E-16 cm; source reported equal=True; horizontal centered=not-horizontal; exact disjoint both; research only.
synthetic-base dart audit FittedSkirtPanel/skirt_back/0 edges=3/4: source gap≈3.5217961E-16 cm candidate gap≈1.76089805E-16 cm; source reported equal=True; horizontal centered=False; exact disjoint both; research only.
synthetic-base dart audit FittedSkirtPanel/skirt_back/3 edges=13/14: source gap≈3.5217961E-16 cm candidate gap≈1.76089805E-16 cm; source reported equal=True; horizontal centered=False; exact disjoint both; research only.
synthetic-base dart audit BodiceBackHalf/left_btorso/0 edges=8/7: source gap≈2.41234786E-16 cm candidate gap≈1.68381926E-16 cm; source reported equal=False; horizontal centered=not-horizontal; exact disjoint both; research only.
synthetic-base dart audit BodiceBackHalf/left_btorso/1 edges=11/10: source gap≈4.40680742E-17 cm candidate gap≈4.32310378E-17 cm; source reported equal=True; horizontal centered=not-horizontal; exact disjoint both; research only.
synthetic-base dart audit BodiceBackHalf/right_btorso/0 edges=4/5: source gap≈2.41234786E-16 cm candidate gap≈1.68381926E-16 cm; source reported equal=False; horizontal centered=not-horizontal; exact disjoint both; research only.
synthetic-base dart audit BodiceBackHalf/right_btorso/1 edges=1/2: source gap≈4.40680742E-17 cm candidate gap≈4.32310378E-17 cm; source reported equal=True; horizontal centered=not-horizontal; exact disjoint both; research only.
synthetic-distinct dart audit FittedSkirtPanel/skirt_back/0 edges=3/4: source gap≈3.76016926E-16 cm candidate gap≈1.88008463E-16 cm; source reported equal=True; horizontal centered=False; exact disjoint both; research only.
synthetic-distinct dart audit FittedSkirtPanel/skirt_back/3 edges=13/14: source gap≈3.76016926E-16 cm candidate gap≈1.88008463E-16 cm; source reported equal=True; horizontal centered=False; exact disjoint both; research only.
synthetic-distinct dart audit BodiceBackHalf/left_btorso/0 edges=8/7: source gap≈8.35974486E-17 cm candidate gap≈2.33688607E-16 cm; source reported equal=True; horizontal centered=not-horizontal; exact disjoint both; research only.
synthetic-distinct dart audit BodiceBackHalf/left_btorso/1 edges=11/10: source gap≈5.12589122E-18 cm candidate gap≈3.12836908E-17 cm; source reported equal=False; horizontal centered=not-horizontal; exact disjoint both; research only.
synthetic-distinct dart audit BodiceBackHalf/right_btorso/0 edges=4/5: source gap≈8.35974486E-17 cm candidate gap≈2.33688607E-16 cm; source reported equal=True; horizontal centered=not-horizontal; exact disjoint both; research only.
synthetic-distinct dart audit BodiceBackHalf/right_btorso/1 edges=1/2: source gap≈5.12589122E-18 cm candidate gap≈3.12836908E-17 cm; source reported equal=False; horizontal centered=not-horizontal; exact disjoint both; research only.
```
