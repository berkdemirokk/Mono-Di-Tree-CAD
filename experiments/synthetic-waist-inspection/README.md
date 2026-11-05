# Sentetik arka yarım bel karşılaştırması

Bu bağımsız WPF araştırma projesi yalnız sabit uydurma senaryolar içindir. Kullanıcı önce senaryoyu, sonra tek mevcut ölçüm seçeneğini seçer: **arka yarım bel, üst kaynak adayı ↔ alt arka bel çizgisi**. Dört panel ayrı ve aynı ekran ölçeğinde gösterilir. Çıktı iki uzunluğu ve üst eksi alt farkını mm cinsinden yaklaşık olarak gösterir; eşik, pass/fail, dikiş eşleşmesi, üretim veya gerçek bedene uyum kararı vermez. Aynı senaryo ID iki farklı deney girdisini seçer; tek beden ve uyum kanıtı değildir.

## Kaynak dayanağı ve kapsam

- Üst: önceki worker'ın Breanna 4.10.2 `primary-only` sentetik seçeneği. Ön birincil göğüs pensi 06:00, ikincil pens yok; arka bel ve omuz pensleri **kapalıdır**. Kaynak `back.mjs` dikiş şablonunda arka `cbWaist → waist` çizgisidir (`breanna.back/seam/op-3`, kaynak işlem indeksi). Bu, ön pensli bel için bir sonuç değildir.
- Alt: mevcut `ALinePanelPreview.Create`, arka kontur noktaları 0 → 1, merkezden yana düz bel çizgisi; ön/arka yaylar ve bolluklar ayrı girilmiş sabit sentetik değerlerdir. Form ekranı bu girdileri açıklar. Üst ve alt ölçüm/bolluk tanımları farklıdır.
- Bu iki hat yalnız karşılaştırma amacıyla seçilir. Yön, pensin fiziksel davranışı, bel eğrisi, çentik, kumaş, dikiş yöntemi ve fiziksel eşleşme doğrulanmadı. Kaynak adayın geometrik uzunluğunun yakın olması bile dikilebilirlik veya beden uyumu ispatlamaz. Ön bel ve diğer kenar ilişkileri çözümlenmemiştir.

`source-cases.json`, daha önceki üç sabit `primary-only` sentetik matris örneğinin arka sınır işlemlerini ve nokta adı adaylarını taşır. Üreteç, eski matris dosyasının ve okunan Breanna `back.mjs` dosyasının SHA-256 değerlerini kaydeder; WPF deneyi beklenen hash metinlerini, seçimdeki dört pens seçeneğini, kaynak aday adlarını/indeksini ve tüm canlı arka yolun koordinat ve eğri kontrol noktalarını fixture ile birebir karşılaştırır. Yanlış üst senaryo, yanlış alt panel, değişmiş kaynak kenarı veya farklı pens seçeneği sonuç üretmez. İşlem kimliği `(senaryo, primary-only, breanna.back, seam/op-3)` bağlamında kullanılmalıdır; küresel veya kalıcı kimlik değildir.

Kaydedilen hash metinleri ve şekil eşitliği kaynak/runtime kimliği için bağımsız imzalı kanıt değildir: çalışan Node modülü ve transitif bağımlılıkların tam kimliği, güvenilirliği, lisansları ve ağ yalıtımı hâlâ doğrulanmamıştır. Fixture içindeki isimler önceki kaynak araştırmasından gelen koordinat-adı adaylarıdır; kaynak nesnelerinin anlamsal kimliğinin tam korunduğu iddia edilmez. Hesaplanan Öklid uzunlukları binary64 yaklaşık değerleridir; belirsizlik veya uzman toleransı değildir. Node ağ bağlantısını kısıtlamaz; gerçek kişi verisi gönderilmez.

## Sentetik sonuçlar ve test

Bu makinedeki `--verify` iki farklı sabit senaryo için gerçek worker ön/arka çıktısını ve gerçek `ALinePanelPreview.Create` alt panellerini çalıştırdı:

| Senaryo | Üst arka yarım çizgi | Alt arka yarım çizgi | Üst − alt |
|---|---:|---:|---:|
| synthetic-base | yaklaşık 203,500 mm | 195,000 mm | +8,500 mm |
| synthetic-scaled | yaklaşık 227,920 mm | 218,400 mm | +9,520 mm |

Bu sayılar uydurma girdilerin hesap sonucudur; tolerans veya fiziksel uyum değerlendirmesi değildir. Test ayrıca farklı senaryodan üst veya alt parça geçirilmesini, arka pens seçeneği ve kaynak kenarı değişimini reddeder; seçim değişince eski ölçümün silinmesini ve geç gelen worker sonucunun bastırılmasını denetler. Başsız test 0 hata ve 0 derleme uyarısıyla geçti; görünür pencere, erişilebilirlik, gerçek kullanıcı ve fiziksel prova test edilmedi.

```powershell
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' build experiments/synthetic-waist-inspection/WaistInspection.csproj -v:q -m:1
& 'C:\Projects\TekstilCAD\.tools\dotnet\dotnet.exe' experiments/synthetic-waist-inspection/bin/Debug/net10.0-windows/WaistInspection.dll --verify
```

Kaynak worker ve paketler diğer yerel araştırma ağacındaki mutlak yollara bağlıdır; bu bir dağıtım paketi değildir. CAD, üretim, export, yazdırma ve cihaz kapıları kapalıdır. Ölçü alma yöntemi ve dikiş seçenekleri uzmanla belirlenmeden ve gerçek numune prova yapılmadan bu çalışma elbise kalıbı olarak sunulmamalıdır.
