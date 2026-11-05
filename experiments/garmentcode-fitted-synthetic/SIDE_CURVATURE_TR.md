# Sentetik yan eğrilik deneyi

Yalnız dört sentetik beden ve sabit araştırma tasarımı kullanılır. Ön yan (kenar 7 ve 4) aynen tutulur; arka yanın 7 numaralı kenarı ve bütün diğer kontur, bel uçları ve pensler değiştirilmez. Arka 8 numaralı doğrusal kenar, uçları sabit bir kuadratik eğriyle değiştirilir. Dolayısıyla ön tarafın sapması sıfır, arkanın aynı parametrede eski doğruya azami sapması eğrinin ortasındaki normal uzaklıktır. Bu tek parametreli aile bütün olası ön/arka eğrileri temsil etmez.

En fazla 3 cm sapma araştırma varsayımıdır, kabul edilebilir fiziksel şekil sınırı değildir. Aranan eşitlik için 0,0001 cm yalnız sayısal tanı eşiğidir; dikiş toleransı değildir. Simpson integrali aday seçer, temsil edilen eğri uzunluğu bağımsız dyadik alt/üst sınırlarla denetlenir. Konturların temsil edilen koordinatlarında kapanış ve eğri çiftleri incelenir; kaynak optimizeri ve daha önceki yuvarlamalar kapsanmaz.

Bel kenarları ve uçları korunur, ancak bel uzunluğu hedefi bu deneyde denetlenmez. Dolayısıyla bu, bel ve yan dikiş hedeflerini birlikte çözen iki hedefli bir çözüm değildir. Sapma sınırı karşılaştırmasındaki `1e-12 cm` ek pay yalnız binary64 hesaplamasındaki sayısal karşılaştırma marjıdır; fiziksel tolerans veya kesin bir azami sapma sertifikası değildir.

| Sentetik örnek | Ön yan cm | Arka önce cm | Arka sonra cm yaklaşık | Azami sapma cm yaklaşık | Topoloji çözülmemiş ön/arka |
|---|---:|---:|---:|---:|---:|
| base | 19,207447 | 18,838868 | 19,207447 | 0,8140 | 0/0 |
| distinct | 21,328167 | 20,821237 | 21,328167 | 0,9663 | 0/0 |
| third | 20,183744 | 19,758216 | 20,183744 | 0,9019 | 0/0 |
| fifth | 20,190709 | 19,693652 | 20,190709 | 0,8376 | 0/0 |

Fixture gerçek çentik verisi sunmaz. Aynı parametreli varsayımsal orta nokta çentiği önde 0, arkada tabloda verilen yaklaşık azami sapma kadar yer değiştirir; bu çentik eşleşmesi veya dikilebilirlik kanıtı değildir. 0,5 cm ve 0 cm sınırlarında base örneği yaklaşık Simpson hesabında aday bulunamadığı için reddedilir; bu ret kesin çözümsüzlük ispatı değildir. Geçersiz sınır ve bilinmeyen beden de reddedilir. Otomatik doğrulama tablodaki dört örneğin tamamının kabul edilmesini gerektirir.

Sonuçlar `unresolved` ve yalnız araştırma amaçlıdır. Gerçek veri, fiziksel prova, üretim, dışa aktarma ve plotter kapalıdır; burada bir ürün kalıbı veya değiştirilmiş dikiş grafiği üretilmez.
