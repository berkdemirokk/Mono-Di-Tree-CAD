# Dikiş payı için ölçüm ve kullanıcı incelemesi: deneysel sözleşme

Bu belge tasarımdır; üretim yolu, dosya biçimi ve mevcut korumalar değiştirilmemiştir. `ExperimentalOffsetMeasurement` yalnız test projesindedir, proje parçası üretmez ve dışa aktarılacak sonuç vermez.

## Zorunlu açık bilgiler

- Kullanıcı veya ürün sözleşmesi; kaynak nokta konumu, kenar/kiriş uzunluğu, yerel yükseklik ve ofset uzaklığı için mm cinsinden sonlu toleransları açıkça sağlar. Buradaki test değerleri varsayılan üretim toleransı değildir.
- Kaynağın gerçek üretim yolunda yalnız yerel ayrıntı doğrulamasında reddedilmiş olması gerekir; önceki doğrulama, küçük/keskin girinti ve işaret retleri aşılmaz. Deney mevcut grid dönüşümüyle çalışır; büyük koordinatta exact-nearest ve mevcut grid uyuşmazsa reddeder. 6e3cdc1'deki alternatif exact grid burada üretime sokulmaz.
- Adayın kaynakla tekil indeksli ilişkisi, indeksli miter/topoloji, grid–binary64 gösterimi, mevcut ters 8-grid karşılaştırması ve XOR alan sınırı deneysel akışta kontrol edilir. Sonuç yine inceleme amaçlıdır.

## Fiziksel uzaklık: tam kapsamı

Her kaynak kenarının yönlü **destek doğrusuna**, karşılık gelen aday kenarın iki ucunun işaretli dik uzaklığı, grid'e yuvarlanmış etkin dikiş payından kullanıcı tarafından verilen `lineToleranceMm` kadar farklı olabilir. Kontrol, gerçek binary64 kaynak ve aday koordinatlarının tam rasyonel değerleri üzerinde tamsayı karşılaştırması kullanır. Uçların doğrultu üzerindeki doğrusal aradeğerleri nedeniyle aynı bant aday kenarın tamamına uygulanır. Ayrıca gösterilen en büyük fark, açıklama için yapılan **yaklaşık** double ölçümdür; başarının dayanağı değildir.

Bu değer tam eğriye/sonlu kaynak kenarına en yakın mesafe, iki kontur arasındaki Hausdorff uzaklığı veya kaynak–aday arasındaki tüm fiziksel sapmaların genel üst sınırı değildir. Kaynak noktalarının ikili64 biçiminden önceki gerçek fiziksel doğruluğunu da kanıtlamaz. Etkin pay ile istenen pay arasındaki fark ayrıca kullanıcıya gösterilmelidir. Bu nedenle müşteri açısından kullanılabilecek bir genel maksimum sapma şartı için sonlu segment kapsaması ve kaynak/aday iki yönlü sınırı ayrıca kanıtlanmalıdır.

İki yöndeki özgün loblu örnekte, testte seçilen 0,00001 mm bant geçti; gösterim için ölçülen en büyük yaklaşık destek-doğrusu farkı 0,000000963 mm'dir. Bu bir ürün toleransı veya sonlu konturlar arası maksimum hata iddiası değildir.

## Özellik ve belirsizlik

Kullanıcı ilgili her çentiğin kaynak ve adayda eşleştirilmiş ağız–tanık–ağız noktalarını ve ayrı mm genişlik/derinlik toleranslarını seçebilir. Ölçüm, yalnız seçilen üçlülerin geçip geçmediğini bildirir. Atlanmış veya yanlış eşleştirilmiş diğer özellikler otomatik olarak keşfedilmez; sıfır seçimin “özellik yok” anlamı yoktur. V'nin ofset sonrası değişmesi, paralel miterin doğal sonucu olabilir; ne kadar değişime izin verileceği açık ürün/iş gereksinimidir.

İnceleme ekranı tasarlanırsa kaynağı ve adayı, etkin/istenen payı, bandı, her seçili üçlünün tolerans ve sonucunu, ölçülmeyen diğer özellikleri ve sınırları göstermeli; belirsizliği ve geçmeyen ölçüleri açıkça belirtmelidir. Tek başına kullanıcı onayı veya bir önizleme üretim güvenliği kanıtı değildir. İstenen genel maksimum sapma ve tüm özellik kapsamı kanıtlanmadan mevcut parçaya uygulama/kaydetme veya makine dışa aktarımı için bu deney kullanılamaz.

## Uyumluluk ve karar

Başarılı mevcut çağrılar aynı davranmalı, yerel ayrıntı dışındaki mevcut retler korunmalı, mevcut grid ve delta yuvarlamasındaki değişiklikler ayrı sürüm/uyumluluk kararı olmadan yapılmamalıdır. İşaretli parçaların desteği ayrıca tasarlanmalıdır. Mevcut testler iki loblu örneğin ölçümünü, seçili V toleransına göre farklı sonucu, küçük çentik/dar kanal ve büyük koordinat retlerini kapsar; bunlar tüm şekiller için garanti oluşturmaz. Bu taslak mevcut iki regresyonu üretimde çözmez.
