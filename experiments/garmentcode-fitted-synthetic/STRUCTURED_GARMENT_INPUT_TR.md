# Yapılandırılmış ölçü ve model sözleşmesi — sentetik araştırma

[StructuredGarmentInput](StructuredGarmentInput.cs), GarmentCode'un sabit kolsuz üst + kalem etek araştırma yolunun 20 kaynak ölçüsünü, bir modeli ve A-kesim seçilirse ayrıca alt panel bildirimini değerlendirir. Bu **metin/ses ayrıştırıcısı, müşteri formu veya gerçek kalıp üreticisi değildir**. Sayılar önceden `decimal?` olarak sağlanır; anlaşılmayan veya bilimsel olarak kanıtlanmamış bir ölçü tahmin edilmez. Gerçek kişi ölçüleri bu çalışmada kullanılmadı; yöntem çağrılırsa sağlanan sayıları bellekte işler ve gerçek/sentetik olduklarını `Assess` aşamasında ayıramaz. `PersonalDataCollectionEnabled=false` veri tespit sistemi veya gizlilik garantisi değildir; bu deneyde toplama arayüzü bulunmadığını ifade eder.

## Bir alan bildiriminin yapısı

Her alan için ayrı `Field`, `Value`, `Unit` ve `DefinitionId` gerekir. 20 `Field` ismi [sabit ölçü listesiyle](PINNED_MEASUREMENT_SPEC_TR.md) aynıdır; bu yol dışındaki alan bilinmeyen sayılır. `Value` null/eksik ise değer uydurulmaz. Uzunluk için `Unit="cm"`, `shoulder_incl` ve `hip_inclination` için `Unit="degree"` gerekir; örtük birim veya otomatik dönüşüm yoktur. Her alanın tanım kimliği tam olarak şu biçimdedir:

`garmentcode:d449629979028123a5c4dc9e732a2ec19b7fce31:<Field>:v1`

Bu yerel sürüm etiketi, hangi [kaynak PDF](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/docs/Body%20Measurements%20GarmentCode.pdf) alanının kastedildiğini açıklar; ölçümü alan kişinin tanıma uyduğunun doğrulanması, bilimsel standart veya insan üzerinde tekrarlanabilir protokol değildir. Özellikle `waist_back_width`, `shoulder_incl` ve `waist_over_bust_line` için açık kalan işaret/yol farkları [ölçüm kanıtı matrisindedir](MEASUREMENT_EVIDENCE_TR.md). Kimliğin yanlış veya eksik olması ayrı hata üretir ve o değer ilişki hesaplarına verilmez.

Eksik, tekrarlı, bilinmeyen, yanlış birimli veya yanlış tanımlı alanlar, mevcut [deterministik teknik ve koşullu tutarlılık denetimi](MEASUREMENT_CONSISTENCY_TR.md) ile birlikte raporlanır. Aynı değerin iki kez verilmesi de tekrardır. Koşullu anatomik sinyaller kişisel teşhis veya otomatik ret gerekçesi olarak sunulmaz; sentetik bağlama denemesi bunları göz ardı etmez. Atlanan ilişki denetimleri açıkça listelenir. Rapor alan isimleri ve hata kodları içerir, sayıları rapor metnine koymaz; bu tek başına anonimlik veya depolama güvenliği garantisi değildir.

Girdi dizisinden en fazla 40 öğe saklanır; 41. öğe yalnız taşmayı saptamak için istenir. Taşmada `input_limit_exceeded` döner, kısmi ölçülerden sonuç üretilmez ve ölçü tutarlılığı sonucu yoktur. Sınırlı tüketim `GetEnumerator`/`MoveNext` içinde bloke olan veya keyfi kod çalıştıran bir üreticiyi durduramaz; dış istek boyutu, ayrıştırma, süre ve iptal sınırları ayrı tasarım gerektirir. `decimal` tipi NaN/sonsuz sayı taşımaz; serbest metin/JSON hassasiyet ve sözdizimi denetimi bu katmanın dışındadır.

## Model ve koşullu bilgi

