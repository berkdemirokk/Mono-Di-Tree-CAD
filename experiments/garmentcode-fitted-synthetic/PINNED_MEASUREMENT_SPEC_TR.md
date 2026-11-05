# Sabit tasarımın ölçü sözleşmesi: kaynak incelemesi

Bu belge **kişiye ölçü aldırma talimatı değildir**. [Sabitlenmiş GarmentCode ölçü PDF’sinin](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/docs/Body%20Measurements%20GarmentCode.pdf) alan açıklamalarını, [beden türevlerini](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/bodies/body_params.py) ve bu araştırmada seçili kolsuz üst/kalem etek hesaplarını eşler. İnsan üzerinde işaretleme, duruş, araç ve tekrarlanabilirlik bağımsız doğrulanmadı; bu eksikler giderilmeden gerçek ölçü toplama açılmaz. Sayısal müşteri örneği yoktur.

Kaynak uzunlukları **cm**, açıları **derece** kabul eder. Aşağıdaki 20 alan yalnız seçili tasarımın mevcut araştırma yolu içindir; farklı tasarım, kol veya beden modellerinin alanları farklı olabilir. `_` ile başlayan türevler girilmez veya kullanıcıdan istenmez. Tablo, kaynağın söylediğini özetler; uygulayıcı için doğrulanmış bir prosedür iddiası taşımaz.

| Alan | Kaynağın ifade ettiği kavram | Özellikle açık kalan nokta |
|---|---|---|
| `bust` | Göğüs noktalarından geçen, zemine paralel çevre | Noktaların ve duruşun tekrarlanabilir işaretlenmesi |
| `back_width` | Göğüs çevresinin iki yan denge çizgisi arasındaki arka bölümü | Yan çizgilerin kişide işaretlenmesi ve ölçüm yolu |
| `waist` | Kaynakta kaburga ve kalça kemikleri arası yaklaşık orta çevre | Bel seviyesinin kişide kesin ve tekrarlanabilir seçimi |
| `waist_back_width` | Bel çevresinin iki yan denge çizgisi arasındaki arka bölümü | Yan çizgilerin ve bel seviyesinin işaretlenmesi |
| `bust_points` | İki göğüs çıkıntı noktası arasındaki mesafe | Nokta işaretleme ve mesafe yolu |
| `shoulder_w` | Köprücük kemiklerinin en dış uçları arasındaki mesafe | Uçların işaretlenmesi ve ölçüm yolu |
| `shoulder_incl` | Koronal düzlemde boyun tabanı–köprücük kemiği ucu hattının yatay düzleme eğimi | Başlangıç/bitiş işaretleri, yön ve duruş |
| `waist_line` | Ense–bel arka mesafesi; kaynak yüzey üzerindeki mesafeyi tercih eder | Ense ve bel işaretleri ve yolun uygulanması |
| `waist_over_bust_line` | Denge çizgisindeki boyun tabanından göğüs noktası üzerinden ön bele; kaynak jeodezik ölçünün dışbükey zarfını anlatır | Boyun başlangıcı ve yolun tekrarlanabilir uygulanması |
| `bust_line` | Omuz düzeyinden göğüs noktasına, önceki hat boyunca yüzey mesafesi | Omuz düzeyi ve noktanın işaretlenmesi |
| `vert_bust_line` | Ense düzeyiyle göğüs çevresi düzeyi arasındaki düşey mesafe | Duruş ve düzey işaretleri |
| `bum_points` | İki kalça çıkıntısı arasındaki mesafe | Çıkıntıların işaretlenmesi ve mesafe yolu |
| `neck_w` | Boyun tabanının arkadan bir denge çizgisinden diğerine yaklaşık genişliği | Uçların işaretlenmesi ve gerçek ölçüm yolu belirsiz |
| `armscye_depth` | Arkadan omuz–koltuk altı mesafesi | Omuz ve koltuk altı noktaları, yol ve duruş |
| `hips` | Kalça çıkıntılarından geçen, zemine paralel çevre | Çıkıntıların ve seviyenin işaretlenmesi |
| `hip_back_width` | Kalça çevresinin iki yan denge çizgisi arasındaki arka bölümü | Yan çizgilerin ve kalça seviyesinin işaretlenmesi |
| `hips_line` | Bel ve kalça çevresi düzeyleri arasındaki düşey mesafe | Duruş ve düzeylerin belirlenmesi |
| `height` | Ayaklardan baş tepesine boy | Duruş ve sınır noktalarının standartlaştırılması |
| `head_l` | Ense–baş tepesi arasındaki düz mesafe | Ense noktasının işaretlenmesi |
| `hip_inclination` | Yanda bel–kalça açısı; dikey yan kaynakta 0° | İşaretlenen noktalar, işaret yönü ve uygulama |

