# Kaynak arka etek pensinde orta nokta farkının izi

Bu inceleme iki sabit sentetik fixture'ın arka etek panelindeki dört pensini inceler. Kaynak checkout commit'i `d449629979028123a5c4dc9e732a2ec19b7fce31`; `generate.py` çalıştırıldığında HEAD ve kullanılan Python dosyalarının yerel SHA-256 manifesti doğrulanır. [İzleme kodu](capture_skirt_dart_rounding.py) kaynak dosyalarını değiştirmez: `sys.settrace` ile gerçek kaynak `cut_into_edge_single` yürütülürken ara köşeleri okur. Dönen üç köşe her pens için kaydedilmiş [golden fixture](golden.json) dikiş kenarlarıyla **birebir binary64 değerleriyle** eşleştirilir. Ortam Python 3.12.14, NumPy 1.26.4, SciPy 1.17.1, Windows x64 idi; iki ayrı çalıştırma aynı ortamda aynı izi üretti. Bu tekrarlar bağımsız kaynak uygulamaları veya imzalı kaynak kanıtı değildir.

## Görülen ilk değişim

| Sentetik fixture | Kaynak arka etek pens kuralı | Üçgen / sıfıra taşıma / döndürme sonrası X orta nokta farkı | `snap_to(ins_point)` sonrası ve kaynak fixture’da fark, cm |
|---|---:|---:|---:|
| base | 0, 3 | tam 0 | her biri `−2⁻⁴⁹` ≈ −1,7763568394002505×10⁻¹⁵ |
| distinct | 0 | tam 0 | `+2⁻⁴⁹` ≈ +1,7763568394002505×10⁻¹⁵ |
| distinct | 3 | tam 0 | `−2⁻⁴⁹` ≈ −1,7763568394002505×10⁻¹⁵ |
| her iki fixture | 1, 2 | tam 0 | tam 0 |

Fark `pens ucu X − (taban uçları X toplamı / 2)` olarak **tam kesir aritmetiğiyle** hesaplanır; pozitif/negatif işaret panelin yerel X eksenindedir. Bu tablo pens bacağı uzunluk farkı değil, uç konumu farkıdır. İzlenen bütün bu tabanlar yataydır. Dört dış pensin kaynak fixture’dan hesaplanan bacak uzunluğu farkları ve diğer arka gövde pensleri [ayrı rapordadır](DART_RESIDUAL_SOURCE_TR.md).

Kaynak [simetrik üçgeni](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/edge_factory.py#L313-L331) oluşturur; [cut_into_edge_single](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/operators.py#L238-L295) üçgeni kopyalar, döndürür ve `snap_to(ins_point)` ile taşır. `snap_to`, [her köşe koordinatına kaydırma bileşenini ayrı ayrı ekler](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/pygarment/garmentcode/edge.py#L820-L837). İncelenen sekiz kesimde de yansıtma adımı çalışmadı; dönen pens uçlarındaki orta nokta farkı taşıma sonrasındaki değerle aynıdır.

Bu fixture ve ortamda çevirinin X işlemleri ayrı ayrı yeniden hesaplandı: üç köşe için `gerçek binary64 sonuç − (eski binary64 değer + binary64 kaydırmanın tam kesir toplamı)` alındı. Dört dış penste taban uçlarının hata terimleri tam sıfır, ortak pens ucununki tablodaki `±2⁻⁴⁹ cm` idi; dört iç penste üç hata terimi de sıfırdı. Tam kesir eşitliği `2 × uç hatası − birinci taban hatası − ikinci taban hatası = orta nokta farkındaki değişimin iki katı` her pens için sınanır. Böylece **bu örneklerde ilk temsil farkı çeviri toplamındaki pens ucu yuvarlamasına bağlanır**. Bu, diğer gövdelerin veya eğimli arka gövde penslerinin de aynı nedenle farklılaştığını kanıtlamaz; optimizerin daha önce seçtiği konum farklı girdilerde değişebilir.

## Tekrar ve sınırlar

[Ham iz](skirt-dart-rounding-trace.json) her aşamada üç köşeyi, tam orta nokta kesrini, taşıma vektörünü ve üç toplama hata kesrini içerir. [Bağımsız doğrulama betiği](verify_skirt_dart_trace.py) yalnız Python standart kütüphanesiyle kaydedilmiş izi ve golden fixture’ı tekrar hesaplar; değişmiş uç ve kaynak commit’i negatif örneklerini reddeder. Doğrulama, aynı verinin bağımsız aritmetik kontrolüdür; ikinci bir bağımsız GarmentCode uygulaması değildir. Çalıştırma: `python experiments/garmentcode-fitted-synthetic/verify_skirt_dart_trace.py`. Kaynak izlemesini yeniden üretmek için `GARMENTCODE_ROOT` sabit kaynak checkout’una işaret etmeli, o kaynağın araştırma bağımlılıkları kurulu olmalı ve `PYTHONDONTWRITEBYTECODE=1` kullanılmalıdır.

Orta noktayı yuvarlanmış koordinatlarda zorla değiştirmenin komşu kontur ve diğer dikişlere etkisi veya tüm bedensel durumlarda uygunluğu gösterilmedi. Bu nedenle geometri veya ayrık uzunluk sonucu düzeltilmedi, tolerans artırılmadı. `unresolved` durumu, gerçek ölçü/kalıp çıktısı/üretim/plotter kapıları değişmedi.
