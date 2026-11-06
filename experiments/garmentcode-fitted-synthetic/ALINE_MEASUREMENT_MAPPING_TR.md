# 20 kaynak ölçüsü ile A-kesim alt panel girdileri

Bu yalnız kod ve kaynak tanımı incelemesidir; bir dönüşüm uygulanmadı ve gerçek kişi verisi kullanılmadı. GarmentCode araştırma yolu 20 ham alanı cm/derece ister. [A-kesim alt paneli](../../src/TekstilCAD.Patterns/ALinePanelPreview.cs) ise mm cinsinden 11 bağımsız sayı alır. A-kesim paneli bir alt kesit önizlemesidir; üst beden, elbiseye birleşim, pens dağılımı, dikiş payı veya üretim çıktısı değildir (`CanApplyOrExport=false`).

| A-kesim alanı | Olası ilişki | Eksik kanıt / veri |
|---|---|---|
| `BackWaistArc` | `waist_back_width × 10` mm adayı | Kaynak arka bel çevresi parçası ve A-kesim yayının aynı bel düzeyini, yan sınırları ve yolu kullandığı gösterilmedi. |
| `FrontWaistArc` | `(waist − waist_back_width) × 10` mm adayı | Aynı sınırlar ve tam ön/arka bölümleme varsayımı doğrulanmalı; kaynak adı bu dönüşümü A-kesim giriş sözleşmesi için otomatik onaylamaz. |
| `BackHipArc` | `hip_back_width × 10` mm adayı | Aynı kalça düzeyi, yan işaretler ve yüzey yolu henüz eşlenmedi. |
| `FrontHipArc` | `(hips − hip_back_width) × 10` mm adayı | Aynı çevre yolu ve ön/arka bölümleme varsayımı doğrulanmalı. |
| `HipDepth` | `hips_line × 10` mm adayı | Kaynak düşey düzey farkının A-kesim kesitindeki y-eksenine ve aynı bel/kalça işaretlerine karşılık geldiği doğrulanmadı. |
| `FrontWaistEase`, `BackWaistEase`, `FrontHipEase`, `BackHipEase` | 20 ham beden alanında karşılığı yok | Dört ayrı bolluk/tasarım kararı gerekir; sıfır veya başka sayı tahmin edilemez. |
| `WaistToHem` | 20 ham beden alanında karşılığı yok | İstenen model ve boy için ayrı açık girdi gerekir. |
| `FinishedHemCircumference` | 20 ham beden alanında karşılığı yok | İstenen bitmiş etek çevresi ayrı tasarım girdisidir. |

Yukarıdaki dört yay ve kalça derinliği yalnız koşullu **eşleme adaylarıdır**; açık kullanıcı yetkisiyle bile ölçüm protokolü, birim ve yapı eşdeğerliği kanıtlanmadan otomatik eşleme yapılmamalıdır. `cm × 10` yalnız birim dönüşümüdür ve anlamsal farkı çözmez. Etek genişliği, bolluk, pens ve bel bağlantısı toplam beden ölçülerinden güvenilir biçimde türetilemez.

[ALineMeasurementReview](../../src/TekstilCAD.Patterns/ALineMeasurementReview.cs) 11 metin girdisini açık seçilen cm/mm birimine göre normalize eder; bu, 20 alanın GarmentCode sözleşmesiyle kaynak eşdeğerliği veya fiziksel prova denetimi değildir. Alt panel, ön ve arka yayları kendi içinde ikiye böler ve bitmiş etek çevresini eşit yarılara dağıtır; arka/ön ölçüler aynı kabul edilmez, ancak sağ/sol simetri varsayılır. Yan uzunluk farkı hesaplanır; dikiş eşitliği kanıtı değildir. Doğrulama katmanı fiziksel beden kabul aralığı belirlemez.

[Sentetik dikey kesit](../measurement-vertical-slice/README.md), ayrı 15 üst ve 11 alt alanı yalnız üç sabit uydurma örnekle kullanır; üst ve altın aynı bedeni veya ortak bel dikişini temsil ettiğini varsaymaz. Bu deneyin kapalı yolu ile masaüstündeki A-kesim önizleme kodunun ayrı varlıklar olduğu karıştırılmamalıdır. Birleşik elbise için ortak ölçüm protokolü ve snapshot, açık model/bolluk/boy girdileri, üst–alt bağlantı kimlikleri, çentik/dikiş uyumu ve bağımsız fiziksel prova hâlâ gereklidir.
