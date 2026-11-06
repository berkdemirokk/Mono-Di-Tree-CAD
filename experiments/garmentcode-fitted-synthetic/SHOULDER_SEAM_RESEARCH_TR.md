# Ayrık dikişlerin önceliği ve omuz deneyi

Bu yalnız iki sabit sentetik C# örneğinin araştırmasıdır. Önceki [23 kuralın tamamının önce/sonra kaydı](COMBINED_GRAPH_TR.md) bu çalışmanın başlangıcıdır; 10/8 ayrık ilişki oradaki bel/yan denemesi sonrasına aittir. Aşağıdaki önem sırası bir fiziksel kusur sınıflaması değil, temsil edilen sayısal aralık farkı ve dikiş işlevine dayalı araştırma önceliğidir. Aynı görünen yuvarlak uzunluklar eşitlik kanıtı değildir.

| Öncelik | Kaynak ilişki ve kenarlar (A ↔ B) | Base / distinct ayrık aralıklar arası yaklaşık boşluk, cm | Yorum |
|---|---|---:|---|
| 1 | `BodiceHalf left/0`: `left_ftorso:9` ↔ `left_btorso:2`; `right/0`: `right_ftorso:2` ↔ `right_btorso:10` | 0,179374736 / 0,192587525 (iki tarafta aynı) | Büzgü katsayıları 1; sentetik omuz farkı, diğer ayrıklardan çok büyük. Öncelikli deney aşağıda. |
| 2 | `BodiceBackHalf left/0`: `left_btorso:8` ↔ `:7`; `left/1`: `:11` ↔ `:10`; `right/0`: `right_btorso:4` ↔ `:5`; `right/1`: `:1` ↔ `:2` | yaklaşık 4,32e−17–1,68e−16 / 3,13e−17–2,34e−16 | Pens bacaklarında çok küçük temsil farkı; tam aralıklar yine ayrık. Sayısal farkı fiziksel kusur ilan etmek doğru değildir. |
| 3 | `FittedSkirtPanel skirt_back/0`: `:3` ↔ `:4`; `/3`: `:13` ↔ `:14` | yaklaşık 1,76e−16 / 1,88e−16 | Arka etek pens bacakları, katsayı 1; sayı duyarlılığı incelenmeli. |
| 4 | `BodiceFrontHalf left_ftorso/1`: `:1` ↔ `:2`; `right_ftorso/1`: `:10` ↔ `:9` | yaklaşık 1,84e−16 / ayrık değil | Yalnız base örneğinde ayrık ön pens bacakları. |

Boşluk gösterimi tam aralık uçlarının tamsayı/kesir farkından yaklaşık binary64 değere dönüştürülür; `disjoint` kararı tam tamsayı karşılaştırmasındandır. Bu küçük farkların salt yuvarlamadan kaynaklandığı ispatlanmış değildir. Önceki 23 kuralın diğerleri de projeksiyon aralıklarıyla hesaplanmıştır; örtüşme gerçek eşitlik kanıtı değildir.

## Kaynağın söylediği ve söylemediği

