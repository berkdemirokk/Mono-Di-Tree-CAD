# 20 alan için deterministik ölçü tutarlılığı — araştırma katmanı

[MeasurementConsistencyAudit.cs](MeasurementConsistencyAudit.cs), sabit `PinnedGarment` yolunun [20 alanı](PINNED_MEASUREMENT_SPEC_TR.md) için önceden ayrıştırılmış `decimal?` değerleri ve açık birimleri bellekte inceler. Bu bir müşteri formu, gerçek kişi verisi toplama yolu, otomatik ölçü tahmincisi veya bedene tıbbi hüküm değildir. Yöntem çağrılınca verilen sayıları bellekte işler ve sayıların bir kişiye ait olup olmadığını kendiliğinden ayırt etmez; `PersonalDataCollectionEnabled=false` bir kişisel veri tespit sistemi değil, bu deneyde giriş/toplama yolu bulunmadığının durum bilgisidir. Bu çalışmada gerçek kişi ölçüsü kullanılmadı. Alan listesi farklı giysi tasarımlarına genellenmez. `Accepted=false`, `Status=unresolved`; gerçek ölçü toplama, üretim ve dışa aktarma açılmaz.

## Sözleşme ve sonuçlar

Katman, mevcut [MeasurementContractAudit](MeasurementContractAudit.cs) sonuçlarını korur: eksik/null, tekrar, bilinmeyen/türetilmiş alan, `cm` ve `degree` dışında açık birim, teknik sayı sınırları ve seçili C# modelinin kapsam koşulları `Blocking` olarak raporlanır. Örneğin arka çevre parçalarının toplam ilgili çevreden küçük olması ve mevcut açı/formül kapsamı teknik model kapılarıdır; kişinin anatomisinin geçersiz olduğu anlamına gelmez. Birim dönüştürülmez, alan tahmin edilmez. `decimal` tipi `NaN` ve sonsuz değer taşımaz; bu katmanın dışında kalan metin/JSON ayrıştırıcısı ve bunların kayıpsız reddi burada uygulanmış değildir.

Girdi sayımı en fazla 52 kaydı saklar; 53. kayıt yalnız sınır aşılıp aşılmadığını belirlemek için istenir ve saklanmaz. Yirmi alan gerekli olmakla birlikte mevcut sözleşme 26 bilinen araştırma alanına izin verdiğinden sınır, yinelenen değerleri de inceleyebilmek için 26'nın iki katıdır; 52 normal veya anatomik değer sınırı değildir. 53. kayıt varsa `input_limit_exceeded` engelidir, bütün koşullu incelemeler atlanmış sayılır ve kısmi listedeki eksiklik/tekrarlar tam girdi hakkında hüküm gibi sunulmaz. Birim hataları ve sürekli `Unverified` durumu korunur. Tam 52 kayıtta mevcut tekrar/hata semantiği sürer. Bu sınır, çağrılan bir `MoveNext` veya `GetEnumerator` işleminin kendi içinde sonsuza kadar beklemesini ya da kötü niyetli kod çalıştırmasını önlemez; dış sınır ve iptal mekanizması ayrıca tasarlanmalıdır.

Sınır yalnız `MeasurementConsistencyAudit.Assess` için geçerlidir; `MeasurementContractAudit.Assess`, `ResearchMeasurements.Create` veya başka giriş noktalarına doğrudan çağrı yapıldığında otomatik uygulanmaz. İlerideki bir dış giriş yolu kendi ayrıştırma, istek boyutu ve iptal sınırlarını sağlamalı ve hangi katmanı çağırdığını açıkça denetlemelidir.

Üç arka bölüm/toplam ilişkisi, iki model açı sınırı, omuz formülü ve türetilen uzunluk kapsamı, kendi bağımlılıkları ve ilgili birim geçerliyse başka ilgisiz alan eksikken de ayrı sınanır. Tam girdide aynı hata iki kez üretilmez. Tekrarlı veya teknik aralık dışı bağımlılık kullanılmaz; yanlış birim için karşılaştırma yapılmaz. Bunlar önceki model kapsamıyla aynı kapılardır, yeni anatomik ret kuralları değildir.

Ayrı `ReviewIssues`, aşağıdaki **koşullu tutarlılık sinyalleridir**; ölçüm yolları, işaretler veya poz bu varsayımlarla eşleşmeyebilir. Bu nedenle tek başlarına `Blocking` değildir ve hastalık, yanlış beden veya üretilebilirlik kararı vermez. `NeedsReview`, yalnız üretilen bu sinyallerin varlığını bildirir. `SkippedReviewCodes` ve `ReviewCoverageComplete`, eksik, yinelenmiş, geçersiz bağımlılıklardan veya yanlış uzunluk biriminden ötürü hesaplanamayan ilişkileri gösterir; hiç sinyal olmaması bütün kontrollerin yapıldığı ya da ölçülerin doğru olduğu anlamına gelmez.

