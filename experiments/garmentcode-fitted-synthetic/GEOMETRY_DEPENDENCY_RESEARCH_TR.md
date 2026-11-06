# 20 alanın geometri bağımlılığı ve gerçek girdiye geçiş engelleri

Bu, kaynak kod ve **tek uydurulmuş `synthetic-base` beden** üzerinde sonlu fark araştırmasıdır. İki örnekli geometri karşılaştırmalarının, gerçek bedenlerin veya fiziksel kalıp uygunluğunun yerine geçmez. [Yapılandırılmış bağlayıcı](STRUCTURED_GARMENT_INPUT_TR.md) iki sabit değer kombinasyonu dışında geometri başlatmaz; bir değerin kişiye ait olup olmadığını da tespit edemez. Bu çalışma gerçek veriyi, üretimi veya plotter'ı açmaz.

## Uygulanan analiz

[SyntheticDependencyProbe](SyntheticDependencyProbe.cs), 20 alanın her birini tek tek artırır: uzunluklar için **0,1 cm**, iki açı için **1 derece**. Diğer 19 değer aynı kalır. 20 alan, `ResearchMeasurements.Create(...).BuildPinnedGarment()` üzerinden altı C# panel adayına verilir; panel kimlikleri, kenar tipleri, temsil edilen uçlar ve göreli kontrol değerleri doğrudan karşılaştırılır. Snapshot'ın değişmesi her girdinin hash'e dahil olduğunu gösterir; geometri değişimi kararı hash'e değil bu kenar verilerine dayanır. Çıktı `unresolved`, `Accepted=false` ve üretim/dışa aktarma kapalıdır.

Ayrı [sabit Python kaynak yakalaması](capture_dependency_sweep.py) aynı değişimleri aynı kaynak commit'iyle uygular. Kaynak HEAD ve dosya özetleri, ayrıca başlangıçtaki altı panelin kenar tip/uç/kontrol verilerinin kayıtlı golden fixture ile tam eşitliği kontrol edilir. [Kaydedilen kaynak izi](source-dependency-sweep.json), bu ortamdaki NumPy/SciPy/Python sürümlerini ve değişen panel/azami temsil farklarını içerir; kaynak tekrar çalıştırılıp aynı sonuç gözlendi. Aynı kaynak/ortamın tekrarı ikinci bağımsız implementasyon veya tüm sürümler için garanti değildir. İşlem yerel panel konturlarını inceler; 3B panel yerleşimini, giysi grafiğinin fiziksel anlamını ve prova sonucunu ölçmez.

`F` iki ön gövde paneli, `B` iki arka gövde paneli, `ÖE` ön etek, `AE` arka etek demektir. Listedeki panel değişimi yalnız bu başlangıç noktası ve tek yönlü artış için gözlenmiştir. `Δ` bir sonuç boyutu değil, girdi değişikliğidir.

| Alan | Δ | C# değişen yerel konturlar | Python kaynakta değişen yerel konturlar |
|---|---:|---|---|
| `bust` | 0.1 | F | F |
| `back_width` | 0.1 | F, B, ÖE, AE | F, B, ÖE, AE |
| `waist` | 0.1 | F, ÖE, AE | F, ÖE, AE |
| `waist_back_width` | 0.1 | F, B, ÖE, AE | F, B, ÖE, AE |
| `bust_points` | 0.1 | F, ÖE | F, ÖE, AE |
| `shoulder_w` | 0.1 | F, B | F, B |
| `shoulder_incl` | 1 | F, B, ÖE, AE | F, B, ÖE, AE |
| `waist_line` | 0.1 | F, B, ÖE, AE | F, B, ÖE, AE |
| `waist_over_bust_line` | 0.1 | F | F |
| `bust_line` | 0.1 | F, B | F, B |
| `vert_bust_line` | 0.1 | F, B | F, B |
| `bum_points` | 0.1 | B, ÖE, AE | B, ÖE, AE |
| `neck_w` | 0.1 | F, B | F, B |
| `armscye_depth` | 0.1 | F, B | F, B |
| `hips` | 0.1 | ÖE | ÖE, AE |
| `hip_back_width` | 0.1 | ÖE, AE | ÖE, AE |
| `hips_line` | 0.1 | ÖE, AE | ÖE, AE |
| `height` | 0.1 | ÖE, AE | ÖE, AE |
| `head_l` | 0.1 | ÖE, AE | ÖE, AE |
| `hip_inclination` | 1 | ÖE, AE | ÖE, AE |