Kaynak, yan denge çizgisini koronal düzlemde beden yanından geçen dikey çizgi olarak açıklar. Üç arka genişlik ilgili çevrenin arka **parçalarıdır**; düz kiriş varsayılmaz. Kaynak `neck_w` için iki denge çizgisini başlangıç ve bitiş olarak belirtir; ölçüm yolunu ve `bum_points` noktalarının nasıl işaretleneceğini gerçek kişilerde uygulamaya yetecek açıklıkta belirlemez. Bu tasarımda `arm_pose_angle` istenmez; kaynakta bu açının ekseni de ayrıca belirsizdir.

## İzole doğrulama sözleşmesi

`MeasurementContractAudit.Assess`, önceden ayrıştırılmış `decimal?` değerleri ve açık birimleri bellekte inceler. Uygulamada ölçü formu, metin ayrıştırıcısı, otomatik birim dönüşümü, dosya veya ağ akışı değildir. Tekrarlanan alan, bilinmeyen/`_` alanı, eksik değer, yanlış birim ve teknik sayı aralığı ayrı hata kodları üretir. Alanlar tam olsa bile ölçüm protokolü ve fiziksel prova **doğrulanmamış** kalır.

| Kod | Araştırma denetiminin anlamı |
|---|---|
| `missing_measurement` | Bu tasarımın gerekli ham alanı yok veya bilinmiyor; tahmin yapılmaz. |
| `length_unit_not_cm`, `angle_unit_not_degree` | Açık birim bu araştırma sözleşmesiyle uyuşmuyor; otomatik dönüşüm yok. |
| `duplicate_field`, `unknown_or_derived_field` | Belirsiz tekrar veya desteklenmeyen/türetilmiş girdi reddedilir. |
| `outside_technical_range` | Sayı teknik sınırın dışında; bu sınır anatomik kabul aralığı değildir. |
| `unsupported_back_bust_relation`, `unsupported_back_waist_relation`, `unsupported_back_hip_relation` | Arka çevre bölümü ilgili toplamdan küçük olmalıdır koşulu mevcut modelde sağlanmıyor. |
| `unsupported_shoulder_angle`, `unsupported_hip_angle`, `unsupported_sleeve_balance` | Kaynak formüllerini kullanan mevcut araştırma kapsamı dışında; anatomik tanı değildir. |
| `unsupported_derived_length` | Kaynak formülünden türetilen yükseklik/bacak uzunluğu bu araştırmada pozitif değil; ölçümün yanlış olduğu kanıtlanmaz. |
| `measurement_protocol_unverified`, `landmarks_and_paths_unverified`, `fit_and_physical_proof_missing` | Her durumda süren ölçüm ve prova belirsizlikleri. |

Teknik uzunluk sınırı `0 < x ≤ 1000 cm`, teknik açı sınırı `0 ≤ x ≤ 180°`; ayrıca mevcut formül kapsamı `shoulder_incl < 45°`, `hip_inclination < 89°`, `shoulder_w > 2 cm` ve üç arka bölümün ilgili toplamdan küçük olmasını ister. Bu sınırlar kaynaktan türetilmiş veya araştırma uygulamasına özgü teknik kapılardır; **insan bedeni için makul aralık, doğru ölçüm ya da üretim uygunluğu değildir**. `height - head_l - waist_line - hips_line` formülü özellikle yüzey uzunluğunu içerebildiği için pozitiflik reddi bir kapsam sınırlamasıdır.

Daha sonra uygulanacak gerçek metin girişi için ondalık ayracı, hassasiyet/kayıpsız dönüşüm, boş/uydurulmuş değerler, açık birimler, veri saklama yetkisi, ölçüm protokolü ve farklı kişilerle prova henüz tasarlanıp doğrulanmalıdır. Rapor ham sayıları içermez, ancak alanlar ve hata kodları kişisel veriye ilişkin bilgi verebilir; anonimlik garantisi yoktur. Bütün durumlarda üretim, dışa aktarma ve plotter kapalıdır.
