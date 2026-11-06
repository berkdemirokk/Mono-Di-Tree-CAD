# Birleşik sentetik aday: kalan dikişler ve ölçüm varsayımları

Bu inceleme yalnız `synthetic-base` ve `synthetic-distinct` fixture'ları ve sabit `FittedShirt` + `PencilSkirt` araştırma yolunu kapsar. Önceki bel–yan düzeltmesine temsil edilen kontur kanıtı bulunan `−1` yönlü ön omuz denemesi eklenmiştir. Bütün 23 kuralın önce/sonra aralıkları ve kapılar [önceki deney kaydında](SHOULDER_GRAPH_RESEARCH_TR.md) bulunur. Bu sonuç kaynak Python kalıbının yeniden üretilmiş değiştirilmiş sürümü veya gerçek kalıp değildir.

## Kalan ayrık ilişkiler

23 kuralda `base` için 8, `distinct` için 6 kuralın projeksiyon aralıkları ayrık kalır. Bunların **tamamı pens bacağı çiftleridir**; önceki omuz farkı bu sınırlı adayda ayrık değildir. Aşağıdaki boşluklar tam rasyonel aralık uçları arasındaki mesafenin yaklaşık görüntüsüdür, aralıkların ayrıklığı ise tam tamsayı karşılaştırmasıdır. Kaynak optimizeri, temsil öncesi yuvarlama ve hata yayılımı kapsanmadığından bu değerler fiziksel ölçüm hassasiyeti veya pratik hata miktarı olarak kullanılamaz.

| Kalan grup | Panel ve kenarlar | Base | Distinct |
|---|---|---|---|
| Ön gövde pensi | Sol `left_ftorso:1↔2`, sağ `right_ftorso:10↔9` | İki kural; yaklaşık `1,84×10⁻¹⁶ cm` | Ayrık değil |
| Arka gövde pensleri | Sol `left_btorso:8↔7`, `11↔10`; sağ `right_btorso:4↔5`, `1↔2` | Dört kural; yaklaşık `4,32×10⁻¹⁷–1,68×10⁻¹⁶ cm` | Dört kural; yaklaşık `3,13×10⁻¹⁷–2,34×10⁻¹⁶ cm` |
| Arka etek pensleri | `skirt_back:3↔4`, `13↔14` | İki kural; yaklaşık `1,76×10⁻¹⁶ cm` | İki kural; yaklaşık `1,88×10⁻¹⁶ cm` |

Bu değerler önceki [kenar bazlı tanı kaydından](SHOULDER_SEAM_RESEARCH_TR.md) gelir; son omuz değişiminde diğer 21 kuralın temsil edilen aralıkları değiştirilmediği test edilir. Çift sayısında büyük bir ayrıklık kalmaması, büzgü projeksiyonunda örtüşmenin gerçek eşitlik olduğunu veya bütün panel şekillerinin dikilebilir olduğunu göstermez. Bu sayısal ayrılıkların nedeninin yalnız kayan nokta yuvarlaması olduğu da kanıtlanmamıştır.

[Kaynak–aday pens incelemesi](DART_RESIDUAL_SOURCE_TR.md), her kalan çiftin kaynak fixture’ında da temsil edilen uzunluklarının ayrık olduğunu ve arka etek penslerinde ortak ucun yatay tabanın tam orta noktasında bulunmadığını gösterir.

## Omuz şekli ve kaynak eşleme

| Fixture | Kaynak C# ön omuz uzunluğu | Yeni öne göre aynı parametreli azami sapma | Kaynak ön uzunluğa oranı |
|---|---:|---:|---:|
| base | ≈ 6,5075 cm | ≈ 0,6697 cm | ≈ %10,29 |
| distinct | ≈ 6,4943 cm | ≈ 0,6938 cm | ≈ %10,68 |

Sapma önceki düz **C# araştırma konturuna** göre, başlangıç ve bitişleri sabit kuadratik eğrinin orta noktasında hesaplanır. Bu, aynı parametrede sapmadır; gerçek kişinin omzu, en yakın-nokta mesafesi veya estetik uygunluk ölçüsü değildir. Arka omuz uzunluğuna sayısal yaklaşmanın karşılığında yaklaşık 6,7–6,9 mm kontur değişimi vardır. Sadece kenar uzunluğuna ve temsil edilen kesişimsizliğe bakarak bu büyüklüğün uygun olduğunu söyleyemeyiz. Kontrol noktaları, yön ve fiziksel dikiş payı ayrı tasarım/prova değerlendirmesi gerektirir.

Sabitlenen kaynak fixture’ının 23 dikiş kaydındaki sahip/tip/indeks, taraflar, panel/kenar sıraları, yön bayrakları ve büzgü bölümleri [CombinedGraphStudy](CombinedGraphStudy.cs) ve `SyntheticFullSeamGraph.Compare` ile karşılaştırılır. Omuz için sağ `right_ftorso:2 ↔ right_btorso:10`, sol `left_ftorso:9 ↔ left_btorso:2`; kaynak fixture'da katsayı 1’dir. Yanlış kenar, panel, yön, farklı beden snapshot'ı ve ayna bozulması negatif testlerdir. Kontur, kimlik, katsayı ve topoloji kontrolleri değişmiş şeklin kaynak tarafından yeniden optimize edildiğini, dikişin kumaşta doğru olacağını veya gerçek çentik eşleşmesini göstermez. Ruffle 1,05 kullanılan etek yan bölümü hakkında kaynak satırları ve projeksiyon ayrımı [önceki incelemededir](SHOULDER_SEAM_RESEARCH_TR.md).

## Ölçüm ve sınır varsayımları

`PinnedGarment` yolu 20 sentetik alan kullanır; alanların kaynak anlamı ve gerçek kişi protokolündeki boşluklar [ölçüm kanıtı matrisinde](MEASUREMENT_EVIDENCE_TR.md) yer alır. Özellikle arka bel çevresi bölümü ile gövde hedefi, göğüs/omuz boyutları ve açısı model varsayımlarıdır; uydurma sentetik değerler bir insan üzerinde ölçülmemiştir. Arka yarım bel hedefi `waist_back_width / 2`, ön–arka yan ve omuz hedefleri kaynak büzgüsüz ilişkilerin sayısal uzunluk karşılaştırmalarıdır. Kaynak `StitchingRule` tam eşitliği fiziksel zorunluluk olarak uygulamaz; bu denemenin sayısal hedefleri kaynakta doğrulanmış prova toleransı değildir.

Bel uç kayması/ilgili kontur sapması için 0,3 cm, arka yan eğrisi için 1,5 cm, omuz için 1 cm araştırma üst sınırları ve karşılaştırmadaki 1e−12 cm sayısal marj keyfi araştırma koşullarıdır. Omuz ve yan uzunluklarında 1e−4 cm, belde 1e−5 cm bantlar yalnız temsil edilen uzunlukların sayısal denetimidir. Çok küçük pens farklarını ya da bu bantları gerçek dikimde gerekli tolerans diye sunmak doğru değildir.

Bernstein çember–kuadratik testine iç teğet temas ve derinlik/düğüm bütçesi tükenmesi karşı örnekleri eklenmiştir: bunlar `Unresolved` kalır. Aynı bütçe örneği yeterli alt bölmeyle `OnlySharedEndpoint` kanıtına ulaşarak testin gerçekten bütçe sınırını çalıştırdığını gösterir. Aday gerçek ölçü ve prova için doğrulanmadı; `unresolved` / `Unverified`, gerçek veri, kalıp çıktısı, üretim ve plotter kapalıdır.