Sabit `d449629979028123a5c4dc9e732a2ec19b7fce31` [Interface kurucusu](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/interface.py#L14-L26), varsayılan `ruffle=1` değerini büzgüsüz bağlantı olarak tanımlar. [projecting_lengths](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/interface.py#L103-L113), 1'e 0,001 içinde yakın katsayıyı 1 kabul eder; diğerlerinde her kenar uzunluğunu katsayıya böler. İncelenen omuz ve pens kaynak fixture kayıtlarının tüm bölümlerinde katsayı 1’dir. Dolayısıyla bu ilişkilerde **büzgüyle açıklanan planlı bir uzunluk farkı tanımlanmamıştır**; eşit uzunluk bu araştırmada büzgüsüz sayısal hedef olarak kullanılır.

Ancak kaynak [StitchingRule](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/connector.py#L26-L40) tam eşitliği zorunlu kılmaz: projeksiyon uzunluk farkı için yalnız `verbose` ise 0,3 cm toleranslı uyarı üretir. Bu uyarı eşiği fiziksel dikiş toleransı olarak alınamaz. [BodiceHalf](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/garment_programs/bodice.py#L207-L218) omuz arayüzlerini birbirine bağlar. Buna karşılık [FittedSkirtPanel](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/garment_programs/skirt_paneled.py#L234-L239) arka etek kalça uzantısında ayrı katsayı kullanır; [sabit tasarım](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/assets/garment_programs/skirt_paneled.py#L350-L360) bu değeri 1,05 verir. Bu bölümlerde ham uzunluk eşitliği aramak yanlış olur; projeksiyon kontrolü ilgili bölümü katsayıya böler. İki etek yan ilişkisinin projeksiyon aralıkları başlangıçta ayrık değildir; bu tek başına dikim kanıtı değildir.

## Ön omuz denemesi: aday reddedildi

Mevcut bel/yan düzeltmesinden bağımsız olarak sağ ön `:2` omuz çizgisinin uçları sabit tutuldu, tek normal yönde kuadratik eğriyle uzatıldı ve sol ön `:9` aynalandı. Diğer ön kontur ve komşu yaka/kol evi kenarları değişmedi. İki ters eğrilik yönü, 1 cm azami normal sapma varsayımı ve 0,0001 cm sayısal uzunluk bandı denendi; bu sayılar fiziksel kabul sınırı değildir. Simpson araması aday seçer, uzunluklar temsil edilen noktalarda dyadik aralıklarla doğrulanır. Şekil sapması binary64 yaklaşık hesap olup kesin dışa yuvarlanmış sınır değildir.

| Örnek | Ön omuz önce → denenmiş, cm | Arka omuz, cm | Yaklaşık azami sapma | Her iki yönde durum |
|---|---:|---:|---:|---|
| base | 6,507540668 → 6,686915243–6,686915729 | 6,686915405 | 0,669698 cm | Topoloji çözülmedi; reddedildi |
| distinct | 6,494327880 → 6,686915231–6,686915751 | 6,686915405 | 0,693844 cm | Topoloji çözülmedi; reddedildi |

Sağdaki `−1` yönünde `(1,2)` komşu çifti; `+1` yönünde `(1,2)` ve `(2,3)` çözümlenemedi. Solda karşılıkları `(9,10)` ve ayrıca `(8,9)`’dur. Otomatik testler bu çiftleri, panel kimliklerini ve yönleri her iki fixture için ayrı ayrı doğrular. Bu sınıflandırma gerçek kesişim bulunduğunu değil, mevcut ayırma yönteminin bu çiftlerde kanıt üretemediğini gösterir. API bir hata yerine yalnız tanısal uzunluk ve topoloji sonucu döndürür; sonucu her zaman `Accepted=false`, `unresolved` ve üretim/dışa aktarma kapalıdır. Sonuçta kontur veya dikiş grafiği bulunmaz ve kompozisyon çağrısı hata verir. Dolayısıyla geçerli kabul edilmiş yeni bir “sonrası 23 kural” yoktur. Önceki son tanısal 23 kural kaydı [buradadır](COMBINED_GRAPH_TR.md) ve değişmemiştir; aday uygulanmış gibi sonuç sunulamaz. Bu ret, başka eğri/yöntem ailelerinin çözümsüzlüğünü ispatlamaz.

Her iki beden ve iki yöndeki retler, sıfır sapma ve geçersiz yön, dyadik boşluk hesabı, üretim/kompozisyon kapısı ve mevcut 23 kural araştırma doğrulamasında sınanır. Gerçek ölçü, gerçek çentik, fiziksel prova, üretim, dışa aktarma ve plotter kapalıdır.

## Ayrık ilişkilerin sayısal tanı kaydı

A/B orta noktaları ve boşluklar gösterim amaçlı yaklaşık sayılardır; yukarıdaki tam ayrıklık bayrağı ile karıştırılmamalıdır.

```text
synthetic-base residual BodiceFrontHalf/left_ftorso/1 midpoint A=12.899999999999999 B=12.899999999999999 approximate gap=1.83602774E-16 cm; exact disjoint=True
synthetic-base residual BodiceFrontHalf/right_ftorso/1 midpoint A=12.899999999999999 B=12.899999999999999 approximate gap=1.83602774E-16 cm; exact disjoint=True
synthetic-base residual FittedSkirtPanel/skirt_back/0 midpoint A=18.630000000000003 B=18.630000000000003 approximate gap=1.76089805E-16 cm; exact disjoint=True
synthetic-base residual FittedSkirtPanel/skirt_back/3 midpoint A=18.630000000000003 B=18.630000000000003 approximate gap=1.76089805E-16 cm; exact disjoint=True
synthetic-base residual BodiceBackHalf/left_btorso/0 midpoint A=10.718258380490152 B=10.718258380490152 approximate gap=1.68381926E-16 cm; exact disjoint=True
synthetic-base residual BodiceBackHalf/left_btorso/1 midpoint A=11.909175978322388 B=11.909175978322388 approximate gap=4.32310378E-17 cm; exact disjoint=True
synthetic-base residual BodiceBackHalf/right_btorso/0 midpoint A=10.718258380490152 B=10.718258380490152 approximate gap=1.68381926E-16 cm; exact disjoint=True
synthetic-base residual BodiceBackHalf/right_btorso/1 midpoint A=11.909175978322388 B=11.909175978322388 approximate gap=4.32310378E-17 cm; exact disjoint=True
synthetic-base residual BodiceHalf/left/0 midpoint A=6.5075406684175459 B=6.6869154046010166 approximate gap=0.179374736 cm; exact disjoint=True
synthetic-base residual BodiceHalf/right/0 midpoint A=6.5075406684175459 B=6.6869154046010166 approximate gap=0.179374736 cm; exact disjoint=True
synthetic-distinct residual FittedSkirtPanel/skirt_back/0 midpoint A=18.629999999999995 B=18.629999999999995 approximate gap=1.88008463E-16 cm; exact disjoint=True
synthetic-distinct residual FittedSkirtPanel/skirt_back/3 midpoint A=18.629999999999995 B=18.629999999999995 approximate gap=1.88008463E-16 cm; exact disjoint=True
synthetic-distinct residual BodiceBackHalf/left_btorso/0 midpoint A=13.427317388619255 B=13.427317388619255 approximate gap=2.33688607E-16 cm; exact disjoint=True
synthetic-distinct residual BodiceBackHalf/left_btorso/1 midpoint A=14.919241542910282 B=14.919241542910282 approximate gap=3.12836908E-17 cm; exact disjoint=True
synthetic-distinct residual BodiceBackHalf/right_btorso/0 midpoint A=13.427317388619255 B=13.427317388619255 approximate gap=2.33688607E-16 cm; exact disjoint=True
synthetic-distinct residual BodiceBackHalf/right_btorso/1 midpoint A=14.919241542910282 B=14.919241542910282 approximate gap=3.12836908E-17 cm; exact disjoint=True
synthetic-distinct residual BodiceHalf/left/0 midpoint A=6.4943278800202311 B=6.6869154046010166 approximate gap=0.192587525 cm; exact disjoint=True
synthetic-distinct residual BodiceHalf/right/0 midpoint A=6.4943278800202311 B=6.6869154046010166 approximate gap=0.192587525 cm; exact disjoint=True
```
