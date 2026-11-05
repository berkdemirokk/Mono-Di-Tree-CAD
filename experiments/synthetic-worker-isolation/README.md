# Yalnız sentetik worker ve izolasyon deneyi

Üç sabit uydurma senaryo (`synthetic-base`, `synthetic-scaled`, `synthetic-distinct`) için önceki yerel Breanna 4.10.2 araştırma motoru, tek sabit pens seçeneğinde ön ve arka dikiş eğrileri üretir. stdin için en fazla 1024 baytlık dar bir gramer uygulanır: yalnız tek, kaçışsız `id` anahtarı ve üç sabit değerden biri; JSON boşluk karakterlerine izin verilir. Yinelenen anahtar, kaçışla yazılmış anahtar veya değer, başka alan ve bilinmeyen kimlik reddedilir. Reddedilen ham istek doğrulama sırasında yine de belleğe ulaşır; worker'a kullanıcıya ait ölçüler veya hassas metin gönderilmemelidir. Sonuçlar araştırma amaçlıdır ve `approved:false`; CAD, çıktı, baskı, cihaz, üretim veya fiziksel uygunluk yetkisi yoktur.

## Gözlemlenen modüller ve sınırları

Node 24.22.1 `registerHooks` yükleyici kancası, dinamik olarak yüklenen 96 dosya modülünün işlenmek üzere döndürülen kaynak baytlarını SHA-256 ile karşılaştırır; bilinmeyen/değişmiş kaynak veya eksik gözlemlenen modül kümesi worker sonucunu durdurur. `observed-manifest.json`, bu makinedeki ilk sentetik çalıştırmanın **yerel, imzasız gözlemidir**; güvenilir yayımlayıcıdan gelen, yetkilendirilmiş manifest değildir. Mutlak yollar içerir, başka kuruluma taşınamaz. Aynı kullanıcı manifest ve kodu değiştirebilir. Kanca kendi bootstrap dosyası yüklenmesinden sonra kurulur; bu dosyanın çalışan baytlarını kapsamaz. Node yerleşik modülleri, yerel kütüphaneler, paket çözümlemesinde okunan metadata ve diğer olası yükleme yollarının tamamı doğrulanmış değildir. `--no-addons` kullanılır ama bunun tüm yerel kodları kapsadığı iddia edilmez.

Node yürütülebilir dosyasının disk hash'i süreç başladıktan sonra yerel manifestle karşılaştırılır; o an çalışan imajın gerçekten bu baytlardan oluştuğunu, imzasını veya tüm bağımlılık closure'ını kanıtlamaz. Dönüşte `executionIdentity: unresolved` ve bu sınırlamalar açıkça korunur. Lisanslar ve dağıtım bildirimleri çözümlenmedi. Bu deney, gerçek kullanıcı verisi için güvenilir kaynak/runtime zinciri olarak kabul edilmez.

## Ağ ve dosya sınırı

Deney `--permission --allow-fs-read=* --no-addons` ile çalıştırılır. Bu çalıştırmada ayrıca izin verilmeyen Node dosya yazma API'sinin engellendiği sentetik testle gözlendi; tüm işletim sistemi dosya erişimlerinin veya gizli yan etkilerin kapandığı kanıtlanmadı. Yerel TCP deneyi, aynı Node izin modundaki bir sürecin `127.0.0.1` sunucusuna **bağlanabildiğini** gösterdi. Bu sebeple ağ erişimi kapalı değildir; herhangi bir dış internete erişim denemesi yapılmadı. WSL kurulu değil ve Windows yönetici grubu etkin yetkiyle kullanılmıyor. İşletim sistemi düzeyinde deny-by-default ağ yalıtımı ve bağımsız doğrulaması mevcut değildir; gerçek kişisel verileri bu worker'a göndermeyin.

## Tekrarlama ve test

Bu iş ağacının kökünden:

```powershell
& 'C:\Users\pc\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' --test experiments/synthetic-worker-isolation/worker.test.mjs
```

**6/6 sentetik test geçti:** üç farklı sentetik şekil ve 96 gözlemlenen modül kümesi; yinelenen anahtar, escape, ek alan, geçersiz UTF-8 ve boyut sınırları; Node dosya yazma izni yokken beklenen ret; yerel loopback bağlantısının mümkün olduğunu gösteren negatif izolasyon testi. Bu son testin geçmesi, ağın güvenli olduğu anlamına gelmez: tersine izolasyonun eksikliğini doğrular. Önceki kaynak ağacının yerel paketleri ve kaydedilmiş mutlak yollar gereklidir. Testler gerçek kullanıcı verisi veya ücretli hizmet kullanmaz.

Gerçek ölçülere geçmeden önce işletim sistemi düzeyinde kapalı ağ/dosya ortamı, son kullanıcı yetkileri altında bağımsız saldırı testleri, yüklenen runtime ve tüm bağımlılıklar için güvenilir imzalı dağıtım ve değiştirme yarışı koruması, gizlilik ve lisans incelemesi gerekir. Dikiş/topoloji, uzman kontrolü ve numune prova ayrıca çözülmelidir.
