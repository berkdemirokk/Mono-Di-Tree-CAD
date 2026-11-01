# Döndürmeli pastal: kalıcı iz ve sürüm geçişi

Durum: **tasarım önerisi**, uygulama veya üretim onayı değildir. İncelenen temel: `9e27a5f`. Bu değişiklik döndürmeli pastal kaydetme işlevini henüz eklemez.

## Mevcut durum ve sorun

`NestingSnapshot`, kumaş ölçülerini, yerleşim parmak izini ve kaynak/kopya/yerleşen parça kimliklerini saklar; açı, kaynak geometrisi ve dönüş doğrulama girdileri yoktur. `ProjectStorage` sadece `deterministic-shelf-v1` izini kabul eder. `PerCopyNestingResult` açıları ve iki parmak izini bellekte tutar. `PerCopyTraceVerifier` kaynak, dönüş, öteleme, çentik, iplik yönü ve raf düzenini tekrar kontrol eder; 9e27a5f işaretli dönüşü destekler. Mevcut snapshot'a yalnızca açı eklemek kaynak değiştiğinde bağımsız doğrulamaya yetmez.

Mevcut okuyucu yalnız 1–6 sürümlerini kabul eder, bilinmeyen alanları reddeder ve proje boyutunu 64 MB ile sınırlar. Yeni iz eski sürüme yazılırsa eski okuyucuda güvenli ve anlaşılır sürüm reddi garanti edilmez. Eski snapshot'taki bilinmeyen açılar sıfır olarak yorumlanamaz.

## Önerilen sürüm 7 modeli

Yalnız tam döndürme izi bulunan belge `[7, project]` olarak yazılır. 1–6 okuması ve döndürme izi bulunmayan belgelerin mevcut sürüm seçimi değişmez. Sürüm 7 izini içeren belge düşük sürüme otomatik indirgenmez; mevcut okuyucular sürüm numarasını reddeder. Yeni okuyucu, 7 içinde zorunlu iz eksikse veya eski sürümde yeni iz varsa reddeder. Desteklenmeyen gelecekteki sürüm veya algoritma da reddedilir. Mobil ve diğer bağımsız okuyucular sürüm 7 güncellenene dek bu dosyaları açamaz; kayıpsız geriye dönüş ayrıca tasarlanmalıdır.

`NestingSnapshot` altında yeni, zorunlu alanları olan `RotationTrace` önerilir:

| Alan | İçerik ve kurallar |
| --- | --- |
| `Schema` | Sabit `per-copy-orthogonal-trace-v1`; bilinmeyen değer reddedilir. |
| `Algorithm` | Hesaplama ve doğrulama anlamını sabitleyen sürümlü kimlik; farklı uygulamaların aynı isimle sessizce kabulü yok. |
| `SourcePieces` | Seçilen kaynak parçaların hesaplama sırasındaki bağımsız, sıralı kopyaları: kimlik, ad, kapalı kontur ve bütün noktalar, miktar ve metadata, iplik çizgisi ve bütün çentikler dahil. Eksik alan varsayılanla doldurulmaz. |
| `SourceFingerprint`, `LayoutFingerprint` | Kaynak seçimi ve sonuç için mevcut parmak izi algoritması ve sürümü açıkça belirtilir. Hash bütünlük kontrolüdür, tek başına doğrulama veya kimlik kanıtı değildir. |
| `Selections` | Seçilen kaynak kimlikleri ve sırası; kaynak kimlikleri tekil ve `SourcePieces` ile bire bir olmalı. Kaynak miktarları kopya açılımını yeniden üretir. |
| `Placements` | Her kaynak kimliği, kopya numarası, yerleşen kimlik ve seçilmiş derece (`0`, `90`, `180`, `270`) tam ve bire bir saklanır. Mevcut yerleşim listesiyle çelişki reddedilir. |
| `AnglePolicy` | Açık kopya açıları ile kaynak başına izinli açı listesi arasında ayrım yapan kip; kullanılan kipin bütün girdileri, sıfır varsayımı olmadan saklanır. Otomatik arama kipi ancak gerçekten uygulanıp sürümlenirse eklenir. |
| `FabricWidth`, `Gap`, `EdgeMargin`, `UsedLength`, `MaxLength` | Hesaplamanın milimetre cinsinden değerleri. Sınırsız `MaxLength` açık bir ayrım ile temsil edilir; JSON sonsuz sayısı veya eksik alan kullanılmaz. Snapshot değerleriyle çelişki reddedilir. |
| `GrainDirection`, `ToleranceDegrees` | Hesaplamaya verilen gerçek iplik yönü politikası ve derece toleransı; yalnız uygulamanın desteklediği enum değerleri kabul edilir. |
| `TransformConvention` | Sabit sürümlü sözleşme: mevcut `OrthogonalRotation` işlemi, koordinat sistemi ve orijine göre çeyrek dönüş; ardından yerleşim ötelemesi. Gelecekte farklı dönüş merkezi farklı sürüm gerektirir. |

