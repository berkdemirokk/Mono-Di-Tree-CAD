# Temsil edilen omuz kanıtından sonra birleşik 23 kural deneyi

İki sentetik fixture için önceki bel/yan araştırması başlangıçtır. Ön omuz eğrisi, −1 yönünde, iki uç ve bütün komşu konturlar sabit tutularak uygulanır; sol kontur ayna/sıra ile kurulur. Bu yalnız iç araştırma yoludur; dışa aktarılabilen veya üretime alınabilen kalıp grafiği değildir. Kaynak kimliği, 23 ilişki, kontur kapanışı ve çözümlenmemiş eğri çifti kapıları geçilmeden uzunluk karşılaştırması yapılmaz. Şekil sapması yaklaşık base 0,669698 cm ve distinct 0,693844 cm’dir; sınır 1 cm araştırma varsayımıdır, fiziksel eşik değildir.

Tam rasyonel Bernstein kontrolü omuz kuadratiği ile yaka yayının destek çemberinde yalnız ortak uç bulunduğunu gösterir. Eğri çiftleri için tam dyadik denetim omuz–kol evi çiftini de yalnız ortak uç olarak sınıflandırır. Değişen iki ön ve iki arka konturun temsil edilen koordinatlarında kapanış, monotonluk ve bütün çift denetimleri geçer. Etekler değişmemiştir ve önceki altı panel kontrolü geçmiştir. Kaynak optimizeri, yuvarlamalar, çentikler ve kumaş davranışı kapsanmaz.

| Örnek | Omuz ön önce → sonra, cm | Omuz arka, cm | 23 kuralda ayrık projeksiyon aralığı önce → sonra |
|---|---:|---:|---:|
| base | 6,507541 → 6,686874–6,686999 | 6,686915 | 10 → 8 |
| distinct | 6,494328 → 6,686871–6,687005 | 6,686915 | 8 → 6 |

Tablodaki uçlar yaklaşık gösterimdir. Omuz hedefinin asıl sınaması daha dar dyadik uzunluk aralığının 0,0001 cm sayısal bant içinde olmasıdır; bu fiziksel dikiş toleransı değildir. Aşağıdaki grafik aralıkları daha kaba derinlik 6 ile hesaplanır; ayrık olmamaları eşitlik kanıtı değildir. Diğer 21 kuralın tam temsil edilen projeksiyon aralıklarının değişmediği otomatik sınanır. Sonuç `unresolved` / `Unverified`: kalan 8/6 ayrık kural ve tüm fiziksel doğrulama eksikleri nedeniyle gerçek ölçü, üretim, kompozisyon ve plotter kapalıdır.

## 23 kuralın iki örnekte önce ve sonra kaydı

Nokta ondalık ayraçtır; köşeli parantez içindeki virgül alt/üst sınırı ayırır. Görüntülenen uçlar yuvarlatılmıştır; `disjoint` kararı tam tamsayı/kesir aralıklarına dayanır.

