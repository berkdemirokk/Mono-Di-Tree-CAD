# Çift taraflı değişimin 23 dikiş kuralı: sayısal araştırma

[Birleşik bel ve yan deneyi](COMBINED_WAIST_CURVATURE_TR.md) sağ arka adayı sol arka kontura ters sıra ve ayna dönüşümüyle aktarır. Önce orijinal sol–sağ temsil edilen kenarların tamamında bu eşleşme birebir sınanır; değişen sol kenarlar 4, 5 ve 6 ile sınırlıdır. Sağ ve sol ön gövdeler ve iki etek konturu değiştirilmez. Altı başlangıç konturu ve değişen iki arka kontur için kapanış, kenar kimliği, çözümlenmemiş eğri çiftleri ve monotonluk denetlenir. Altı konturun temsil edilen koordinatlarındaki önce/sonra kontur topolojisinde çözümlenmemiş çift ve monotonluk sorunu sıfırdır; bu fiziksel topoloji kanıtı değildir.

İki sabit Python fixture’ındaki 23 kaynak dikiş kaydı, üretilen grafikle sahip, kural indeksi, taraf içindeki kenar sırası, panel/kenar, yön bayrakları ve büzgü bölümleri bakımından karşılaştırılır; kaynak kayıtlarının liste sırası bu karşılaştırmada sınanmaz. Sonra aynı ilişkiler değişen C# konturlarına yeniden bağlanır ve 23 kuralın iki tarafının büzgü katsayılarıyla bölünmüş uzunluk aralıkları yeniden hesaplanır. Yanlış geçerli kenar, panel, ters yön ve açık sol kontur negatif denetimde reddedilir; grafiğin diğer 20 kuralındaki uzunluk aralıklarının aynı kalması gerekir. Fixture bağlantıları, güncellenmiş geometrinin fiziksel dikiş semantiğinin kaynakta yeniden doğrulandığını göstermez.

Değiştirilmiş panel hesabı, serbest kontur listeleri yerine yalnız sabit sentetik kimlikten adayı yeniden oluşturan ve iki konturu değiştirilemez kopyalarda saklayan iç nesneyi kabul eder. Beden snapshot'ı grafik ve hesap gövdesiyle karşılaştırılır. Başka beden snapshot'ı ve kapalı fakat ayna olmayan bir kontrol noktası negatif testte reddedilir. Bu köken denetimi gerçek kaynak imzası veya fiziksel onay değildir.

| Beden | Ön üst / ön etek bel önce = sonra, cm | Arka üst bel önce → sonra, cm | Arka etek bel önce = sonra, cm | 23 kuralda ayrık aralık sayısı önce → sonra |
|---|---:|---:|---:|---:|
| base | 44 / 44 | 36,357008–36,357011 → 35,999999–36,000003 | 36 | 13 → 10 |
| distinct | 40 / 40 | 34,315481–34,315486 → 33,999998–34,000003 | 34 | 11 → 8 |

Aralıklar gösterim için yaklaşık yuvarlanır; yanlarda ve toplam belde ayrıklığın kaybolması eşitlik ispatı değildir. Özellikle kalan 10 ve 8 ayrık kural, birleştirilmiş grafiğin **geçerli bir üretim adayı olarak kabul edilemeyeceğini** gösterir. Bunların kimlikleri ve tüm 23 kuralın iki tarafı aşağıdaki ham doğrulama kaydındadır. Büzgü projeksiyonu sayısal kaynak modeli olup kumaş mekaniği değildir. Sol ve sağ yan için daha dar sayısal kontrol ve şekil sınırları önceki deneyde açıklanmıştır; yukarıdaki 23 kuralda kullanılan derinlik 6 aralıkları daha geniştir.

Gerçek çentik verisi, fiziksel prova, dikiş payı, kaynak optimizer ve tüm diğer bedenler doğrulanmadı. Sonuç yalnız tanısaldır (`unresolved`); değişen grafiğin kompozisyonu, gerçek veri, üretim, dışa aktarma ve plotter kapalıdır. Kaynak fixture yalnız iki sentetik örnektir.

