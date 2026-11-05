# Ölçüden dört ayrı parça: sentetik dikey kesit

Bu, ana uygulamaya bağlanmamış bağımsız WPF geliştirici araştırmasıdır. Model seçimi, açık mm/cm birimi, 15 üst beden ölçüsü, alt panel için 11 ayrı yay/bolluk/boy girdisi, alt panel simetri kabulü ve açık onay ister. Bütün alanlar ve seçimler boş başlar. Kullanıcı isterse açık bir butonla üç **uydurma** test kümesinden birini doldurabilir; gerçek kişi verisi girilmemelidir. Üst ölçü açıklamaları mevcut `BreannaMeasurementGate` tanımlarından gelir ve henüz uzman tarafından doğrulanmamıştır. Alt ön/arka yayları ve bollukları üst motorun toplam/ön ölçü sözleşmesiyle aynı anlama zorlanmaz.

Form ayrıca worker başlatmayan C# yerel bel girdisi kontrolü içerir. Ayrıntılar ve bu kontrolün dikiş uygunluğu anlamına gelmediği sınırları `LOCAL_WAIST_REVIEW.md` dosyasındadır.

Sentetik doldurma düğmesi, herhangi bir ölçü alanı doluysa bunların değiştirileceğine dair açık onay ister; ret halinde alanlara dokunmaz. Onay penceresi açıkken alan, model, birim, örnek seçimi veya onay/simetri durumu değişirse işlem iptal edilir. Değişip eski değere dönme de yeni onay gerektirir. Boş forma yüklemede değiştirilmiş alan olmadığı için değiştirme onayı istenmez; daha sonra ölçü ve simetri onayları ayrıca gerekir.

`Contract.Validate` önce form değerlerinin kopyasını alır; alan adlarını ve sayısını, modeli, birimi ve onayları kontrol eder. Üst 15 ölçüyü mevcut `BreannaMeasurementGate.Review` ile ve alt 11 değeri decimal olarak değerlendirir. Santimetre uzunlukları mm'ye çevrilir; omuz eğimi derecedir. Sentetik değerlerin tam eşleşmesinden sonra alt panel mevcut `ALineMeasurementReview.Normalize` ve `ALinePanelPreview.Create` ile oluşturulur. Üst ve altın aynı beden veya belde birleştiği varsayılmaz; ön göğüs pensi 06:00, ikincil pens yok, arka bel ve omuz pensleri kapalıdır.

Gizlilik ve güvenlik sınırı tamamlanmadığından doğrulanan 26 alanın **tamamı birlikte** yalnız üç sabit sentetik kümeden biriyle eşleşirse çağrı yapılır. Üst ve alt değerler mm cinsinden decimal olarak örnekle karşılaştırılır; alt panel binary64 geometrisi eşleşmeden sonra oluşturulur. Örneğin `350.00000000000000001`, binary64'te `350` ile aynı olsa da sentetik `350` sayılmaz ve worker başlatılmadan reddedilir. Worker'a alan değerleri veya ham metin verilmez; yalnız `synthetic-base`, `synthetic-scaled`, `synthetic-distinct` sabit kimliklerinden biri gönderilir. İşlem bitince bağımsız strict okuyucudan geçen üst ön/arka ve `ALinePanelPreview` alt ön/arka panelleri aynı ekranda ayrı çizilir. Girdi değişikliğinde önceki görünüm ve onay silinir; iptal edilmiş işlem geç sonuç yayımlamaz. Form değerlerini dosyaya veya ağa gönderen uygulama kodu yoktur; bu, işletim sistemindeki diğer süreçlere karşı gizlilik garantisi değildir.

Bu bir **gerçek ölçüden canlı kalıp üretimi değildir**. Worker hâlâ üç sabit örnekle çalışır. Üst motorun ağ izolasyonu, çalışan runtime ve tüm transitif bağımlılıklarının güvenilir kimliği, lisans ve dağıtım bildirimleri doğrulanmadı; yerel Node sürecinde ağ erişimi mümkün olduğu önceki deneyde gözlendi. Bu yüzden gerçek kişi verisi worker'a bağlanmamalıdır. CAD, export, yazdırma, cihaz ve üretim kapıları yoktur. Dikiş semantiği, pens kapama, üst–alt bel eşleşmesi, kalıp uyumu, uzman incelemesi ve numune prova açık; dört panel birbirine dikilebilir elbise olarak sunulamaz.

## Test

Bu ağacın kökünde:

```powershell
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' build experiments/measurement-vertical-slice/MeasurementSlice.csproj -v:q -m:1
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' experiments/measurement-vertical-slice/bin/Debug/net10.0-windows/MeasurementSlice.dll --verify
```

Başsız doğrulama, 26 alanın varsayılan boşluğunu; boş form doldurmayı, dolu formda değiştirme reddini ve kabulünü, onay sırasında değişen birim ile değişip geri alınan alan/model durumlarında iptali; üç sabit örneğin mm/cm eşdeğerliğini; binary64'te aynı değere yuvarlanan farklı decimal alt girdilerin mm ve cm olarak worker öncesi reddini; her alanın eksikliği, fazla alan, sayısal hata, uzun girdi, değiştirilmiş üst veya alt değer, model/birim/onay/simetri eksikliğinde worker'a hiç çağrı yapılmamasını; önceden iptali; iki farklı gerçek sentetik worker sonucu ve alt panelini; çıktı kapılarını; geç sonuçta iptali ve görüntü/onayın girdi değişince temizlenmesini sınar. Onay seçimleri sentetik callback ile taklit edilir; gerçek MessageBox etkileşimi test edilmedi. WPF ekran dışında iki kaynak renginin çizildiği kontrol edilir; görünür pencere, gerçek kullanıcı, ekran okuyucu veya fiziksel prova test edilmedi. Bu makinede derleme ve başsız kontrol geçti, derleme uyarısı yok.

Gerçek dikey kesit için bir sonraki bağımsız kapılar: güvenilir kaynaktan sabitlenmiş ve imzalı runtime/paket dağıtımı ve lisans envanteri; işletim sistemi düzeyinde ölçümün dış ağa ve istenmeyen dosyalara çıkmasını engelleyen sınırın bağımsız testi; eksiksiz seçenek/ölçü ve hata protokolü; ardından uzmanla üst–alt bel, pens ve dikiş yöntemi ile numune doğrulaması. Bu deney, mevcut sentetik-only kontrolü kaldırmadan bu işleri yapmaya yönelik form ve sözleşme iskeletidir.