Her 20 alanın bu örnekte en az bir yerel konturu değiştirdiği gözlendi; bu, tüm alanların her bedende etkili olduğunu veya alanların kaynakta **eksiksiz/ doğru** türetildiğini ispatlamaz. `bust_points` ve `hips` için Python tarafında ek `AE` değişimi `skirt_back` panelinde uç koordinatlarının azami farkı yaklaşık `1,77636×10⁻¹⁴ cm` idi; kontrol değerleri ve yapı değişmedi. Kayıt panel düzeyinde azami farkı tuttuğundan hangi kenar veya uçların değiştiğini kanıtlamaz. C#'ta aynı iki değişimde o panel birebir aynı kaldı. Bu küçük farkın optimizer/yuvarlama kaynaklı olduğu makul bir olasılıktır, ancak nedeni bu testle kanıtlanmadı. `1e−12 cm` yalnız bu iki gözlemin regresyon sınırıdır, fiziksel tolerans değildir. Diğer 18 alanın değişen panel **kümeleri** eşleşti; bu, geometri değerlerinin veya dikiş uzunluklarının birebir eşitliği demek değildir.

## Kaynak/C# işlevleri ve eksik kapılar

| Kaynak veya işlem | C# araştırma karşılığı ve sınır |
|---|---|
| [BodyParameters.eval_dependencies](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/bodies/body_params.py) | `ResearchBody.DeriveValues` yedi türevi hesaplar. `height - head_l - waist_line` ve diğer formüller kaynak program davranışıdır; fiziksel ölçüm tanımı doğruluğu değildir. |
| [BodiceFrontHalf / BodiceBackHalf](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/garment_programs/bodice.py) | `FrontBodiceCandidate`, `BackBodiceCandidate` altı panel yolunun gövdelerini üretir. Arka bel eğrisi ve eğriye pens kesme adayları kaynak BFGS çıktısının tam portu değildir; bilinen farklı optimizer örnekleri vardır. |
| [CurveEdgeFactory.curve_from_tangents](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/edge_factory.py) ve [cut_into_edge_single](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/operators.py) | C# teğet ve pens adayları ile `DartCut` seçili biçimleri hesaplar; SciPy BFGS, eğri boyunca optimizasyonlu kesim ve kaynakta oluşturulan ara durumların tamamı birebir taşınmamıştır. Başarı iddiası için daha geniş bağımsız kaynak karşılaştırması gerekir. |
| [FittedSkirtPanel / PencilSkirt](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/garment_programs/skirt_paneled.py) | `FrontSkirtCandidate` ve `BackSkirtCandidate` sabit tasarım adaylarıdır; eğri kontrolleri ve bel arayüzü eşleşmesi kaynak optimizasyonunun kanıtlanmış tam portu değildir. |
| `MetaGarment` / `StitchingRule` ve bileşen birleşimi | `BodySeamStudy` sabit 23 ilişkiyi C# panel adaylarına bağlar; iki kaynak fixture üzerinde kimlikler incelenmiştir. Bu, tüm Python tasarım dallarının veya fiziksel dikiş/çentik semantiğinin genel portu değildir. |
| Geometriyi düzeltme ve kaynak ölçüye bağlama | `CombinedWaistCurvatureExperiment.Create` yalnız sabit sentetik kimlikleri, `ShoulderSeamStudy` ve birleşik 23 kural yolu yalnız iki fixture'ı destekler. Yapılandırılmış `BindSynthetic` **düzeltilmiş birleşik adayı değil**, doğrulanmamış C# temel panel/graf tanısını çağırır. Bu düzeltmeler henüz rastgele geçerli ölçüye genellenmiş ve kaynağa karşı doğrulanmış değildir. |
| Kullanıcı ölçüsü, giysi ve prova | Alan tanım/protokolü, birim ve giriş güvenliği, tasarım/bolluk/boy/kumaş/kapama, gerçek çentik ve 3B–fiziksel prova ayrı kapılardır. [Ölçüm kanıtı](MEASUREMENT_EVIDENCE_TR.md) ve [ürün engelleri](REAL_PRODUCT_BARRIERS_TR.md) bunların açık olduğunu belirtir. |

Dolayısıyla bu taramada herhangi bir alan için “kaynak türetmesi tamamen eksik” veya “genel olarak doğru” hükmü verilmez. En etkili sonraki mühendislik adayı, **ölçüden üretilen arka bel/pens kaynak optimizeri ve düzeltilmiş konturun genel bedenlere bağımlılığını**, bağımsız kaynak vakaları ve belirsizlik/hata sınırlarıyla karşılaştırmaktır; iki sabit kimliğin ötesini otomatik üretime açmak değildir.

## Testler ve sınır

