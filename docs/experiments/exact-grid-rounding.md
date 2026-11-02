# Kesin kaynak ızgarası: test adayı

`ExperimentalExactGridRounding` yalnız test projesindedir. Sonlu binary64 sayısının tam ikili değerini çözümler, 1.000.000 ile `UInt128` tamsayı aritmetiğinde çarpar ve en yakın tamsayıya, eşitlikte sıfırdan uzağa yuvarlar. Sonuç orijinal ondalık girdiyi değil uygulamanın elindeki binary64 değerini temsil eder. `Geometry.MaxCoordinate` dışı ve sonlu olmayan değerler reddedilir.

Bağımsız `BigInteger` rasyonel oracle ile sıfır, negatif sıfır, subnormal değerler, tam ikili yarım bağlar, yanlarındaki temsil edilebilir sayılar, pozitif/negatif koordinat sınırları ve sabit tohumlu 25.000 bit örneği sınandı. Yarım bağa örnek `±0,0078125 mm`: kesin grid `±7813`.

Gerçek üretim `PatternTools.Offset` önce çağrılır; yalnız mevcut yerel ayrıntı istisnası alınırsa test akışı kesin kaynak gridi, mevcut dönüş koruması, açık kaynak/grid fiziksel bütçeleri, indeksli miter/sweep ve mevcut 8-grid ters örneklemesi ile XOR alan koşulunu sınar. Bu test akışı üretim değişikliği veya onay değildir. Özgün 512 loblu şekil iki yönde ve 0, 50.000.000, 99.998.000 mm ötelemelerinde bu deneysel kontrollerden geçti. Üretim çağrısı aynı örneklerde yerel ayrıntı istisnası vermeye devam eder. Büyük ötelenmiş iki örnekte kesin kaynak gridinin mevcut üretim dönüşümünden sırasıyla üç ve dört eksen değerinde birer grid farklı olması, kaydedilmiş veya dışarı verilen koordinatların değişebileceği bir geriye uyumluluk etkisidir.

Bir grid derinlikli girinti, orijinal küçük/keskin girinti vetosunda; kapanan dar kanal, indeksli aday kanıtında reddedildi. 6×0,5 mm V içeren loblu örnek deneysel geometri kontrollerinden geçti, ancak ağız ölçüsü yaklaşık 6 mm'den 5,834475 mm'ye değişti. Bu değişim miter ofsetin doğal sonucu olabilir. Tüm özellikler veya istenen boyut toleransları otomatik olarak bilinmediğinden güvenli imalat anlamı çıkarılmaz.

Yerel bir denemede 1.024.000 koordinat için ısınma sonrası üç tekrarın en iyisi mevcut `Math.Round` aritmetiğinde 1,365 ms, deneysel `UInt128` dönüşümünde 5,567 ms idi. Bu yalnız o çalışmaya ait mikro ölçümdür; makine, JIT, eşzamanlı yük ve tam ofset akışı için performans garantisi değildir. Daha önce kullanılan indeksli kanıt ve kontur taramaları bu mikro ölçüme dahil değildir.

Üretime geçiş öncesi özellik ve tolerans sözleşmesi, geriye uyumluluk ve mevcut proje/çıktı etkisi, mevcut bütün guard'ların kapsamı, sınır değerler, kaynak ve sonuç gösterimi, iptal ve performans ayrı doğrulanmalıdır. Burada `PatternTools.Offset` ve üretim dosyası değiştirilmemiştir.
