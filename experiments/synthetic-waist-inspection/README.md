# Sentetik ön ve arka bel zinciri incelemesi

Bu bağımsız WPF araştırma projesi yalnız sabit uydurma senaryolar içindir. Seçili tek desteklenen model: Breanna 4.10.2 ön göğüs pensi **06:00**, ikincil pens yok, arka bel ve omuz pensleri kapalı. İki üst yol ile iki `ALinePanelPreview.Create` alt paneli aynı ekranda ayrı gösterilir. Senaryo kimliği, iki farklı kaynaktaki sabit sentetik girdileri seçer; ortak beden veya uygunluk kanıtı değildir. Ön ve arka her biri kendi alt hattıyla yalnız ölçü incelemesi amacıyla eşlenir; dikiş eşleşmesi varsayılmaz.

## Kaynak zinciri ve somut eksik

`front-primary-only.mjs` 600 dalında ön dikiş yolu `cfWaist` sonrasına `primary` pensini ekler; `dart-utils.mjs` pens yolunu `primaryBustDart1 → primaryBustDartTip → primaryBustDart2` olarak tanımlar ve `front.mjs` bunu `insop` ile şablona ekler. Önceki sentetik matrisin bu konfigürasyonunda ön bel kaynak zinciri şu dört düz işlemdir:

`cfWaist → dart1 → dartTip → dart2 → waist`.

Dıştaki iki çizgi, iki pens bacağından ayrılabilir. Bu, **kaynak yolunun yapısına ilişkin gözlemdir**. Kaynak uçlarında `origBustDart1/primaryBustDart1` ve `origBustDart2/primaryBustDart2` koordinat-adı çakışmaları vardır; bu isimler yalnız aday kaynağıdır. Pensin üç boyutlu katlanması, bacakların kumaşta oturması, bel eğrisinin düzeltilmesi, üst-alt yön/refakat noktaları ve dikiş toleransı hesaplanmış veya fiziksel doğrulanmış değildir. Dış çizgi toplamına *dikilebilir bel uzunluğu* sertifikası verilmez.

Somut uydurma `synthetic-base` örneğinde ön üstte iki dış çizginin toplamı yaklaşık **203,500 mm**, pens bacakları dahil açık kaynak yolunun dört çizgisi **512,011 mm**, merkez–yan uçları arasındaki düz mesafe ise **320,877 mm**'dir. Bunlar aynı şekil üzerindeki farklı ölçüm tanımlarıdır; bu fark tek başına doğru dikim hattını seçmeye, pensin kapatılmış geometrisini kurmaya veya alt panele uydurmaya yetmez. Alt ön bel çizgisi ayrıca **180,000 mm**'dir. Bu, bel hattının semantik olarak tanımlanması ve pens sonrası üst–alt ortak hat geometrisinin oluşturulmasının, dört görünür konturu tek bir elbiseye dönüştürmek için hâlâ kritik eksik olduğunu somut olarak gösterir.

Arka üstte seçili penssiz kaynak adayı `cbWaist → waist`, kaynak `breanna.back/seam/op-3`; alt arka çizgi noktalar 0 → 1'dir. Bu çizgiler de yalnız araştırma karşılaştırmasıdır. Ekranda ön dış çizgiler ile ön alt ve arka aday ile arka alt ayrı raporlanır; otomatik tolerans, pass/fail veya uyum onayı üretilmez.

| Sentetik senaryo | Ön üst dış çizgiler | Ön alt | Arka üst adayı | Arka alt |
|---|---:|---:|---:|---:|
| base | ≈203,500 mm | 180,000 mm | ≈203,500 mm | 195,000 mm |
| scaled | ≈227,920 mm | 201,600 mm | ≈227,920 mm | 218,400 mm |

Bu sayıların tamamı hesaplanmış uydurma örneklerdir; ölçüm doğruluğu veya dikim toleransı değildir.

## İzlenebilirlik ve sınırlar

`front-source-cases.json` ve `source-cases.json`, önceki sentetik matrisin `primary-only` senaryolarından üretilmiştir. Üreteçler matris ve kaynak dosyası SHA-256 değerlerini kaydeder; bu kod belirli kaynak/konfigürasyon hash metinlerini, bütün canlı ön/arka çizgi ve kübik işlemlerinin fixture ile tam geometrik eşitliğini, pens ve kenar adayı sırasını ve alt panelin aynı senaryo sonuçları olduğunu kontrol eder. Değişik kaynak kenarı, eksik isim adayı, farklı pens veya karışık üst/alt senaryo karşılaştırma üretmez. Kaydedilmiş hash metinleri, kaynak ve çalışan runtime'ın imzalı ve güvenilir olduğu anlamına gelmez; nokta isimleri önceki koordinat-adı eşleşmelerine dayalıdır.

İki üst çıktının gerçek worker'ı yerel Node bağımlılıklarına dayanır. OS ağ izolasyonu, çalışan runtime/bağımlılık zinciri ve lisanslar doğrulanmadı; gerçek kişi ölçüsü verilmez. CAD, üretim, export, baskı ve cihaz yok. Formun kullanıcıya görünür düzeni, ekran okuyucu, dikiş ve fiziksel uygunluk test edilmedi; bağımsız kalıp uzmanı ve gerçek numune prova gerekir.

## Başsız test

```powershell
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' build experiments/synthetic-waist-inspection/WaistInspection.csproj -v:q -m:1
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' experiments/synthetic-waist-inspection/bin/Debug/net10.0-windows/WaistInspection.dll --verify
```

İki farklı sentetik worker çıktısı ve gerçek alt panel hesapları; ön zincirin dört işlemi, pens bacaklarının açık hat/dış hat farkı, arka hat ölçümü, farklı pens/isim/koordinat/üst veya alt senaryo reddi ve eski sonucun temizlenmesi test edilir. Test görünür pencere açmaz. Yerel mutlak yollar, diğer araştırma ağacının paketleri ve imzasız manifest gereklidir; bağımsız dağıtım paketi değildir.
