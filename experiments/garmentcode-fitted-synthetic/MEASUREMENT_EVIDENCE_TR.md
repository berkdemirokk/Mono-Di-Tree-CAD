# Ölçüm kanıtı matrisi — sabit araştırma modeli

**İnceleme tarihi:** 6 Kasım 2026. Bu belge kullanıcıya ölçü alma talimatı değildir. `PinnedGarment` yolundaki 20 alan [ResearchMeasurements.cs](ResearchMeasurements.cs) üzerinden belirlenmiştir; 26 alan isteyen ayrı `CompleteCurrentBody` yolu ve türetilen `_` alanları aynı kapsam değildir. Uzunluk birimi cm, açı birimi derecedir. Aşağıdaki “kaynak tanımı” GarmentCode’un ne demek istediğine dair kanıttır; gerçek kişilerde geçerli, tekrarlanabilir uygulama veya beden uyumu kanıtı değildir. Her alan gerçek ölçüm protokolü bakımından **açık** kalır.

## Kaynaklar ve okuma sınırları

- **[G]** Sabitlenmiş GarmentCode [ölçü tanımları](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/docs/Body%20Measurements%20GarmentCode.pdf), [body_params.py](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/bodies/body_params.py), [bodice.py](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/garment_programs/bodice.py), commit `d449629979028123a5c4dc9e732a2ec19b7fce31`. Özgün PDF'nin yerel araştırma checkout'undaki kopyası metin ve görsel olarak incelendi: tek sayfalık bir tablo, ek şekil yok. Dosya Git blob kimliği `1bcbbeb82c8a981263c4b72105be1c702f2d3547`, sabit commit ağacındaki blob ile aynı; SHA-256 `c27136ee91293755f28526e666a5b2989fb9068e591ee26baa737fed7e071930`. Bu eşleşme imzalı kaynak doğrulaması değildir. Tablodaki 20 G tanımı doğrudan PDF ile karşılaştırıldı; her alanın sağ sütunundaki uygulama ayrıntıları açık kalır. Kod formülü ölçüm yöntemini tek başına kanıtlamaz.
- **[D]** Korosteleva ve diğerleri, *GarmentCodeData*, ECCV 2024, [makale s. 6–7 ve ek s. 19–20](https://igl.ethz.ch/projects/GarmentCodeData/GarmentCodeData-A-Dataset-of-3D-Made-to-Measure-Garments-with-Sewing-patterns-ECCV-2024-Korosteleva-et-al.pdf#page=20), [DOI](https://doi.org/10.1007/978-3-031-73027-6_7). Bu daha sonraki çalışma **şablon 3B beden ağından otomatik ölçüm** tanımlar; sabit kod sürümünün elle ölçüm protokolü veya aynı girdi dağılımı olduğunu kanıtlamaz. Özellikle çevrede düzlemsel kesitin dışbükey zarfı ve arka bölümün koronal düzlemle ayrılması kullanılır; bunlar cilt üstü mezura ile aynı oldukları gösterilmeden eşitlenemez.
- **[N]** CDC/NCHS [NHANES Anthropometry Procedures Manual 2021](https://wwwn.cdc.gov/nchs/data/nhanes/public/2021/manuals/2021-Anthropometry-Procedures-Manual-508.pdf), §§3.4.3, 3.4.8–3.4.9: boy, iliak tepeye göre bel, kalça ve duruş örnekleri. NHANES bel referansı, G’deki kaburga–kalça arası yaklaşık orta bölge ile aynı olmak zorunda değildir; değiş tokuş için kanıt yoktur. Klinik protokolün pozisyonu da bu model için doğrudan kabul edilmez.
- **[R]** Kuehnapfel ve diğerleri, [*Reliability of 3D laser-based anthropometry and comparison with classical anthropometry*](https://www.nature.com/articles/srep26672), Scientific Reports, 2016, DOI [10.1038/srep26672](https://doi.org/10.1038/srep26672). Çalışmada bazı omuz ölçüleri ve açılarında düşük tekrarlanabilirlik bulunmuştur; bu deneyin spesifik alanı için hata sınırı değildir. [Sistematik derleme](https://pmc.ncbi.nlm.nih.gov/articles/PMC8230172/) yöntem ve duruş farklarını ayrıca tartışır.

## Alan bazında kanıt ve açık noktalar

| Alan | Kaynağın tanımladığı / hesapladığı | Gerçek ölçü için açık nokta |
|---|---|---|
| `bust` | G: göğüs noktalarından geçen yatay çevre; D: yatay ağ kesiti, dışbükey zarf ve yerel maksimum | Nefes, duruş, basınç, nokta ve ağ–mezura eşdeğerliği |
| `back_width` | G: göğüs çevresinin yan denge çizgileri arası arka kısmı; D: yan işaretlerden arka zarf bölümü | Yan işaretler ve kavisli yol; düz genişlik değildir |
| `waist` | G: bel çevresi; D: ağ kesitinde yerel minimum; N: iliak tepe üzerindeki yatay çevre | Bu farklı seviye tanımları eşdeğer kanıtlanmadı |
| `waist_back_width` | G: bel çevresinin iki yan denge çizgisi arasındaki arka bölümü; D: koronal düzlem arkasındaki zarf parçası | Bel seviyesi ve iki yan işaretin canlı bedende seçimi; düz kiriş veya belin yarısı değildir |
| `bust_points` | G: iki göğüs çıkıntısı arası; D: iki noktanın Öklid mesafesi | İşaretleme, araç, basınç ve G ile D yol eşdeğerliği |
| `shoulder_w` | G: köprücük kemiklerinin dış uçları arası; D: iki omuz işareti arası Öklid uzaklığı | Anatomik nokta eşleşmesi, düz ölçüm ve duruş; R omuz ölçümlerinde değişkenlik gösterir |
| `shoulder_incl` | G: boyun–omuz hattının yataya eğimi; D: boyun ve omuz işaretleri vektörünün yatay düzleme açısı; kod `_shoulder_incl` olarak aktarır | Noktaların tanımı, hangi taraf, düzlem/projeksiyon ve işaret yönü; R’de omuz açısı güvenilirliği sınırlı |
| `waist_line` | G: ense–bel arka mesafesi; beden yüzeyindeki jeodezik yol tercih edilir | Yüzey yolu ve bel/ense işareti; aşağıdaki türetim düşey yükseklik kanıtı değildir |
| `waist_over_bust_line` | G: denge çizgisindeki boyun tabanından göğüs noktası üzerinden ön bele; jeodezik ölçünün dışbükey zarfını izler; D: parasagittal ağ kesiti, bel düzleminden omuz noktalarına göre ön düzleme kadar | G boyun başlangıcı ile D üst sınırı aynı gösterilmedi; göğüs noktası, yüzey yolu ve mezura eşdeğerliği açık |
| `bust_line` | G: omuz düzeyinden göğüs noktasına yüzey yolu; D: benzer ağ kesiti göğüs düzleminden başlar | Omuz başlangıcı ve D kesitiyle eşdeğerlik |
| `vert_bust_line` | G: ense düzeyi ile göğüs çevresi düzeyi arasındaki düşey fark; D: noktalar arasında düşey eksen farkı | Düzey ve nokta seçimi, duruş |
| `bum_points` | G: kalça çıkıntıları arası; D: işaretlerin Öklid mesafesi | Noktaların işaretlenmesi, D noktalarının G’deki noktalarla eşdeğerliği |
| `neck_w` | G: boyun tabanının arkadan bir denge çizgisinden diğerine yaklaşık genişliği; D: ilgili ağ üzerindeki edge loop Öklid uzunluğu | D’nin loop uzunluğu ile G genişliğinin eşdeğerliği kanıtlanmadı; kesin ölçüm yolu ve uçların işaretlenmesi belirsiz |
| `armscye_depth` | G: arka omuz–koltuk altı mesafesi; D: iki işaret arası Öklid uzaklığı | İşaretlerin seçimi, yüzey/mesafe ve kol pozisyonu |
| `hips` | G: kalça çıkıntıları düzeyinde yatay çevre; D: ağda kesit zarfı ve yerel maksimum; N: en belirgin kalça çevresi | Seviye, duruş, mezura basıncı ve yöntemler arası eşdeğerlik |
| `hip_back_width` | G: kalça çevresinin yan denge çizgileri arasındaki arka kısmı; D: koronal düzlemin arka zarf bölümü | Yan noktalar ve ölçüm yolu; düz genişlik değildir |
| `hips_line` | G: bel–kalça düzeyleri arası düşey fark; D: işaretler arasında düşey eksen uzaklığı | Düzey seçimi ve poz |
| `height` | G: ayaktan baş tepesine boy; D: ağın düşey sınırlayıcı kutusu; N: stadiometre yöntemi | G/D/N tanımlarının uygulanma ve poz koşulları eşlenmeli |
| `head_l` | G: ense–baş tepesi düz mesafesi; D: boyun işareti ile baş tepesinin düşey eksen farkı | İki yolun eşitliği gösterilmedi; işaret ve baş pozisyonu |
| `hip_inclination` | G: yanda bel–kalça açısı, dikey yan 0°; D: medyan düzlem ve iki işaret vektörü arasındaki açı; kod yarısını kullanır | D açı tanımı ve G sıfır/işaret kuralının eşleşmesi, yan ve duruş doğrulanmalı |

## Koddan çıkarılabilen ve çıkarılamayanlar

G `body_params.py` içinde `_waist_level = height - head_l - waist_line`, `_leg_length = _waist_level - hips_line`, `_bust_line = 2/3 vert_bust_line + 1/3 bust_line`, `_shoulder_incl = shoulder_incl` ve `_hip_inclination = hip_inclination/2` kullanır. Bunlar programın hesaplarıdır; örneğin yüzey uzunluğu içeren ilk eşitlik anatomik düşey mesafenin doğruluğunu kanıtlamaz. [Bodice kaynak kodu](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/garment_programs/bodice.py) arka bel bölümü ile toplam belin farkından ön bölüm gibi kalıp hesapları yapar; bunun insan bedeninde ön/arka işaret ve ölçüm yolu eşleşmesini doğruladığı söylenemez.

Gerçek veri yolundan önce kaynak PDF’nin alan alan yeniden kontrolü, yöntem/işaret/sınır noktaları ve pozun uzmanlarla yazılı protokole dönüştürülmesi, farklı bedenlerde operatör içi ve operatörler arası tekrarlanabilirlik ve yöntem karşılaştırması gerekir. Bulguların hepsi hâlâ `unverified`; ölçü tahmini, müşteri verisi, gerçek prova, üretim ve plotter yoktur.

## Lisans ve atıf

Sabit GarmentCode deposu [MIT lisansı](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/LICENSE) taşır; telif ve izin bildirimi yazılımın kopyaları veya önemli bölümleriyle korunmalıdır; yerel kopya [GarmentCode-LICENSE.txt](GarmentCode-LICENSE.txt). Makale ve PDF’lerin lisansları ayrı değerlendirilir; MIT yazılım lisansı bütün dış yayınlara otomatik uygulanmaz. Bu belge kısa özgün özetler ve bağlantılar içerir, dış kaynakların şekil veya metnini yeniden dağıtmaz. GarmentCode: Korosteleva ve Sorkine-Hornung, ACM TOG 42(6), 2023, [DOI 10.1145/3618351](https://doi.org/10.1145/3618351). Hukuki lisans uygunluğu denetimi yapılmadı.
