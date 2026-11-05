# Dört ayrı parça: sentetik WPF araştırması

Bu bağımsız WPF deneyi yalnız üç sabit uydurma senaryoya izin verir. İki üst parça `synthetic-worker-isolation-1105` yerel worker'ından alınır, `BodiceReader` ile doğrulanır; iki alt parça, bu çalışma ağacındaki gerçek `ALinePanelPreview.Create` çağrısından alınır. Aynı senaryo ID'si yalnız iki ayrı sabit deney girdisini seçer; girdilerin aynı bedene, ortak ölçüme veya fiziksel olarak birbirine uygun olduğuna dair kanıt değildir. Gerçek kullanıcı ölçüsü alınmaz.

Üst kaynak: Breanna 4.10.2 araştırma worker'ının 15 sabit sentetik ölçüsü ve sabit motor seçenekleri. Ön göğüs pensi 06:00, ikinci pens yok, arka omuz/bel pensi **kapalıdır**. Ekranda 15 girdinin bildirimi, bu pens seçimi ve belirgin göğüs/bel/yaka bolluk seçenekleri gösterilir; geri kalan seçenekler worker sözleşmesindedir. Bu üst girdiler worker kaynak kodundan bu prototipe elle sabitlenmiş senaryo bildirimidir; çalışan motorun kimliği veya bunlarla hesap yaptığına dair bağımsız imzalı ispat değildir.

Alt kaynak: 11 ayrı sentetik ön/arka bel ve kalça yayları, ayrı bolluklar, bel–kalça, bel–etek ve bitmiş etek çevresi; tüm değerler ekranda mm olarak gösterilir. Alt etek ucunda eşit ön/arka dağılımı ve düz kesit varsayımı vardır. Örneğin `synthetic-base` üst bel çevresi girdisi 740 mm iken alt ön/arka beden yayları toplamı 720 mm'dir; bu farkın veya kaynakların uyumlu olduğu varsayılmaz. İki kaynak arasında ölçü, bolluk, pens, bel eğrisi ve birleşim yöntemi eşleştirmesi yapılmaz. Eski `shoulderDart=true/waistDart=true`, 06:00/13:30 konfigürasyonuna ait arka-bel kanıtı bu worker görseline bağlanmaz.

Üstlerin analitik eğrileri ve alt poligonlar dört ayrı panelde bir **ortak ekran ölçeği** ile gösterilir. Paneller arasındaki konumlar yalnız düzen içindir; birbirine birleştirilmiş elbise veya bir dikiş eşleşmesi değildir. Görünür uyarıda bel bağlantısı, dikiş/beden uyumu, üretim, CAD, export, baskı ve cihaz işlevlerinin kapalı olduğu belirtilir. Gerçek fiziksel değerlendirme için bağımsız kalıp uzmanı ve numune prova gerekir.

Worker yerel Windows Job sınırında çalışır; bu ağ yalıtımı değildir. Önceki deneyde ağ bağlantısı mümkün bulunmuştur. Runtime ve transitif bağımlılık kimliği, lisans ve dağıtım koşulları doğrulanmamıştır. Bu WPF deneyini gerçek kişi verisine, ana ürüne veya üretime bağlamayın.

## Test

```powershell
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' build experiments/wpf-four-piece-synthetic/FourPieceResearch.csproj -v:q -m:1
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' experiments/wpf-four-piece-synthetic/bin/Debug/net10.0-windows/FourPieceResearch.dll --verify
```

Başsız doğrulama, ayrı `synthetic-base` ve `synthetic-scaled` worker çağrılarının farklı gerçek üst eğrilerini; iki `ALinePanelPreview.Create` alt sonucunu, beklenen iki farklı alt bel genişliğini; dört geometri için ortak pozitif ekran ölçeğini; renkli WPF ekran dışı çizimi ve senaryo değiştirilince görüntünün temizlenmesi ile eski neslin bastırılmasını sınar. Ana WPF penceresi açılmaz. Bu, gerçek insan verisi, fiziksel dikiş, görünür pencere düzeni, DPI, ekran okuyucu veya tüm sayısal sınırların kanıtı değildir. Node, worker, paket ve imzasız manifest diğer yerel araştırma ağacına ve mutlak yollara bağlıdır; bağımsız dağıtım paketi değildir.
