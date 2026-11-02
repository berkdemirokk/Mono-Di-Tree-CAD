# Seçili özellikler için kapalı deneysel kontrol

Bu çalışma `PatternTools.Offset` ve üretim guard'larını değiştirmez. İki loblu regresyon açık kalır. Testteki `ExperimentalExplicitFeatureGate`, gerçek üretim çağrısının yalnız yerel ayrıntı vetosuna ulaştığı girdide mevcut deneysel kaynak grid, indeksli aday, topoloji ve ters karşılaştırma zincirini kullanır. Bu zincirin bir kısmı ayrı deneydir; üretim onayı değildir.

## İncelenen eksik koşul

Mevcut üretim yolu kaynak ve sonuç konturlarını basitlik açısından doğrular; grid birleşmesini ve dönüş değişimini, çok kısa keskin içbükey girintiyi, dar dış köşeyi, ters ofsette iki yönlü yerel örnekleri ve XOR alanını kontrol eder. Bu kontroller **kullanıcının belirli bir V genişliği/derinliği veya iki kenar arasındaki en az açıklık için gereksinimini içermez**. 8-grid örnek mesafesi, tüm ilgili giysi özelliklerinin boyutsal uygunluğunu tek başına kanıtlamaz. Deneysel indeksli miter ve topoloji kanıtı da ayrı bir özellik toleransı yerine geçmez.

Deneyin ek koşulu, açıkça seçilen her V için kaynak/adayda aynı indeksli ağız–tanık–ağız üçlüsünü ve ayrı kullanıcı genişlik/derinlik toleranslarını; seçilen iki komşu olmayan kanal kenarı için aday üzerindeki en az açıklığı doğrulamaktır. V kontrolü mevcut tam binary64 karşılaştırma deneyini kullanır. Kanal için aday binary64 noktalarındaki iki sonlu segmentin kesişmesi tam tamsayı işaretleriyle reddedilir; kesişmeyen segmentlerde dört uçtan karşı segmente tam uzaklık karşılaştırması kullanılır. Bu sınır **yalnız seçilen iki segment** içindir; bütün kanal geometrisi veya diğer özellikler için çıkarım yapılmaz.

Tolerans/özellik bilgisi verilmezse deney uygun parça vermez. Yanlış, tekrarlı veya komşu kenar seçimi reddedilir. Özgün iki loblu şekilde sayısal aday olsa da özellikler tam olarak bilinmediği için üretim onayı verilmez. 6×0,5 mm V örneğinde açık 0,1 mm genişlik veya 0,01 mm derinlik toleransı ayrı ayrı reddeder; 0,2/0,02 mm bu **seçili ölçüleri** geçirir. Bunlar test girdisidir, tavsiye edilen toleranslar değildir. Değişim paralel miterin doğal sonucu olabilir. Başarılı seçili kontrol bile otomatik keşfin tamlığını veya üretim uygunluğunu kanıtlamaz.

İki kenar arasındaki 5 mm sınırının tam eşitliği/geçen bir sonraki binary64 değerin reddi, kesişme, sıfır kenar ve büyük koordinat karşı örnekleri test edilir. Bir gridlik çentik ve kapanan dar kanal örnekleri önceki güvenlik kontrollerinde reddedilir. Uzun kollu, 1–7 grid derinlikli sığ V denemelerinde ise test edilen üretim sonuçlarında ters ofset tepe noktası korunmuştur; bu gözlem bütün sığ V'ler için kanıt değildir.

Özellik sayıları ayrı ayrı 0–64 aralığında doğrulanıp sonra toplanır; sahte aşırı büyük/negatif `Count` değerleri ve toplamın 64'ü aşması listeler okunmadan reddedilir. Kanal testleri kollinear ayrı ve örtüşen parçaları, uç temasını, eğik iç izdüşümü ve uç–uç minimumunu da kapsar. Bir seçili üçlüde ağız ve tanık aynı kalırken aradaki başka bir nokta derinlik değiştirebilir; seçili üçlü ölçümünün geçmesi o noktayı veya seçilmeyen bir kanalı doğrulamaz. Bu gösterim tam gate'in geçtiği iddiası değildir.

## Üretime geçmeden önce

Kaynak ve adayda bütün önemli özelliklerin nasıl belirleneceği veya belirsizlikte kullanıcıya nasıl gösterileceği, ilgili toleransların kaynağı, kaynak–aday kimlik/eşleme ve seçili segment çiftinin gerçekten ilgili kanal olup olmadığı, istenen paya göre interval sınırı, uyumluluk, iptal ve performans ayrı olarak çözülmelidir. Deneyin seçili özellikleri geçmesi, `NotchMark` veya imalat semantiğini kapsamaz ve otomatik olarak parçaya dönüştürülmez.