C# doğrulaması 20 alan/tekil kayıt/Δ, altı panel çıktısı, değişen snapshot ve kapalı üretim bayraklarını denetler; kaynak iziyle panel kümelerini karşılaştırır. Yinelenmiş alan, golden başlangıcı bozulması ve iki küçük farkın yapay büyütülmesi reddedilir. Kaynak yakalama, aynı ortamda yeniden çalıştırılarak kaydedilen sonuçlarla karşılaştırıldı. Bu karşılaştırmalar tek sentetik bedende iki çalıştırmanın gözlemidir; kimlik/kapsam hataları, optimizer başarısızlıkları, tüm olası uç değerler, gerçek ölçü alma, dikiş payı, kumaş veya prova doğrulaması değildir.

İz ve golden fixture C# doğrulamasında sabit SHA-256 özetlerine bağlıdır; Python yakalaması da fixture özetini ve kaynak HEAD/dosya manifestini kontrol eder. Özet, sürümlenmiş gözlemin değiştirilmediğini kontrol eder; kaynağın bu ortamda gerçekten çalıştırıldığını bağımsız olarak kanıtlayan imza veya köken belgesi değildir. C# baseline bayraklarını kendisi geometriyle yeniden hesaplamaz. İz veya fixture bilinçli güncellenirse sabit özetlerin ve ilgili gözlemlerin ayrıca incelenmesi gerekir.

## Ek araştırma: farklı sentetik bedenlerde arka bel ve pens

[Kaynak yakalaması](capture_back_multibody.py), sabitlenmiş Python checkout ve dosya manifesti üzerinden beş uydurma ölçü kombinasyonunun sağ arka panelini üretir. [İz](back-multibody-source.json) C# doğrulamasında sabit özete bağlıdır. C# testi aynı ölçülerle `BackBodiceCandidate.CreateFromBody` çağırır; ilk yedi bel/pens kenarının sırasını, türünü, uçlarını ve eğri göreli kontrollerini kaynakla karşılaştırır. Aşağıdaki uç farkı koordinat düzleminde iki karşılık gelen uç arasındaki en büyük Öklid uzaklığı, kontrol farkı ise boyutsuz göreli kontrol uzaklığıdır. Bunlar kaynak ve aday arasındaki farklardır; tolerans veya fiziksel uygunluk sınırı değildir.

| Sentetik örnek | Omuz açısı | En büyük uç farkı (cm) | En büyük göreli kontrol farkı |
|---|---:|---:|---:|
| angle5 | 5° | 0,00004595 | 0,00003010 |
| angle18_narrow | 18° | 0,00018882 | 0,00003900 |
| angle30 | 30° | 0,01428234 | 0,00207226 |
| angle35_wide | 35° | 0,04621207 | 0,00612745 |
| angle40 | 40° | 0,12480602 | 0,01502485 |

Dar ve geniş örnekler ayrıca sırt genişliği, arka bel genişliği ve kalça noktasını değiştirir. Önceki sekiz açılık optimizer izinde, aynı teğet yönünü sağlayan kontrol noktalarının yarıçapı Python BFGS sonucunda değişiyordu. Başlangıç x koordinatını sabit tutan başka bir analitik teğet seçimi 30° ve 40° farkını azaltsa da 10°, 18° ve 22° kontrol farklarını artırdı. Bu nedenle o seçim bir optimizer portu veya genel düzeltme değildir. Kontur farkları yalnız teğet optimizasyonundan kaynaklanıyor diye de çıkarım yapılamaz; pens kesme ve eğri uzunluğu optimizasyonları da farklıdır.

Bu çalışma farkı genel olarak gidermedi. SciPy BFGS ve çizgi aramasının davranışı ile eğriye pens kesiminin bağımsız taşınması ve daha geniş, eğitim dışı sentetik karşılaştırmaları sonraki açık iştir. İz, yerel kaynak çalıştırmasının kaydıdır; özet imzalı köken kanıtı değildir. Gerçek ölçü, üretim, dışa aktarma ve plotter kapıları değişmedi.

Beş örneğin her biri için C# testi, tabloda gözlenen uç ve göreli kontrol farklarını ayrı araştırma regresyon aralıklarıyla izler. Uç ve kontrol koordinatlarını ayrı ayrı bozan adaylar her örnekte reddedilir. Farkın azalması dahil aralık dışına çıkılması, kaynağın ve raporun yeniden değerlendirilmesini gerektirir. Bu aralıklar üretim, fiziksel kalıp veya kaynak eşdeğerliği toleransı değildir; fixture dışındaki bedenlere genellenemez.
