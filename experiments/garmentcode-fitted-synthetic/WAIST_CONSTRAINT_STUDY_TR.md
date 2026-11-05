# Arka bel düzeltmesi: sabit uç ve pens fizibilitesi

Bu deney yalnız sabit kolsuz üst/kalem etek tasarımındaki **arka üst yarım bel** araştırmasıdır. Hedef, arka eteğin seçili tasarımdaki bel ilişkisi için yarım `waist_back_width` uzunluğuna **tam eşitlik** kabulüdür; kumaş rahatlığı, tolerans veya başka tasarım varsayılmaz. Sonuç üretim, gerçek ölçüm veya prova onayı değildir.

## Denenen kısıtlar ve matematiksel engel

Orta arka ve yan uçlar sabit kalsın; iki pens şekilleri ve yönleri bozulmadan yer değiştirebilsin. Belde pensler arasında kalan üç eğri parçasının başlangıçtan sona vektörleri `v₁`, `v₂`, `v₃` olsun. Pensler yalnız ötelendiğinde her pensin giriş–çıkış vektörü değişmez. Dış uçlar sabit olduğu için üç bel parçasının toplam yer değiştirme vektörü de sabittir. Herhangi bir eğrinin uzunluğu uçları arasından kısa olamayacağından:

`toplam bel uzunluğu ≥ |v₁| + |v₂| + |v₃| ≥ |v₁ + v₂ + v₃|`

`BackWaistConstraintStudy`, bu alt sınırın karesini hedefin karesiyle, temsil edilen binary64 değerlerini **tam dyadic tamsayılara** çevirerek karşılaştırır. Karekök veya yuvarlanmış gösterim karar için kullanılmaz. Alt sınır hedeften büyükse, bu kısıtlar altında hedefe erişmek; eğri kontrol noktaları ne olursa olsun, hatta pensler çakışma gözetilmeden yalnız ötelense bile mümkün değildir. Kontrol bu koşullarda kaynak ve C# adayını ayrı ayrı inceler.

Bu sonuç penslerin **döndürülmesine**, deforme edilmesine, giriş–çıkış vektörlerinin değiştirilmesine, dış uçların kaydırılmasına veya açıkça tanımlanmış bir rahatlık payına izin verilen başka bir problemi kapsamaz. Alt sınır hedefin altında/eşitse sonuç yalnız `Unresolved` olur; çözüm bulunduğu anlamına gelmez.

## Beş sentetik örnek: önce ve engel

Aşağıdaki uzunluklar bir arka yarım içindir, cm cinsinden yaklaşık gösterilmiştir. `Kaynak önce`, kaynak fixture’ındaki üç bel parçasının toplamıdır; `Alt sınır`, sabit dış uçlar ve bozulmadan/döndürülmeden taşınan pensler için vektör sınırıdır. `Sonra` yoktur: prototip çözüm üretmek yerine açık hata verir.

| Sentetik örnek | Hedef | Kaynak önce | Kaynak alt sınır | Sonra |
|---|---:|---:|---:|---|
| temel | 18,000000 | 18,178554 | 18,129786 | Yok; kısıtlarla erişilemez |
| farklı oran | 17,000000 | 17,157785 | 17,114997 | Yok; kısıtlarla erişilemez |
| üçüncü | 19,000000 | 19,119939 | 19,087278 | Yok; kısıtlarla erişilemez |
| küçük fark, 30° | 19,000000 | 19,305181 | 19,230393 | Yok; kısıtlarla erişilemez |
| beşinci | 17,500000 | 17,574954 | 17,554648 | Yok; kısıtlarla erişilemez |

C# adayı aynı beş örnekte ayrıca sınanır; kendi temsil edilen uçları için de kesin kare karşılaştırması bu kısıtlarla erişilemezlik verir. Kaynak ve C# sayıları birebir aynı sayılmaz: özellikle 30° örneği önceden bilinen optimizer karşı örneğidir. Kaynakta saklanan eğri uzunluğu ile C# adayının temsil edilen kontrol noktalarına ait uzunluk aralıkları test çıktısında ayrı yazılır. Önceki kayan nokta hesapları ve kaynak optimizerinin hata aralığı bu sonucun kapsamı dışındadır.

## Önce/sonra topolojisi ve güvenlik sınırı

Düzeltme **öncesinde** C# arka ön-kesim adaylarının beşinde temsil edilen 11 kenarın 55 çiftinden 44’ü ayrık, 11’i yalnız ortak uç olarak sınıflandı; kullanılan yeterli monotonluk kontrolünden geçmeyen kenar yoktu. Bu yalnız temsil edilen kontrol noktaları için sonuçtur. Düzeltme **sonrası** topoloji veya uzunluk yoktur: her beş örnek kısıtlarla erişilemez olduğundan prototip hiçbir düzeltilmiş panel üretmez. Kaynak topolojisi ve fiziksel kumaş hakkında bu sayılardan garanti çıkarılmaz.

`AttemptCorrection`, erişilemezlik halinde açık hata verir; alt sınırın hedefi dışlamadığı hallerde de sertifikalı çözümleyici bulunmadığı için açık hata verir. Eksik/hatalı beden veya bozuk topoloji reddedilir. Eşik eşitliği ve bir binary64 adım üst/altı, pens ötelemesi altında vektör değişmezliği, hatalı giriş, beş kaynak fixture’ı ile 20/26 alanlı C# adayları test edilir. Tüm sonuçlar `Unverified`/`unresolved`, üretim/dışa aktarma/plotter kapalıdır.
