# Dikiş payında çentik verisi: mevcut kapsam ve eksikler

Bu not izole deneyin incelemesidir; üretim davranışını veya dosya biçimini değiştirmez.

## Mevcut gösterimler

- `TekstilCAD.Core.NotchMark`, yalnız `VertexIndex` ve `Position` içeren, mevcut kontur köşesine bağlı görsel bir işarettir. `MarkedPieceTranslation` konumun seçilen köşeyle aynı olmasını, indeksin benzersiz olmasını ve kapalı konturun tekrarlanan son noktasının seçilmemesini ister. Kenarın içindeki bağımsız bir nokta veya parametre saklanmaz. Yön, kenar kimliği, çentik tipi, genişlik, derinlik, birim, tolerans ve kesim komutu yoktur.
- DXF `POINT` eşlemesi kullanıcı tarafından seçilen kaynak noktanın desteklenen kapalı polilin köşesiyle tam eşleşmesini ister. `NotchMark` üretir; fiziksel çentik anlamı çıkarmaz. Çizimde işaret halka olarak görünür. İnceleme DXF'i ayrı katmanda `POINT` yazar; işaret fiziksel kesim değildir.
- `PatternTools.InsertNotch`, seçilen kenara ağız–uç–ağız olmak üzere üç kontur noktası ekleyerek fiziksel V geometrisi oluşturur. Genişlik, derinlik ve kenar oranı işlem sırasında kullanılır; sonuçta bunlara veya kaynak kenara bağlı ayrı bir çentik kaydı oluşturulmaz. Sonraki işlemler yalnız noktaları görür. Bu geometri `NotchMark` ile aynı kavram değildir.
- `PatternTools.Offset` işaretli parçayı `NotchSafety` üzerinden reddeder. Makine simülasyonu da işaretli parçanın aktarımını reddeder. Bu korumalar ve gerçek dikiş payı akışı değişmemiştir.

## Somut belirsizlikler

1. `(0,0)→(100,0)` kenarının `(50,0)` noktasındaki bir işaret, iki alanlı `NotchMark` ile ifade edilemez: herhangi bir köşe indeksiyle konum eşleşmez. Kontura yeni nokta eklemek geometrik indeksleri ve kaynak bağlantılarını değiştirir; otomatik yapılması güvenli bir eşleme sözleşmesi değildir.
2. Aynı köşe konumundaki işaret; gelen kenara, giden kenara veya başka bir tekstil işlemine ait olabilir. İşaretin iki alanı bu seçenekleri, iç/dış yönü veya V çentiğin derinliğini ayırt etmez. Köşe eşlemesinden bunlar çıkarılamaz.
3. Genişliği 4 mm ve derinliği 2 mm olan `InsertNotch` sonucunda üç kontur noktası oluşur; `Notches` boş kalabilir. Dolayısıyla işaretleri aktarmak fiziksel V geometrisinin de korunduğunu göstermez. Tersine, V içermeyen konturda görsel işaret bulunabilir.
4. Deneysel `OffsetVisualMarkCorrespondenceExperiment` yalnız açıkça sağlanan adayın, köşe işaretlerini varsayımsal indeks eşlemesiyle taşıyıp taşımadığını inceler. Kaynak/adaydaki dönüşümün imalat anlamını, fiziksel çentik boyutlarını, işaret yönünü veya bütün özellikleri doğrulamaz; üretime bağlı değildir.

## Gerçekçi uygulama sırası

1. Önce kullanıcıyla işaretin yalnız görsel mi, fiziksel çentik mi olduğunu ve dikiş payı sonrasındaki beklenen davranışını tanımlayın. İki komşu kenardan hangisinin esas olduğu, açık/kapalı yol ve yön ters çevrilmesi durumları için açık kurallar gereklidir.
2. Mevcut `NotchMark` türünün davranışını ve eski dosyaların okunmasını koruyun. Kenar içi konum gerekirse, ayrı ve sürümlenmiş bir veri sözleşmesinde kaynak parça/kenar kimliği, yönlü kenar parametresi veya köşe bağlantısı ve dönüşüm geçmişi tanımlayın. Eski görsel işaretlerden eksik alanları tahmin etmeyin; belirsizse eşlemeyi reddedin veya kullanıcı incelemesine bırakın.
3. Fiziksel çentik için açık tür, iç/dış yön, derinlik, genişlik, birim, tolerans ve bu değerlerin hangi konturda ölçüldüğünü ayrı kaydedin. Mevcut üç noktalı V geometrisini otomatik olarak etiketlemek ancak desteklenen kapsam ve belirsizlik retleri ayrıca tanımlanırsa değerlendirilebilir.
4. Dikiş payı için kaynak kenar/köşe ile aday sınır arasında tekil, yönü ve bağlantıyı koruyan eşleme; kıvrım/köşe, birleşen veya bölünen kenar, dar kanal, kendine yaklaşma, yuvarlama, dönüşüm ve kaynak düzenlemelerinde kayıp/çakışma durumlarına yönelik testler geliştirin. Fiziksel gereksinimler ve toleranslar kullanıcı veya ürün sözleşmesinden gelmelidir.
5. Saklama, geri alma, DXF alışverişi ve makine çıktısı için ayrı uyumluluk kararları ve uçtan uca testler tamamlanmadan mevcut retleri değiştirmeyin. İşaretlerin görsel eşlenmesi tek başına kesim komutu üretmez.
