# Loblu dikiş payı: özellik ölçümünde somut engel

Bu deney üretim korumasını değiştirmez ve adayın imalata uygun olduğunu iddia etmez. Test, `SmoothAllowanceRegressionTests.Radial(512,true)` örneğini kullanıp 146. kenara `PatternTools.InsertNotch` ile açıkça seçilen bir V girinti ekler; 1 mm dış payın deneysel `OffsetClosedEvaluation` adayını inceler. Görsel `NotchMark` kullanılmaz; V gerçek kontur noktalarıdır.

| Kaynak V | Kontur yönü | Deneysel kapalı değerlendirme | Seçili V genişlik/depth |
| --- | --- | --- | --- |
| 6 mm ağız, 0,5 mm derinlik | iki yön | geçer; üretim onayı ve özellik sertifikası vermez | yaklaşık 5,83447515 mm / 0,48620551 mm |
| 4 mm ağız, 1 mm derinlik | iki yön | kaynak kenar ayrımı için yeterli marj olmadığı gerekçesiyle reddedilir | aday verilmez |

İlk örnekte ağız yaklaşık 0,165525 mm, derinlik 0,0137945 mm küçülür. Açıkça seçilmiş 0,000001 mm genişlik/derinlik değişimi toleransı reddeder; 2 mm toleransı bu seçili ölçüleri kabul eder. Bu toleranslar test örnekleridir, imalat için uygun varsayılanlar değildir. Ölçüm, kaynak ve adayda açıkça seçilmiş aynı indeksli üç noktayla sınırlıdır. Kaynak V'nin korunması için hangi değişimin kabul edilebilir olduğu ürün/kullanıcı sözleşmesinden gelmelidir; noktaların ve topolojinin korunması yeterli değildir.

Orijinal 512 noktalı loblu şeklin 50.000.000 mm ötelenmiş örneğinde `OffsetClosedEvaluation`, iki yönde de kaynak koordinatının üretim ızgarasına matematiksel en yakın nokta olduğunu gösteren kontrolde durur. Bu, bu aday değerlendirmesinin büyük koordinatta güvenli kabul sağladığını göstermediği anlamına gelir; mevcut üretim yolu değiştirilmemiştir.

Önceki 10x Clipper deneyi 8-grid örnek karşılaştırmasını bazı loblu girdilerde geçirir; fakat bir grid derinliğindeki gerçek girinti de 8-grid karşılaştırmasından geçerek kaybolabilir. Mevcut küçük/keskin girinti vetosu ayrı gereklidir. İndeksli miter adayı mevcut ileri sınır ve topoloji kanıtlarından geçebilse de yukarıdaki fiziksel boyut örneği özellik keşfi ve kabul ölçütlerinin eksik olduğunu gösterir. Mevcut korumayı aşan bir üretim geri dönüş yolu önerilmez.

Üretim incelemesi için en azından: kaynak–ızgara dönüşümüne uçtan uca fiziksel hata sınırı; tüm ilgili girinti/kanal ve diğer özelliklerin belirlenmesi veya belirsizlikte ret; kullanıcı/ürün tarafından tanımlanmış genişlik, derinlik ve kanal açıklığı toleranslarının aday geometride kontrolü; büyük koordinat, ızgaraya dönüş, yakın kenar ve çoklu kontur durumları; işaretli parçalar için ayrı sözleşme; mevcut tüm vetoların ve performans/iptal sınırlarının korunması gerekir. Bunların genel kanıtı bu deneyde yoktur.
