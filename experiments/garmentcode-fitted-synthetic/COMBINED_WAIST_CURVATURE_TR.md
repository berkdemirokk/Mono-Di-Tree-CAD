# Birleşik sentetik arka bel ve yan eğrilik deneyi

Bu araştırma seçili dört sentetik beden ve sabit kolsuz üst/kalem etek modelinin yalnız sağ üst gövde panelleri üzerindedir. Kaynak şekil karşılaştırması başlangıçtaki **C# araştırma adayına** göredir; değiştirilmiş kontur Python kaynağında yeniden optimize edilmemiştir. Bel hedefi, **arka üst yarım bel dikişinin** `waist_back_width / 2` değeridir; ön belin, eteğin veya tam üst–etek dikiş grafiğinin eşleştiği kontrol edilmemiştir. Yan hedefi aynı sağ ön ve sağ arka yanın ikişer kenarının toplam uzunluklarıdır. Bu bir tam ürün veya fiziksel uyum doğrulaması değildir.

Ön panel sabittir. Önceki deneyin çözümüyle arka 6 numaralı bel eğrisinin yan ucu yatay kaydırılır; 7 numaralı yan kenar bu uca bağlanır. Sabit uçlu 8 numaralı arka yan kenar, normal yönde tek parametreli kuadratik eğriye dönüştürülerek yan uzunluğu için yaklaşık aday aranır. Pens kenarları, arka orta bel ucu, yan ara noktası, yan üst ucu ve diğer bütün arka kontur kenarları birebir korunur. Bu aile ön konturu ve yan ara noktayı değiştirmez; tüm olası eğrileri veya kumaş şekillerini kapsamaz.

Varsayılan araştırma sınırları: bel yan ucunun ve bel/7 numaralı kenarın hesaplanan aynı-parametre sapma üst sınırının her biri **0,3 cm**, 8 numaralı kenarın hesaplanan en büyük aynı-parametre sapması **1,5 cm**. İlk iki sapma, eş dereceli Bézier kontrol noktası farklarının maksimumundan gelen dışbükey-zarf üst sınırıdır; 8 numaralı kenar için maksimum orta noktadadır. Hesaplar binary64 tanısıdır, dışa yuvarlanmış kesin mesafe sertifikası değildir; karşılaştırmadaki `1e-12 cm` sayısal marj fiziksel tolerans değildir. Bunlar yalnız seçilmiş deney varsayımlarıdır; kabul edilebilir şekil değişimi oldukları kanıtlanmamıştır.

Simpson integrali yalnız yan eğri adayını seçer. Son konturdan arka belin üç parçası ve her iki yanın iki parçası yeniden ölçülür. Temsil edilen kontrol noktaları üzerinde dyadik uzunluk aralıklarının tamamı, bel hedefi için `0,00001 cm`, yanlar için `0,0001 cm` sayısal fark bandı içindeyse kabul edilir. Bantlar fiziksel dikiş toleransı değildir; önceki kayan nokta hesaplarını veya Python optimizeri farkını kapsamaz. Temsil edilen sağ ön ve arka konturun önce/sonra kapanışı, kenar kimlikleri, eğri çiftleri ve monotonluğu incelenir; herhangi bir çözülmemiş çift veya monotonluk sorunu adayın reddidir. Sol paneller, çentik eşleşmesi, kumaş ve tam dikiş grafiği incelenmez.

| Beden | Arka yarım bel önce → sonra (hedef), cm | Ön yan, cm | Arka yan önce → sonra, cm | Bel eğrisi / yan 7 sapma üst sınırı, cm yaklaşık | Yan 8 azami sapma, cm yaklaşık |
|---|---:|---:|---:|---:|---:|
| base | 18,178505 → 18,000000 (18) | 19,207447 | 18,838868 → 19,207447 | 0,181913 / 0,181913 | 0,797655 |
| distinct | 17,157741 → 17,000000 (17) | 21,328167 | 20,821237 → 21,328167 | 0,160739 / 0,160739 | 0,957854 |
| third | 19,119955 → 19,000000 (19) | 20,183744 | 19,758216 → 20,183744 | 0,121403 / 0,121403 | 0,892159 |
| fifth | 17,574958 → 17,500000 (17,5) | 20,190709 | 19,693652 → 20,190709 | 0,075552 / 0,075552 | 0,833481 |

Tablodaki değerler gösterim için yuvarlanmıştır; kabulde gösterilen yaklaşık sayılar değil aralıklar karşılaştırılır. Dört örnekte sağ ön ve sağ arka kontur öncesi/sonrası `ClosedRepresented`, çözülmemiş çift sayısı ve monotonluk sorunu sıfırdır. Dört örneğin hepsi otomatik testte gereklidir. Sıfır bel kayması sınırı, sıfır ve 0,5 cm eğrilik sınırları, bilinmeyen beden ve sonlu olmayan sınır sentetik negatif örneklerdir. Sıkı eğrilik sınırında yaklaşık arama aday bulamaz; bu tüm yöntemler veya bu aile için kesin çözümsüzlük ispatı değildir. Aday her hedef ve topoloji kapısını geçmezse sonuç üretilmez.

Durum `unresolved` / `Unverified`; kompozisyon, gerçek ölçü işleme, üretim, dışa aktarma ve plotter kapalıdır. Kaynak şekle sapma tek başına estetik, rahatlık, denge, dikiş payı veya giyilebilirlik kanıtı değildir.