Kaynak parça şeması bütün geometrik sayıları ve işaretli sıfır dahil anlamlı bitleri kayıpsız taşımalıdır; mevcut JSON sayı serileştiricisi bunun için dosyaya yazıp tekrar okuma testleriyle doğrulanmadan varsayılmaz. Öteleme ayrıca saklanırsa doğrudan türetilen geometriyle eşleşmesi doğrulanmalıdır; saklanmaması durumunda yerleşim konturundan yeniden doğrulanır. Dosya yolunu kaynak kimliği saymayın. Projenin serileme kuralları ve içe aktarma geçmişi seçilen parça snapshot'ında yer almıyorsa bu alanların korunmadığı açıkça belirtilir; bunların da ürün gereksinimi olması halinde ayrı sürümlü ve doğrulanan veri eklenir.

## Yükleme, doğrulama ve kaydetme sözleşmesi

1. JSON yinelenen/bilinmeyen alanları, eksik zorunlu değerleri, geçersiz sayıları, yinelenen kimlikleri ve sürüm uyuşmazlıklarını model varsayılanları uygulanmadan reddet. Kaynak ve sonuç için parça, nokta, çentik, toplam bayt ve hesaplama işi sınırları birlikte uygulanmalı; gömülü kaynak sınırları atlatmamalı.
2. Snapshot ve dosya düzeni parmak izlerini yeniden hesapla; kaynak parmak izinde kaynak sırasını mevcut `FingerprintSources` ile aynı tut. Kaynak/kopya/yerleşen kimlikleri ve miktar açılımını doğrula. Görünür belgede yapılmış sonraki değişiklikler izi güncel olmayan geçmiş olarak işaretlemeli; kaydedilmiş geçmiş sessizce yeniden doğrulanmış sayılmamalı.
3. Gömülü kaynak ve kaydedilmiş kısıtlardan doğrulayıcının beklediği nesneleri yeniden kur. İlgili `PerCopyTraceVerifier.IsValidForConstraints` veya `IsValidForAllowedAngles` yolunu çağır; kaynak parçası, her nokta, işaret, iplik çizgisi, açı, öteleme, kumaş/boşluk ve raf koşullarını kontrol et. Persist edilen `valid=true` gibi bir bayrak güvence değildir. Mevcut doğrulayıcı hesaplama bütçesini aşan veri güvenli biçimde reddedilir.
4. Kaynak ve sonucu bağımsız snapshot olarak al; iptal veya eşzamanlı düzenlemeyi kontrol et. Tam doğrulama ve serileştirme başarılı olduktan sonra mevcut atomik yazım ve dosya sürümü çakışma mekanizmasını kullan. Çatışmada, iptalde, sınır aşımında veya başarısız doğrulamada önceki dosya ve açık kaynak değişmemeli.

Bir hash'i yeniden hesaplayabilen saldırgan yanlış açı veya işaret yazabilir: doğrulayıcı kontur ve işaret ilişkisini her seferinde incelemelidir. Bu iz üretim/makine sertifikası değildir; kaynak snapshot'ının tarihsel doğruluğunu veya imzasını da kanıtlamaz.

## Uygulama ve kabul sırası

1. Core modeline derin kopyalanan, açık kipli iz türlerini ekle; mevcut snapshot'ları ve eski clone/durum karşılaştırmalarını bozmadan ilerle. Ardından v7 için zorunlu ham JSON alan kontrolü, sürüm kapısı, birleşik kaynak limitleri ve serileştirmeyi ekle.
2. Nesting katmanında yerleşim sonucundan bağımsız immutable kaynak/istek snapshot'ı oluştur ve kaydetmeden önce ve yükledikten sonra yeniden doğrula. Core/FileFormats ile Nesting arasındaki bağımlılık yönünü korumak için doğrulama koordinasyonu üst katmanda olmalı; ayrıştırıcı tanımadığı veriyi saklayıp başarılı göstermemeli.
3. Kullanıcıya kayıt/açma ve güncel olmayan iz durumunu sun; eski okuyucunun v7'yi reddedeceğini belirt. Başarısızlıkta veya iptalde kaynak belgesini ve mevcut dosyayı koru. Mobil ve diğer okuyucuları bağımsız güncellemeden ortak uyumluluk iddia etme.
4. Anlamlı regresyonlar: işaretli, iplik çizgili, farklı açılı ve çok kopyalı kaydet/aç/doğrula; 1–6 dosyalarında davranışın korunması; eski okuyucunun 7 reddi; eksik/bilinmeyen/yinelenen alan ve açı; eksik/kopya kimlikleri; tüm hash'ler yeniden hesaplanmış yanlış işaret, kaynak, açı veya yerleşimin reddi; iplik ve boşluk ihlali; signed-zero yuvarlama; sınırlar, iptal ve yazım çakışması; güncel olmayan iz. Testler hem elle belirtilmiş hem izinli açılar kipini ve sınırsız uzunluğu kapsamalı.

Bu plan üzerinde derleme veya test çalıştırılmamıştır. Ürün koduna geçmeden önce mevcut sayı serileştirmesinin bit koruması, kullanılan doğrulayıcının bütün kaynak ve işaret semantiği, 64 MB altında birleşik bütçe ve bağımsız okuyucuların sürüm reddi doğrulanmalıdır.