Model açıkça seçilir: `research_fitted_shirt_pencil_skirt_v1` yalnız sabit kaynak araştırma tasarımını ifade eder; boş veya bilinmeyen model ayrı hatadır. Kullanıcının yalnız ölçü söylemesi bir elbise silueti, uzunluk, bolluk, kumaş veya kapanma seçimi olarak yorumlanmaz.

`a_line_lower_preview_v1` seçilirse alt panel için cm birimiyle **11 ayrı** bildirim sorgulanır: ön/arka bel yayı, ön/arka kalça yayı, bunların dört ayrı bolluğu, kalça derinliği, belden etek boyu ve bitmiş etek çevresi. Sıfır bolluk yalnız açıkça `0` verilirse vardır; boş değer tahmin edilmez. Yöntem ayrıca A-kesim önizlemesinin teknik kalça derinliği/boy, ön-arka bel/kalça ve bitmiş etek çevresi koşullarını, yalnız ilgili değerler ve birim geçerliyse değerlendirir. Bu koşullar bedenin anatomik geçerliliği değildir.

Tüm 11 sayı verilse bile `aline_source_mapping_unverified` kalır: A-kesim yayları ile GarmentCode `waist_back_width`, `hip_back_width` ve düşey seviyelerin ölçüm yolları/yan sınırları aynı protokolle doğrulanmış değildir. 20 alandan otomatik çıkarım yapılmaz; ayrıntılar [eşleme incelemesinde](ALINE_MEASUREMENT_MAPPING_TR.md). A-kesim dışı seçime yanlışlıkla eklenen alt bildirim de sessizce yok sayılmaz. Belirsiz kumaş, dikiş payı, gerçek çentik, kapama ve prova bu model bildiriminden çıkarılamaz.

## Geometriye yalnız sentetik bağlama

`BindSynthetic`, yalnız `synthetic-base` veya `synthetic-distinct` kimliği, sabit model, tamamlanmış açık birim/tanım bildirimi ve beklenen 20 sayının **tam decimal eşitliği** ile çalışır. Aynı binary64'e yuvarlanabilecek farklı bir decimal de reddedilir. Eksik/model dışı/tutarlılık hatası veya koşullu gözden geçirme varsa bağlama yapılmaz. Geçen değerler `ResearchMeasurements.Create(...).BuildPinnedGarment()` yoluna aktarılır; aynı snapshot üzerinden C# `BodySeamStudy` 23 kuralı ve `BodyCurveStudy` altı kapalı, çözümlenmiş temsil konturu hesaplanır. Bu metot tek başına kaynak golden fixture'ını karşılaştırmaz; fixture kimlik ve kaynak dikiş karşılaştırmaları mevcut ayrı doğrulama akışındadır.

Dönen `SyntheticBinding` yalnız sentetik kimlik, snapshot ve sayıları içerir; panel/dikiş geometrisini veya kalıp çıktısını içermez. `Accepted=false`, `Status=unresolved`, dışa aktarma/üretim kapalıdır; kompozisyon çağrısı hata verir. Bu bağlama simülasyonun kaynakla tam eşdeğerliği, gerçek ölçüm doğruluğu, kumaş, çentik veya fiziksel giyim onayı değildir. Gerçek kişi değerleri `BindSynthetic` ile geometrik hesaba geçirilmez.

## Otomatik sınamalar

İki sentetik örnek ve ters giriş sırası aynı body snapshot'ını üretir. 20 alanın her birinde eksik, null, tekrar, yanlış birim ve yanlış tanım ayrı sınanır; başka alanın geçerli tanım kimliğinin kullanılması, kapsam dışı alan ve binary64'te aynı sayıya yuvarlanabilecek farklı decimal reddedilir. Model eksikliği/uyumsuzluğu, A-kesimde 11 eksik alan, sıfır bolluk, hatalı birim/sınır ve ilişkiler; tam 40 giriş, 41. öğe ve sonsuz üreticinin serbest bırakılması test edilir. Geometri başlatılan örnekler yalnız iki sabit sentetik fixture'dır. Gerçek ölçü alma, gerçek bedende doğrulama, üretim, çıktı ve plotter kapsam dışıdır.
