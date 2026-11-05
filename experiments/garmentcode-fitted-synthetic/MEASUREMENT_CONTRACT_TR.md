# Kaynak ölçü alanları: araştırma eşlemesi

Bu liste ölçü alma kılavuzu değildir. [GarmentCode ölçü belgesi](https://github.com/maria-korosteleva/GarmentCode/blob/d449629979028123a5c4dc9e732a2ec19b7fce31/docs/Body%20Measurements%20GarmentCode.pdf) ve kaynak şemasındaki adları eşler; uzunluklar cm, açılar derecedir. Kaynak açıklaması, gerçek kişilerde tekrarlanabilir işaretleme ve ölçüm protokolü olarak bağımsız doğrulanmamıştır. Otomatik tahmin yapılmaz; gerçek kullanıcıdan ölçü toplamak için kullanılmamalıdır.

| Kaynak alanı | Kaynakta ifade edilen kavram; ölçüm yöntemi doğrulanmadı |
|---|---|
| waist | Bel çevresi |
| bust | Göğüs çevresi |
| underbust | Göğüs altı çevresi |
| hips | Kalça çevresi |
| leg_circ | Tek uyluk çevresi |
| wrist | Bilek çevresi |
| waist_back_width | Bel çevresinin arka bölümü |
| back_width | Göğüs çevresinin arka bölümü |
| hip_back_width | Kalça çevresinin arka bölümü |
| neck_w | Boyun tabanı genişliği |
| waist_line | Arka bel hattı uzunluğu |
| waist_over_bust_line | Ön bel–göğüs üzerinden boyun tabanına uzunluk |
| bust_line | Omuz düzeyinden göğüs noktasına uzunluk |
| arm_length | Omuz–bilek uzunluğu |
| height | Boy |
| head_l | Ense–baş tepesi mesafesi |
| shoulder_w | Kaynakta köprücük kemiklerinin dış uçları arası mesafe |
| armscye_depth | Arka omuz–koltuk altı mesafesi |
| bust_points | Göğüs çıkıntıları arası mesafe |
| hips_line | Bel ve kalça düzeyleri arası düşey mesafe |
| bum_points | Kaynakta kalça çıkıntıları arası mesafe |
| crotch_hip_diff | Kalça düzeyi–ağ düşey mesafesi |
| vert_bust_line | Ense ve göğüs çevresi düzeyleri arası düşey mesafe |
| hip_inclination | Kaynakta yan bel–kalça açısı |
| shoulder_incl | Kaynakta omuz hattı eğimi |
| arm_pose_angle | Kaynakta omuz eklemindeki kol poz açısı |

Kaynak PDF, `back_width`, `waist_back_width` ve `hip_back_width` alanlarını iki yan denge çizgisi arasındaki ilgili çevrenin arka **bölümü** olarak tanımlar; denge çizgisi koronal düzlemde bedenin yanından geçen dikey çizgi olarak açıklanır. Bu, kaynakta çevre bölümüdür; düz kiriş olarak yeniden yorumlanmaz. Gerçek kişiler üzerinde çizginin ve ölçüm yolunun tekrarlanabilir biçimde işaretlenmesi ayrıca doğrulanmalıdır.

Kaynak, `waist_line` için sırt yüzeyi üzerindeki mesafeyi tercih eder. Bu yüzden `head_l + waist_line + hips_line < height` ilişkisi geometrik olarak herkes için zorunlu sayılamaz: modelde türetilen `_leg_length` pozitif çıkmıyorsa mevcut araştırma hesabı kapsam dışına alır; bu, kişinin ölçüsünün hatalı olduğunu kanıtlamaz.

PDF, `hip_inclination` için dikey yanın 0° olduğunu ve `shoulder_incl` için yatay düzleme göre eğimi açıklar. `arm_pose_angle` ekseni, `neck_w` ölçüm yolu ve `bum_points` işaretleme işlemi yeterince tarif edilmemiştir; varsayım veya kullanıcıya yeni ölçüm talimatı üretilmez. Omuz-boyun başlangıçlarının pratik işaretlenmesi ve kişinin duruşu da bağımsız protokol incelemesi gerektirir.

`_waist_level`, `_leg_length`, `_base_sleeve_balance`, `_bust_line`, `_hip_inclination`, `_shoulder_incl` ve `_armscye_depth` kaynak koddan hesaplanan türevlerdir; kullanıcı ölçüsü olarak istenmez. C# sözleşmesindeki sınırlar yalnız teknik kontrollerdir, anatomik geçerlilik veya kalıp uygunluğu onayı değildir.


`ResearchMeasurements`, eksik alanları ayrı tutan yalnız araştırma amaçlı bir varlık modelidir; arka bel ve ön kontur formüllerinin doğrudan kullandığı alanlar için eksik listesi sağlar. Listede tüm alanların bulunması anatomik geçerlilik veya model kapsamına uygunluk değildir. Ön gövde ön-kesimi 11, arka bel ön-kesimi 8 gerekli ham alanla araştırma adayı hesaplayabilir; eksik alan tahmin edilmez, kapsam kontrolleri ayrıca uygulanır. Seçili sabit tasarımın altı panel ve dikiş araştırması, `PinnedGarment` işlemindeki 20 ham alanla da çalışabilir; eksik diğer altı alana değer atanmaz. Bu, başka tasarımların aynı alanları gerektireceği anlamına gelmez. Gerçek kişilerden veri toplama açılmamıştır. 0–1000 cm gibi teknik sayısal sınırlar anatomik kabul aralığı değildir; aşırı değerlerin bu aşamayı geçmesi bunların uygun olduğu anlamına gelmez.


Seçili 20 alanın PDF’deki daha ayrıntılı tanımları, eksik işaretleme bilgileri ve izole hata kodları için [sabit tasarım ölçü sözleşmesine](PINNED_MEASUREMENT_SPEC_TR.md) bakın. Bu belge de gerçek kişilere uygulanacak ölçüm talimatı değildir.
