# Yerel bel girdisi incelemesi

Bu izole WPF araştırması, önceki 26 alanlı üst/alt sentetik dikey kesite bir **yalnız C# yerel inceleme** ekler. Gerçek kişi verisi bu araştırma formunda kullanılmamalıdır. Kontrol `BreannaMeasurementGate`, `ALineMeasurementReview` ve `ALinePanelPreview.Create` ile aynı form sözleşmesini yerelde değerlendirir; Node/FreeSewing worker, ağ, dosyaya kayıt veya üçüncü taraf servise form değeri göndermez. UI'da ayrı bir düğmedir ve worker başlatmaz. İşletim sistemi veya makinedeki diğer yazılımlara karşı gizlilik yalıtımı garantisi değildir.

Kullanıcı model, birim, bütün alanlar, alt simetri ve sentetik onayı yanında, üst bel çevresi ile alt ön/arka yaylarının aynı beden ve **aynı bel hizasında** alındığını açıkça belirtir. Farklı referansta veya belirsizse bu beyan verilmemeli; kontrol durur. Kullanıcı karşılaştırma için kendi sayısal toleransını seçilen mm/cm biriminde açıkça girmelidir; sıfır yazılabilir, varsayılan tolerans yoktur. Tolerans ölçüm toplamlarının karşılaştırılması içindir; dikiş veya üretim toleransı değildir.

Sayısal giriş mevcut `MeasurementInput` sözcüksel kontrolünü kullanır ve ayrıca ayırıcıdan sonra tam üç basamak bulunan biçimleri belirsiz sayar: `1,000`, `1.000` ve `0,001` reddedilir; örneğin `1` veya `0,0010` açık biçimde yazılabilir. Bu kural ölçü alanları ve tolerans için aynıdır. Karşılaştırılan decimal değerler ve fark sabit üç haneye yuvarlanmadan tam gösterilir; alt çizgi uzunlukları double hesap olduğu için yaklaşık olarak round-trip biçiminde gösterilir.

Dolu veya kısmen dolu formda mm/cm değiştirilmek istendiğinde bütün dolu uzunluklar ve tolerans önce kayıpsız dönüştürülüp denetlenir; omuz açısı derece olarak bırakılır. Kullanıcı açıkça onaylarsa ve onay sırasında form/model/birim/örnek veya onaylar değişmemişse değerler tek işlemde uygulanır ve önceki inceleme onayı temizlenir. Ret, geçersiz/belirsiz kısmi alan, gösterim sınırını aşan sonuç veya uygulama hatasında önceki değerler ve birim korunur. Alan değişip geri dönse bile bekleyen onay geçersizdir. Birim henüz hiç seçilmemişse ilk seçim mevcut sayıları dönüştürmez; onların birimini açıkça belirler.

## Yerel sonuç ve düzeltme rehberi

Üst beden için girilen **beden bel çevresi**, alt için girilen ön+arka **beden bel yayları** toplamıyla decimal aritmetiğiyle karşılaştırılır; işaretli fark ve kullanıcının açık toleransı gösterilir. Fark dışındaysa yönü gösterilir ve kullanıcıya üst çevresini, alt yayların aynı bel hizası ve yan noktalar üzerinden ölçülmesini, birimini ve kaynak değerleri kontrol edip yalnız doğru bilgiyi kendisinin düzeltmesi söylenir. Program farkı otomatik olarak ön veya arkaya paylaştırmaz; tolerans içi de bedene uyum onayı değildir.

Gerçek `ALinePanelPreview.Create` alt geometrisinden ön ve arka yarım bel çizgisi uzunlukları ayrıca yaklaşık mm olarak gösterilir; bu çizgiler alt panele ayrı girilmiş bollukları içerir. Üst beden çevresi **üst dikiş hattı uzunluğu değildir**. Üst parçanın gerçek dikiş kenarı, ön pensin kapatılması, ortak eşleşme yönleri ve dikilebilirlik çözümlenmemiştir; sonucu `SewingStatus=unresolved`, `ProductionAllowed=false`. Bu nedenle kontrol iki bedensel girdi sözleşmesini karşılaştırır; üst–alt dikiş eşleşmesini doğruladığını iddia etmez.

Örnek sentetik küme: üst beden beli 740 mm, alt ön ve arka yaylar 350 + 370 = 720 mm, fark +20 mm. Kullanıcı 0 mm tolerans girerse sayıların dışında olduğu belirtilir; örneğin 20 mm tolerans girilirse yalnız sayıların o beyan edilen aralık içinde olduğu belirtilir. Alt ön/arka yarım bel çizgileri ayrı bolluklarla yaklaşık 180 / 195 mm'dir. Bu sayılar veya tolerans bir öneri değildir.

## Test ve kapılar

```powershell
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' build experiments/measurement-vertical-slice/MeasurementSlice.csproj -v:q -m:1
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' experiments/measurement-vertical-slice/bin/Debug/net10.0-windows/MeasurementSlice.dll --verify
```

Başsız testler sentetik örnekle üst/alt beden farkını, gerçek alt geometri uzunluklarını, açık tolerans sınırını ve cm/mm eşdeğerliğini; `1,000` gibi belirsiz yazımın reddini ve sıfıra yuvarlanmayan küçük fark/toleransı; kısmi/tam form dönüşümü, mm↔cm sayısal gidiş dönüşü, açı değişmezliği, iptal, bekleyen onayda alan/model/birim değişikliği, gösterilemeyen sonuç reddi ve enjekte edilmiş hata sonrası geri almayı; referans beyanı/tolerans eksikliği ve geçersiz tolerans reddini; rakamlar eşit olsa bile dikiş durumunun `unresolved` kalmasını; ekrandaki rehberin görünmesini ve ölçü değişince silinmesini sınar. Önceki 26 alan, sentetik-only worker sınırı, doldurma onayı/yarış, iki sentetik worker çıktısı ve eski sonuç kontrolleri de çalışır. Gerçek onay penceresi testte açılmaz; karar callback ile taklit edilir. Pencere gösterilmez; gerçek kullanıcı, ekran okuyucu, fiziksel ölçüm veya prova test edilmedi.

Ayrı sentetik worker'ın Node ağ izolasyonu, çalışan bağımlılık kimliği, lisansları ve dağıtımı hâlâ doğrulanmamıştır. Kullanıcı ölçüleri worker'a açılmaz. CAD, export, baskı, cihaz ve üretim kapıları kapalıdır. Üst dikiş geometrisini güvenli ve kaynak semantiğiyle üretebilmek, pens sonrası gerçek dikiş uzunluğunu ve karşılık gelen alt hattı uzman yöntemiyle ölçmek, ayrı test ve nihayet uzman incelemesi/numune prova sonraki işlerdir.