Her ilişki yalnız kendi iki alanı tek, sayısal teknik sınırda ve uzunluk birimi açıkça `cm` ise değerlendirilir. İlgisiz başka bir eksik alan veya yalnız açı birimi hatası, bu iki alan arasındaki uyarıyı gizlemez. Hiçbir karşılaştırmada sayı ortalaması, tahmini veya otomatik düzeltme yapılmaz. Eşitlik uyarı üretmez; yalnız belirtilen katı `>` ilişkisi uyarı üretir. Sınırların susması geçerlilik sertifikası değildir.

## Koşullu ilişki tablosu

| Kod | İncelenecek ilişki | Yorum ve sınır |
|---|---|---|
| `review_bust_points_vs_circumference` | `2 × bust_points > bust` | Noktalar aynı kapalı ölçü eğrisi üzerindeyse ve mesafe doğrusal ise çevrenin yarısından uzun kiriş çelişkilidir. Kaynak `bust_points` ölçüm yolunun gerçek kişide uygulanışı ayrıca doğrulanmadığından yalnız inceleme. |
| `review_bum_points_vs_circumference` | `2 × bum_points > hips` | Aynı kapalı eğri/doğrusal mesafe varsayımında benzer geometrik sinyal; kalça işaretleri ve mesafe yolu henüz doğrulanmadı. |
| `review_front_path_order` | `bust_line > waist_over_bust_line` | Kaynakta göğüs hattı uzunluğu ön bel–göğüs yoluyla ilişkilidir; ancak yüzey/konveks yol ve uçlar aynı protokolle doğrulanmadığından otomatik ret değil. |
| `review_head_vs_height` | `head_l > height` | Ense–tepe mesafesi ile boy arasındaki alışılmadık ilişki; iki ölçünün sınır noktaları ve pozları doğrulanmalı. |
| `review_vertical_bust_vs_height` | `vert_bust_line > height` | Aynı dikey eksen/poz varsayımıyla düzey farkı tüm boyu aşıyor; ölçüm yolu kontrol edilmeli. |
| `review_hips_line_vs_height` | `hips_line > height` | Aynı dikey eksen/poz varsayımıyla bel–kalça düzey farkı boyu aşıyor; kaynak işaretleri ve birim gözden geçirilmeli. |
| `review_neck_vs_shoulders` | `neck_w > shoulder_w` | Boyun tabanı genişliği ile omuz uçları uzaklığı farklı ölçüm yollarına sahip olabilir; yalnız gözden geçirme sinyali. |
| `review_armhole_vs_back_length` | `armscye_depth > waist_line` | Omuz–koltuk altı ve ense–bel yollarının farklı başlangıçları vardır; yalnız gözden geçirme sinyali. |

Bu sinyaller kaynakta birebir tanımlanmış kabul eşikleri değildir; kaynak ölçüm açıklamalarından türetilmiş, açık varsayımlı araştırma kontrolleridir. [Sabit GarmentCode ölçü PDF'si](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/docs/Body%20Measurements%20GarmentCode.pdf) ilgili alan ve birim kavramlarını verir. [GarmentCodeData ekindeki 3B ağ yöntemi](https://igl.ethz.ch/projects/GarmentCodeData/GarmentCodeData-A-Dataset-of-3D-Made-to-Measure-Garments-with-Sewing-patterns-ECCV-2024-Korosteleva-et-al.pdf#page=20) bazı mesafeleri Öklid uzayı ve bazı çevreleri ağ kesitinden hesaplar; bu, gerçek bir mezura protokolüyle eşdeğerlik kanıtı değildir. [NHANES antropometri kılavuzu](https://wwwn.cdc.gov/nchs/data/nhanes/public/2021/manuals/2021-Anthropometry-Procedures-Manual-508.pdf) standartlaştırılmış duruş ve işaretleme örneği sunar, ancak onun bel seviyesi GarmentCode'un seviyesi yerine otomatik kullanılamaz. Ayrıntılı alan kanıtı ve açık noktalar [ölçüm kanıtı matrisindedir](MEASUREMENT_EVIDENCE_TR.md).

[A-kesim alt panel eşleme incelemesi](ALINE_MEASUREMENT_MAPPING_TR.md), benzer görünen bel/kalça yaylarının hangi varsayımlarla ilişkilendirilebileceğini ve 20 kaynak alanda bulunmayan altı tasarım girdisini gösterir; otomatik dönüşüm yapmaz.

## Test ve kapsam

Dört sentetik beden; sekiz ilişkinin eşik altı/eşit/üstü; her bağımlılıkta null, tekrar, teknik sınır dışı değer ve yanlış uzunluk birimi test edilir. İlgisiz eksik alan veya açı birimi hatası varken bağımsız uyarının korunması, ikinci bağımlılık geçersizken karşılaştırmanın atlanması, giriş sırasından bağımsız rapor sırası, üretim kapıları ve kompozisyon reddi sınanır. Mevcut 20 alanlı kapsam ve birim testleri ayrıca çalışır. Bunlar gerçek ölçü doğruluğu veya prova testi değildir.
