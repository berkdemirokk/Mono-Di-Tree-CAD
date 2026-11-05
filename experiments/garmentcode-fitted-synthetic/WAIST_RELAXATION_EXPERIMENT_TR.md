# Arka bel: pens döndürme sınırı ve yan uç deneyi

Bu çalışma yalnız seçili sabit kolsuz üst/kalem etek tasarımının sentetik araştırmasıdır. Hedef, arka üst yarım belin arka bel ölçüsünün yarısına, açık bir rahatlık katsayısı eklemeden eşitlenmesidir. Gerçek ölçüm, kumaş veya fiziksel prova yapılmadı. Sonuçlar `Unverified`/`unresolved`; dikiş grafiğine, üretime, dışa aktarmaya veya plotter’a bağlanamaz.

## Pensler döndürülürken dış uçlar sabitse

İki pensin her biri yer değiştirebilir ve dönebilir; ancak her pensin iki bacak uzunluğu ve aralarındaki açı değişmez. Bu durumda her pensin giriş–çıkış kiriş uzunluğu sabittir. Orta ve yan dış uçlar arasındaki mesafe `D`, iki pensin kiriş uzunlukları `d₁`, `d₂` ve penslerin arasında kalan üç bel eğrisinin toplam uzunluğu `L` ise üçgen eşitsizliği gereği:

`L ≥ D − d₁ − d₂`

`BackWaistRotationStudy`, `D` için alt, iki kiriş ve hedef için üst sınırları temsil edilen binary64 noktalarından tam dyadic tamsayılarla hesaplar. `D` alt sınırı, hedef ile iki kirişin üst sınırları toplamından büyükse kesin karşılaştırma bu **kısıtlı temsil probleminde** çözüm olmadığını gösterir. Test, kaynak fixture ve C# adayını beş sentetik beden için ayrı inceler; beşinde de dış uçlar sabitken penslerin serbestçe döndürülmesi dahi tam hedefe yetmez. Eşik eşitliği `çözüm var` diye yorumlanmaz; belirsiz kalır. Bu sonuç hedefe bir rahatlık payı eklenen veya pens şekillerinin/dış uçların değiştiği başka problemi kapsamaz.

## Yan uç kaydırma: uygulanmış sayısal aday

`BackWaistSideExperiment`, yalnız dört sabit sentetik kimliği kabul eder. İki pensin bütün kenarlarını, orta arka ucunu ve komşu yan kenarın üst ucunu aynı bırakır; alt yan uç yatay olarak içeri kaydırılır. Son bel eğrisinin göreli kontrolü korunur; uç değiştiğinden **mutlak kontrol noktası ve eğri şekli değişir**. Komşu yan kenarın alt ucu yeni noktaya bağlanır. Bilinen 30° optimizer uyuşmazlığı olan sentetik durum ve bilinmeyen kimlikler reddedilir.

İkili arama Simpson tahminiyle bir aday arar. Aday yalnız temsil edilen C# kontrol noktaları için hesaplanan tam dyadic Bézier uzunluk aralığı, hedefin `±0,00001 cm` **sayısal araştırma bandı** içindeyse ve temsil edilen tam sağ arka konturun kapanış/çift ayrıklığı/monotonluk denetimi çözümlenmişse döner. Arama aralığı bulunamaz, sayısal sınır veya kontur denetimi sağlanamazsa hata verir. Bant fiziksel dikiş toleransı değildir; bu işlem bütün bedenlerde çözüm, tekillik veya kaynak optimizer eşdeğerliği kanıtlamaz.

| Sentetik örnek | Arka yarım bel önce → sonra, cm | Hedef, cm | Yan uç kayması, mm | Ön−arka yan farkı önce → sonra, mm |
|---|---:|---:|---:|---:|
| temel | 18,178505 → yaklaşık 18,000000 | 18,0 | 1,819 | 3,686 → 3,548 |
| farklı oran | 17,157742 → yaklaşık 17,000000 | 17,0 | 1,607 | 5,069 → 4,988 |
| üçüncü | 19,119955 → yaklaşık 19,000000 | 19,0 | 1,214 | 4,255 → 4,170 |
| beşinci | 17,574958 → yaklaşık 17,500000 | 17,5 | 0,756 | 4,971 → 4,927 |

Bu değerler **C# adayına** aittir; değiştirilmiş geometri Python kaynakta yeniden optimize edilmemiştir. Yan dikiş karşılaştırması, sabit kalan sağ ön panelin iki yan kenarı ile değişen sağ arka panelin iki yan kenarının sayısal uzunluklarıdır. Farkın biraz azalması fiziksel uyum veya kabul edilebilirlik anlamına gelmez; fark sürmektedir. Sol panel veya tam üst–etek dikiş grafiği bu deneyle yeniden oluşturulmaz.

Penslerin dört kenarı deney öncesi/sonrası temsil edilen koordinatlarda birebir aynıdır; dolayısıyla bu adayda pens bacakları ve açısı değişmez. Diğer tüm ön-kesim kenarları sabittir; yalnız son bel eğrisinin uç noktası ve ona bitişik yan kenarın başlangıcı değişir. Tam sağ arka konturun 13 kenarının 78 çifti için her dört örnekte de önce/sonra `unresolved=0` ve çizgi/Bézier yeterli monotonlukta başarısızlık `0` bulundu. Bu yalnız temsil edilen geometri üzerindeki denetimdir; kaynak optimizer ve önceki kayan nokta hatasını veya kumaşı kapsamaz.

## Sınırlar ve güvenlik

Dört aday da ayrı `Unverified` tipindedir; kompozisyon isteği hata verir. Hesap genel kullanıcı bedenini veya gerçek veriyi kabul etmez, kaynak kısıtlamalarını otomatik gevşetmez ve üretim/plotter açmaz. Kaynak bel/yan farklarının fiziksel hedefi, ölçüm protokolü, dikiş payı, olası rahatlık, malzeme ve farklı bedenlerde prova henüz belirlenmemiştir. Aday, tam giysi çözümü veya gerçek kalıp olarak sunulmamalıdır.
