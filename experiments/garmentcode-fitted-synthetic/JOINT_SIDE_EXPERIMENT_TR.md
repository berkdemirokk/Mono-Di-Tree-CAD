# Sentetik arka bel ve yan dikişi birlikte inceleme

Bu deney yalnız seçili kolsuz üst/kalem etek tasarımı ve dört sabit sentetik beden içindir. Gerçek ölçü, kumaş, rahatlık payı veya prova yoktur. Aday ayrı `Unverified` türündedir; dikiş grafiğine, üretime, dışa aktarmaya ve plotter’a bağlanamaz.

## Hedef ve iki değişken

Deney, mevcut [yan uç bel deneyini](WAIST_RELAXATION_EXPERIMENT_TR.md) başlangıç alır. Birinci değişken arka bel yan ucunun yatay kaymasıdır; pensler ve orta arka noktası korunurken yarım arka belin hedefe yaklaşması aranır. İkinci değişken arka yan sınırındaki iki düz kenarın ortak ara noktasının **düşey** konumudur. Bu nokta, bel yan ucunun yüksekliği ile sabit kol evi yan ucu arasında kalmak zorundadır. Ön panel ve arka kol evi ucu değiştirilmez.

Bu geometride `dx` yatay fark, `H` bel yan ucuyla kol evi yan ucu arası düşey fark, `z` ara noktanın bel yanından yüksekliği olmak üzere arka yan uzunluğu `sqrt(dx² + z²) + H − z` olur. Ön yan uzunluğu hedef alındığında uygun iç nokta cebirsel olarak denenir. Bu formül yalnız bu deneyin dikey ikinci yan kenar varsayımında geçerlidir. Desteklenmeyen geometri, hedef aralık dışında çözüm, sonlu olmayan değer, sınır uçlarında/ötesindeki nokta veya çözümlenemeyen kontur açık hatayla reddedilir; bu, diğer geometrilerde çözüm bulunmadığının kanıtı değildir.

İlk değişkenin bel uzunluğu, ikinci değişkenin ön ve arka yan uzunlukları temsil edilen C# çizgi/Bézier noktaları üzerinde tam dyadic uzunluk aralıklarıyla sınanır. Her iki sayısal amaç için kullanılan `±0,00001 cm` yalnız araştırma doğrulama bandıdır; fiziksel dikiş toleransı değildir. Kaynak SciPy optimizeri ve önceki kayan nokta işlemlerinin hatası bu aralıklara dahil değildir.

## Sentetik önce/sonra

Uzunluklar C# araştırma adayına aittir; Python kaynak üzerinde değişiklik yapılmadı. Omuz/kol evi kesimi, ön panel ve pens kenarları değişmez. Arka yan değerleri sağ yarımın iki kenarının toplamıdır. Tablodaki `ön yan`, değiştirilmeden kalan karşı taraftır.

| Sentetik örnek | Yarım arka bel önce → sonra, cm | Arka yan önce → sonra, cm | Sabit ön yan, cm | Yan ara noktası y önce → sonra, cm |
|---|---:|---:|---:|---:|
| temel | 18,178505 → yaklaşık 18,000000 | 18,838868 → yaklaşık 19,207447 | 19,207447 | 14,333 → 1,530 |
| farklı oran | 17,157742 → yaklaşık 17,000000 | 20,821237 → yaklaşık 21,328167 | 21,328167 | 16,333 → 0,529 |
| üçüncü | 19,119955 → yaklaşık 19,000000 | 19,758216 → yaklaşık 20,183744 | 20,183744 | 15,000 → 1,141 |
| beşinci | 17,574958 → yaklaşık 17,500000 | 19,693652 → yaklaşık 20,190709 | 20,190709 | 16,333 → 0,680 |

İlk değişkenin yatay kayması sırasıyla yaklaşık 1,819; 1,607; 1,214; 0,756 mm’dir. İkinci değişkenle ara noktanın **yaklaşık 12,8–15,8 cm** hareket etmesi, eşit uzunluk koşulunun büyük bir yan kontur değişikliği pahasına sağlandığını gösterir. Bunun fiziksel olarak giyilebilir, estetik veya üretilebilir olduğuna dair kanıt yoktur; yalnız uzunlukların eşleşmesi şekil uyumunu garanti etmez.

Dört adayda penslerin dört kenarı temsil edilen koordinatlarda aynen korunur. Ön kesimin orta arka noktası ve kol evi yan ucu sabittir; diğer değişmeyen kenarlar test edilir. Bel eğrisinin göreli kontrolü önceki deneydeki gibi korunur ve yeni mutlak kontrol, uzunlukla topoloji arasında birebir karşılaştırılır. Tam sağ arka konturun 13 kenarı için önce ve sonra 78 çiftin ayrıklık/ortak uç denetiminde `unresolved=0`; çizgi/Bézier yeterli monotonlukta başarısızlık `0` bulundu. Bu sonuç yalnız temsil edilen noktalar içindir, kaynak/hassasiyet hatası veya kumaş için topoloji garantisi değildir. Sol yarım ve tam dikiş grafiği değiştirilip yeniden doğrulanmadı.

## Negatif durumlar ve sınır

Yan uzunluğu hedefi desteklenen aralık dışındaysa, ikinci kenar dikey değilse veya ara nokta uç noktalara denk geliyorsa aday üretilmez. Geçersiz/hedefsiz örnekler, sıfır yatay fark, sonlu olmayan sayı, hatalı uzunluk bandı, bilinmeyen kimlik ve kaynak optimizeri bilinen 30° karşı örneği ret testlerindedir. Dört sabit sentetik adayın hepsi sayısal denetimden geçti; tüm bedenler için fizibilite, optimumluk veya fiziksel uyum çıkarımı yapılamaz. Kaynak omuz/yan/bel bağlantılarındaki tasarım niyeti ve gerçek rahatlık henüz belirlenmediğinden sonuç hiçbir üretim yoluna açılmaz.