```text
synthetic-base shoulder graph BodiceFrontHalf/left_ftorso/0: before A=[13.125,13.125] B=[13.125,13.125], after A=[13.125,13.125] B=[13.125,13.125], disjoint before/after=False/False
synthetic-base shoulder graph BodiceFrontHalf/left_ftorso/1: before A=[12.9,12.9] B=[12.9,12.9], after A=[12.9,12.9] B=[12.9,12.9], disjoint before/after=True/True
synthetic-base shoulder graph BodiceFrontHalf/right_ftorso/0: before A=[13.125,13.125] B=[13.125,13.125], after A=[13.125,13.125] B=[13.125,13.125], disjoint before/after=False/False
synthetic-base shoulder graph BodiceFrontHalf/right_ftorso/1: before A=[12.9,12.9] B=[12.9,12.9], after A=[12.9,12.9] B=[12.9,12.9], disjoint before/after=True/True
synthetic-base shoulder graph FittedSkirtPanel/skirt_back/0: before A=[18.63,18.63] B=[18.63,18.63], after A=[18.63,18.63] B=[18.63,18.63], disjoint before/after=True/True
synthetic-base shoulder graph FittedSkirtPanel/skirt_back/1: before A=[20.7,20.7] B=[20.7,20.7], after A=[20.7,20.7] B=[20.7,20.7], disjoint before/after=False/False
synthetic-base shoulder graph FittedSkirtPanel/skirt_back/2: before A=[20.7,20.7] B=[20.7,20.7], after A=[20.7,20.7] B=[20.7,20.7], disjoint before/after=False/False
synthetic-base shoulder graph FittedSkirtPanel/skirt_back/3: before A=[18.63,18.63] B=[18.63,18.63], after A=[18.63,18.63] B=[18.63,18.63], disjoint before/after=True/True
synthetic-base shoulder graph FittedSkirtPanel/skirt_front/0: before A=[18.4,18.4] B=[18.4,18.4], after A=[18.4,18.4] B=[18.4,18.4], disjoint before/after=False/False
synthetic-base shoulder graph FittedSkirtPanel/skirt_front/1: before A=[18.4,18.4] B=[18.4,18.4], after A=[18.4,18.4] B=[18.4,18.4], disjoint before/after=False/False
synthetic-base shoulder graph BodiceBackHalf/left_btorso/0: before A=[10.7182584,10.7182584] B=[10.7182584,10.7182584], after A=[10.7182584,10.7182584] B=[10.7182584,10.7182584], disjoint before/after=True/True
synthetic-base shoulder graph BodiceBackHalf/left_btorso/1: before A=[11.909176,11.909176] B=[11.909176,11.909176], after A=[11.909176,11.909176] B=[11.909176,11.909176], disjoint before/after=True/True
synthetic-base shoulder graph BodiceBackHalf/right_btorso/0: before A=[10.7182584,10.7182584] B=[10.7182584,10.7182584], after A=[10.7182584,10.7182584] B=[10.7182584,10.7182584], disjoint before/after=True/True
synthetic-base shoulder graph BodiceBackHalf/right_btorso/1: before A=[11.909176,11.909176] B=[11.909176,11.909176], after A=[11.909176,11.909176] B=[11.909176,11.909176], disjoint before/after=True/True
synthetic-base shoulder graph BodiceHalf/left/0: before A=[6.50754067,6.50754067] B=[6.6869154,6.6869154], after A=[6.68687391,6.68699839] B=[6.6869154,6.6869154], disjoint before/after=True/False
synthetic-base shoulder graph BodiceHalf/left/1: before A=[19.2074468,19.2074468] B=[19.2073714,19.2075977], after A=[19.2074468,19.2074468] B=[19.2073714,19.2075977], disjoint before/after=False/False
synthetic-base shoulder graph BodiceHalf/right/0: before A=[6.50754067,6.50754067] B=[6.6869154,6.6869154], after A=[6.68687391,6.68699839] B=[6.6869154,6.6869154], disjoint before/after=True/False
synthetic-base shoulder graph BodiceHalf/right/1: before A=[19.2074468,19.2074468] B=[19.2073714,19.2075977], after A=[19.2074468,19.2074468] B=[19.2073714,19.2075977], disjoint before/after=False/False
synthetic-base shoulder graph FittedShirt/FittedShirt/0: before A=[31.9333333,31.9333333] B=[31.9333333,31.9333333], after A=[31.9333333,31.9333333] B=[31.9333333,31.9333333], disjoint before/after=False/False
synthetic-base shoulder graph FittedShirt/FittedShirt/1: before A=[32.1516853,32.1516853] B=[32.1516853,32.1516853], after A=[32.1516853,32.1516853] B=[32.1516853,32.1516853], disjoint before/after=False/False
synthetic-base shoulder graph PencilSkirt/PencilSkirt/0: before A=[57.5170459,57.5170672] B=[57.5170459,57.5170672], after A=[57.5170459,57.5170672] B=[57.5170459,57.5170672], disjoint before/after=False/False
synthetic-base shoulder graph PencilSkirt/PencilSkirt/1: before A=[57.5170459,57.5170672] B=[57.5170459,57.5170672], after A=[57.5170459,57.5170672] B=[57.5170459,57.5170672], disjoint before/after=False/False
synthetic-base shoulder graph MetaGarment/09CFA3D4CAD107CE27ABC512F8AC452BF1B65A3FB4CBEC79B34CD5B1FF0667A9/0: before A=[79.9999989,80.0000023] B=[80,80], after A=[79.9999989,80.0000023] B=[80,80], disjoint before/after=False/False
synthetic-distinct shoulder graph BodiceFrontHalf/left_ftorso/0: before A=[13.5,13.5] B=[13.5,13.5], after A=[13.5,13.5] B=[13.5,13.5], disjoint before/after=False/False
synthetic-distinct shoulder graph BodiceFrontHalf/left_ftorso/1: before A=[14.7,14.7] B=[14.7,14.7], after A=[14.7,14.7] B=[14.7,14.7], disjoint before/after=False/False
synthetic-distinct shoulder graph BodiceFrontHalf/right_ftorso/0: before A=[13.5,13.5] B=[13.5,13.5], after A=[13.5,13.5] B=[13.5,13.5], disjoint before/after=False/False
synthetic-distinct shoulder graph BodiceFrontHalf/right_ftorso/1: before A=[14.7,14.7] B=[14.7,14.7], after A=[14.7,14.7] B=[14.7,14.7], disjoint before/after=False/False
synthetic-distinct shoulder graph FittedSkirtPanel/skirt_back/0: before A=[18.63,18.63] B=[18.63,18.63], after A=[18.63,18.63] B=[18.63,18.63], disjoint before/after=True/True
synthetic-distinct shoulder graph FittedSkirtPanel/skirt_back/1: before A=[20.7,20.7] B=[20.7,20.7], after A=[20.7,20.7] B=[20.7,20.7], disjoint before/after=False/False
synthetic-distinct shoulder graph FittedSkirtPanel/skirt_back/2: before A=[20.7,20.7] B=[20.7,20.7], after A=[20.7,20.7] B=[20.7,20.7], disjoint before/after=False/False
synthetic-distinct shoulder graph FittedSkirtPanel/skirt_back/3: before A=[18.63,18.63] B=[18.63,18.63], after A=[18.63,18.63] B=[18.63,18.63], disjoint before/after=True/True
synthetic-distinct shoulder graph FittedSkirtPanel/skirt_front/0: before A=[18.4,18.4] B=[18.4,18.4], after A=[18.4,18.4] B=[18.4,18.4], disjoint before/after=False/False
synthetic-distinct shoulder graph FittedSkirtPanel/skirt_front/1: before A=[18.4,18.4] B=[18.4,18.4], after A=[18.4,18.4] B=[18.4,18.4], disjoint before/after=False/False
synthetic-distinct shoulder graph BodiceBackHalf/left_btorso/0: before A=[13.4273174,13.4273174] B=[13.4273174,13.4273174], after A=[13.4273174,13.4273174] B=[13.4273174,13.4273174], disjoint before/after=True/True
synthetic-distinct shoulder graph BodiceBackHalf/left_btorso/1: before A=[14.9192415,14.9192415] B=[14.9192415,14.9192415], after A=[14.9192415,14.9192415] B=[14.9192415,14.9192415], disjoint before/after=True/True
synthetic-distinct shoulder graph BodiceBackHalf/right_btorso/0: before A=[13.4273174,13.4273174] B=[13.4273174,13.4273174], after A=[13.4273174,13.4273174] B=[13.4273174,13.4273174], disjoint before/after=True/True
synthetic-distinct shoulder graph BodiceBackHalf/right_btorso/1: before A=[14.9192415,14.9192415] B=[14.9192415,14.9192415], after A=[14.9192415,14.9192415] B=[14.9192415,14.9192415], disjoint before/after=True/True
synthetic-distinct shoulder graph BodiceHalf/left/0: before A=[6.49432788,6.49432788] B=[6.6869154,6.6869154], after A=[6.68687103,6.68700416] B=[6.6869154,6.6869154], disjoint before/after=True/False
synthetic-distinct shoulder graph BodiceHalf/left/1: before A=[21.3281666,21.3281666] B=[21.3280652,21.3283695], after A=[21.3281666,21.3281666] B=[21.3280652,21.3283695], disjoint before/after=False/False
synthetic-distinct shoulder graph BodiceHalf/right/0: before A=[6.49432788,6.49432788] B=[6.6869154,6.6869154], after A=[6.68687103,6.68700416] B=[6.6869154,6.6869154], disjoint before/after=True/False
synthetic-distinct shoulder graph BodiceHalf/right/1: before A=[21.3281666,21.3281666] B=[21.3280652,21.3283695], after A=[21.3281666,21.3281666] B=[21.3280652,21.3283695], disjoint before/after=False/False
synthetic-distinct shoulder graph FittedShirt/FittedShirt/0: before A=[32.9333333,32.9333333] B=[32.9333333,32.9333333], after A=[32.9333333,32.9333333] B=[32.9333333,32.9333333], disjoint before/after=False/False
synthetic-distinct shoulder graph FittedShirt/FittedShirt/1: before A=[35.4142672,35.4142672] B=[35.4142672,35.4142672], after A=[35.4142672,35.4142672] B=[35.4142672,35.4142672], disjoint before/after=False/False
synthetic-distinct shoulder graph PencilSkirt/PencilSkirt/0: before A=[56.7170459,56.7170672] B=[56.7170459,56.7170672], after A=[56.7170459,56.7170672] B=[56.7170459,56.7170672], disjoint before/after=False/False
synthetic-distinct shoulder graph PencilSkirt/PencilSkirt/1: before A=[56.7170459,56.7170672] B=[56.7170459,56.7170672], after A=[56.7170459,56.7170672] B=[56.7170459,56.7170672], disjoint before/after=False/False
synthetic-distinct shoulder graph MetaGarment/80AFDE88D2387AA69B4D10EFBCCE03291794022D0974CA1DC31A7CF35A89BA72/0: before A=[73.9999989,74.0000023] B=[74,74], after A=[73.9999989,74.0000023] B=[74,74], disjoint before/after=False/False
```
