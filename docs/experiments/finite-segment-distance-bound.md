# Sonlu konturlar için iki yönlü mesafe sınırı: test deneyi

Bu yöntem yalnız test projesinde, kullanıcının açıkça verdiği grid toleransıyla çalışır; üretim guard'ını veya `PatternTools.Offset` davranışını değiştirmez. Bir grid = 0,000001 mm'dir. Kontrol edilen noktalar tamsayı grid koordinatları olarak kabul edilir; özgün binary64 veya fiziksel ölçümün bu koordinatlara dönüşümü ayrı bir sorundur.

## Sürekli kapsama koşulu

Her sonlu kaynak segmenti belirtilen sayıda eşit rasyonel alt segmente bölünür. Her alt segmentin **iki ucu için aynı karşı sonlu segmente** olan uzaklığın toleranstan küçük veya eşit olduğu tam tamsayı aritmetiğiyle gösterilirse, karşı segmentin tolerans yarıçapındaki çevresi dışbükey olduğundan bütün alt segment kapsanır. İşlem bütün kaynak segmentlerine ve sonra ters yönde bütün aday segmentlerine uygulanır. Bu nedenle kabul, iki sonlu poligonal sınırın her noktasının karşı sınırda tolerans dahilinde bir noktası olduğu için iki yönlü Hausdorff uzaklığına yeterli bir üst sınır verir; köşe veya raster örneklemesine dayanmaz.

Koşul yeterlidir, zorunlu değildir. Bir alt segmentin iki ucu farklı karşı segmentlere yakınsa, aradaki kapsama gerçekten var olsa bile kanıt bulunamayabilir. Alt bölme bazı kavşakları ayırabilir; sınırlı bölme sayısı ve iki milyon karşılaştırma bütçesi nedeniyle belirsiz durumlar reddedilir. Yöntem kontur basitliğini, yönünü veya fiziksel çentiklerin anlamını tek başına incelemez.

Somut karşı örnek: aynı kareyi iki kez dolaşan adayın nokta kümesi tek dolaşımlı kareyle aynıdır ve iki yönlü uzaklığı sıfırdır; buna rağmen aday tekrarlı ve geçersiz bir konturdur. Bu nedenle ayrı basitlik, tekrar, yön ve topoloji denetimleri zorunludur.

## Grid kaynağın ideal miteri

Ek test, grid kaynak üzerindeki indeksli ideal miterin her köşesini mevcut interval hesabıyla grid vekiline eksen başına 1 grid içinde sınırlar. İndeksli kenarların doğrusal aradeğerleri de bu sınıra uyar; her noktada Öklid uzaklığı için **2 grid** güvenli, ama sıkı olmayan bir üst sınırdır. Önce vekil ve aday sonlu sınırları için iki yönlü mesafe `T−2` grid içinde kanıtlanırsa, üçgen eşitsizliğiyle ideal miter ile aday için toplam `T` grid sınırı elde edilir. Bu sonuç mevcut interval hesabının doğruluğuna bağlıdır ve grid kaynağın ideal miterini ifade eder.

Özgün 512 noktalı loblu grid ve indeksli adayı iki yönde `T=2 grid = 0,000002 mm` için geçti; 6×0,5 mm V eklenmiş örnek de aynı geometrik sınırdan geçti. Bu değer kullanıcı/üretim toleransı değildir ve çentiğin boyutunu koruduğu anlamına gelmez. Bir gridlik kaybolan çentik ile kapanan dar kanal örneklerinde indeksli ideal adayın ön koşulları başarısızdır.

Uçları aynı olan ama ortada uzaklaşan yol, farklı uzunluktaki segmentler, yakın segmentin kavşakta değişmesi, kapalı konturun son kenarı, sıfır kenar, bütçe ve iptal karşı örnekleri test edilmiştir. Kanıtlanamayan girdiye geçerli sonuç denmez.

## Üretim için eksik kalanlar

Kaynak grid dönüşümünün özgün mm geometriye etkisi, özellikle neredeyse paralel kenarlarda ideal miter değişimi için ayrı fiziksel hata sınırı; aday gridin `double` mm'ye dönüşüm hatası; miter/yuvarlama interval uygulamasının bağımsız doğrulanması; basitlik, seçili ve seçilmeyen çentik/kanal toleransları ve mevcut tüm üretim kontrolleri hâlâ gereklidir. Özellikle bu grid sınırını özgün kalıbın ideal ofsetine veya imalata yönelik genel mm garantisi olarak sunmak doğru değildir.