## Bütün kuralların önce/sonra kaydı

Her satırda A ve B kaynak dikiş kuralının taraflarıdır; sayılar cm cinsinden temsil edilen kontrol noktalarının projeksiyon uzunluğu aralıklarıdır. Aşağıdaki kayıt ve doğrulama çıktısındaki dört `waist part` satırı kültürden bağımsızdır: nokta ondalık ayraç, virgül alt ve üst sınır ayıracıdır. Görüntülenen yaklaşık yuvarlatılmış uçlar aralık kanıtı değildir. `disjoint` bayrağı tam tamsayı/kesir aralıklarından hesaplanır: `True` aralıkların ayrıldığını, `False` ise yalnız ayrılmadıklarını belirtir. Kimlikteki uzun değer beden snapshot özetidir.

```text
synthetic-base graph BodiceFrontHalf/left_ftorso/0: before A=[13.125,13.125] B=[13.125,13.125], after A=[13.125,13.125] B=[13.125,13.125], disjoint before/after=False/False; research only.
synthetic-base graph BodiceFrontHalf/left_ftorso/1: before A=[12.9,12.9] B=[12.9,12.9], after A=[12.9,12.9] B=[12.9,12.9], disjoint before/after=True/True; research only.
synthetic-base graph BodiceFrontHalf/right_ftorso/0: before A=[13.125,13.125] B=[13.125,13.125], after A=[13.125,13.125] B=[13.125,13.125], disjoint before/after=False/False; research only.
synthetic-base graph BodiceFrontHalf/right_ftorso/1: before A=[12.9,12.9] B=[12.9,12.9], after A=[12.9,12.9] B=[12.9,12.9], disjoint before/after=True/True; research only.
synthetic-base graph FittedSkirtPanel/skirt_back/0: before A=[18.63,18.63] B=[18.63,18.63], after A=[18.63,18.63] B=[18.63,18.63], disjoint before/after=True/True; research only.
synthetic-base graph FittedSkirtPanel/skirt_back/1: before A=[20.7,20.7] B=[20.7,20.7], after A=[20.7,20.7] B=[20.7,20.7], disjoint before/after=False/False; research only.
synthetic-base graph FittedSkirtPanel/skirt_back/2: before A=[20.7,20.7] B=[20.7,20.7], after A=[20.7,20.7] B=[20.7,20.7], disjoint before/after=False/False; research only.
synthetic-base graph FittedSkirtPanel/skirt_back/3: before A=[18.63,18.63] B=[18.63,18.63], after A=[18.63,18.63] B=[18.63,18.63], disjoint before/after=True/True; research only.
synthetic-base graph FittedSkirtPanel/skirt_front/0: before A=[18.4,18.4] B=[18.4,18.4], after A=[18.4,18.4] B=[18.4,18.4], disjoint before/after=False/False; research only.
synthetic-base graph FittedSkirtPanel/skirt_front/1: before A=[18.4,18.4] B=[18.4,18.4], after A=[18.4,18.4] B=[18.4,18.4], disjoint before/after=False/False; research only.
synthetic-base graph BodiceBackHalf/left_btorso/0: before A=[10.7182584,10.7182584] B=[10.7182584,10.7182584], after A=[10.7182584,10.7182584] B=[10.7182584,10.7182584], disjoint before/after=True/True; research only.
synthetic-base graph BodiceBackHalf/left_btorso/1: before A=[11.909176,11.909176] B=[11.909176,11.909176], after A=[11.909176,11.909176] B=[11.909176,11.909176], disjoint before/after=True/True; research only.
synthetic-base graph BodiceBackHalf/right_btorso/0: before A=[10.7182584,10.7182584] B=[10.7182584,10.7182584], after A=[10.7182584,10.7182584] B=[10.7182584,10.7182584], disjoint before/after=True/True; research only.
synthetic-base graph BodiceBackHalf/right_btorso/1: before A=[11.909176,11.909176] B=[11.909176,11.909176], after A=[11.909176,11.909176] B=[11.909176,11.909176], disjoint before/after=True/True; research only.
synthetic-base graph BodiceHalf/left/0: before A=[6.50754067,6.50754067] B=[6.6869154,6.6869154], after A=[6.50754067,6.50754067] B=[6.6869154,6.6869154], disjoint before/after=True/True; research only.
synthetic-base graph BodiceHalf/left/1: before A=[19.2074468,19.2074468] B=[18.8388676,18.8388676], after A=[19.2074468,19.2074468] B=[19.2073714,19.2075977], disjoint before/after=True/False; research only.
synthetic-base graph BodiceHalf/right/0: before A=[6.50754067,6.50754067] B=[6.6869154,6.6869154], after A=[6.50754067,6.50754067] B=[6.6869154,6.6869154], disjoint before/after=True/True; research only.
synthetic-base graph BodiceHalf/right/1: before A=[19.2074468,19.2074468] B=[18.8388676,18.8388676], after A=[19.2074468,19.2074468] B=[19.2073714,19.2075977], disjoint before/after=True/False; research only.
synthetic-base graph FittedShirt/FittedShirt/0: before A=[31.9333333,31.9333333] B=[31.9333333,31.9333333], after A=[31.9333333,31.9333333] B=[31.9333333,31.9333333], disjoint before/after=False/False; research only.
synthetic-base graph FittedShirt/FittedShirt/1: before A=[32.1516853,32.1516853] B=[32.1516853,32.1516853], after A=[32.1516853,32.1516853] B=[32.1516853,32.1516853], disjoint before/after=False/False; research only.
synthetic-base graph PencilSkirt/PencilSkirt/0: before A=[57.5170459,57.5170672] B=[57.5170459,57.5170672], after A=[57.5170459,57.5170672] B=[57.5170459,57.5170672], disjoint before/after=False/False; research only.
synthetic-base graph PencilSkirt/PencilSkirt/1: before A=[57.5170459,57.5170672] B=[57.5170459,57.5170672], after A=[57.5170459,57.5170672] B=[57.5170459,57.5170672], disjoint before/after=False/False; research only.
synthetic-base graph MetaGarment/09CFA3D4CAD107CE27ABC512F8AC452BF1B65A3FB4CBEC79B34CD5B1FF0667A9/0: before A=[80.357008,80.3570115] B=[80,80], after A=[79.9999989,80.0000023] B=[80,80], disjoint before/after=True/False; research only.
synthetic-distinct graph BodiceFrontHalf/left_ftorso/0: before A=[13.5,13.5] B=[13.5,13.5], after A=[13.5,13.5] B=[13.5,13.5], disjoint before/after=False/False; research only.
synthetic-distinct graph BodiceFrontHalf/left_ftorso/1: before A=[14.7,14.7] B=[14.7,14.7], after A=[14.7,14.7] B=[14.7,14.7], disjoint before/after=False/False; research only.
synthetic-distinct graph BodiceFrontHalf/right_ftorso/0: before A=[13.5,13.5] B=[13.5,13.5], after A=[13.5,13.5] B=[13.5,13.5], disjoint before/after=False/False; research only.
synthetic-distinct graph BodiceFrontHalf/right_ftorso/1: before A=[14.7,14.7] B=[14.7,14.7], after A=[14.7,14.7] B=[14.7,14.7], disjoint before/after=False/False; research only.
synthetic-distinct graph FittedSkirtPanel/skirt_back/0: before A=[18.63,18.63] B=[18.63,18.63], after A=[18.63,18.63] B=[18.63,18.63], disjoint before/after=True/True; research only.
synthetic-distinct graph FittedSkirtPanel/skirt_back/1: before A=[20.7,20.7] B=[20.7,20.7], after A=[20.7,20.7] B=[20.7,20.7], disjoint before/after=False/False; research only.
synthetic-distinct graph FittedSkirtPanel/skirt_back/2: before A=[20.7,20.7] B=[20.7,20.7], after A=[20.7,20.7] B=[20.7,20.7], disjoint before/after=False/False; research only.
synthetic-distinct graph FittedSkirtPanel/skirt_back/3: before A=[18.63,18.63] B=[18.63,18.63], after A=[18.63,18.63] B=[18.63,18.63], disjoint before/after=True/True; research only.
synthetic-distinct graph FittedSkirtPanel/skirt_front/0: before A=[18.4,18.4] B=[18.4,18.4], after A=[18.4,18.4] B=[18.4,18.4], disjoint before/after=False/False; research only.
synthetic-distinct graph FittedSkirtPanel/skirt_front/1: before A=[18.4,18.4] B=[18.4,18.4], after A=[18.4,18.4] B=[18.4,18.4], disjoint before/after=False/False; research only.
synthetic-distinct graph BodiceBackHalf/left_btorso/0: before A=[13.4273174,13.4273174] B=[13.4273174,13.4273174], after A=[13.4273174,13.4273174] B=[13.4273174,13.4273174], disjoint before/after=True/True; research only.
synthetic-distinct graph BodiceBackHalf/left_btorso/1: before A=[14.9192415,14.9192415] B=[14.9192415,14.9192415], after A=[14.9192415,14.9192415] B=[14.9192415,14.9192415], disjoint before/after=True/True; research only.
synthetic-distinct graph BodiceBackHalf/right_btorso/0: before A=[13.4273174,13.4273174] B=[13.4273174,13.4273174], after A=[13.4273174,13.4273174] B=[13.4273174,13.4273174], disjoint before/after=True/True; research only.
synthetic-distinct graph BodiceBackHalf/right_btorso/1: before A=[14.9192415,14.9192415] B=[14.9192415,14.9192415], after A=[14.9192415,14.9192415] B=[14.9192415,14.9192415], disjoint before/after=True/True; research only.
synthetic-distinct graph BodiceHalf/left/0: before A=[6.49432788,6.49432788] B=[6.6869154,6.6869154], after A=[6.49432788,6.49432788] B=[6.6869154,6.6869154], disjoint before/after=True/True; research only.
synthetic-distinct graph BodiceHalf/left/1: before A=[21.3281666,21.3281666] B=[20.8212365,20.8212365], after A=[21.3281666,21.3281666] B=[21.3280652,21.3283695], disjoint before/after=True/False; research only.
synthetic-distinct graph BodiceHalf/right/0: before A=[6.49432788,6.49432788] B=[6.6869154,6.6869154], after A=[6.49432788,6.49432788] B=[6.6869154,6.6869154], disjoint before/after=True/True; research only.
synthetic-distinct graph BodiceHalf/right/1: before A=[21.3281666,21.3281666] B=[20.8212365,20.8212365], after A=[21.3281666,21.3281666] B=[21.3280652,21.3283695], disjoint before/after=True/False; research only.
synthetic-distinct graph FittedShirt/FittedShirt/0: before A=[32.9333333,32.9333333] B=[32.9333333,32.9333333], after A=[32.9333333,32.9333333] B=[32.9333333,32.9333333], disjoint before/after=False/False; research only.
synthetic-distinct graph FittedShirt/FittedShirt/1: before A=[35.4142672,35.4142672] B=[35.4142672,35.4142672], after A=[35.4142672,35.4142672] B=[35.4142672,35.4142672], disjoint before/after=False/False; research only.
synthetic-distinct graph PencilSkirt/PencilSkirt/0: before A=[56.7170459,56.7170672] B=[56.7170459,56.7170672], after A=[56.7170459,56.7170672] B=[56.7170459,56.7170672], disjoint before/after=False/False; research only.
synthetic-distinct graph PencilSkirt/PencilSkirt/1: before A=[56.7170459,56.7170672] B=[56.7170459,56.7170672], after A=[56.7170459,56.7170672] B=[56.7170459,56.7170672], disjoint before/after=False/False; research only.
synthetic-distinct graph MetaGarment/80AFDE88D2387AA69B4D10EFBCCE03291794022D0974CA1DC31A7CF35A89BA72/0: before A=[74.3154818,74.3154853] B=[74,74], after A=[73.9999989,74.0000023] B=[74,74], disjoint before/after=True/False; research only.
```
