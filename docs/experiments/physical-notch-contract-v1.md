# Fiziksel V çentik: deneysel sözleşme taslağı v1

Bu taslak yalnız test projesindedir. `.tcad`, DXF, `NotchMark`, dikiş payı ve makine iş akışları değiştirilmez. Testteki onay bir üretim veya kesin sayısal doğrulama değildir.

Bir kayıt, çentik açılmadan önceki **tek, kapalı, düz kenarlı kaynak kontura** bağlanır. Kaynak parça kimliği; köşelerin sırası ve ikili64 bitlerini içeren kaynak anlık görüntüsü özeti; başlangıç köşesi indeksi ve kenarın iki uç noktası birlikte kaynak kimliğini oluşturur. İndeks tek başına kalıcı kenar kimliği değildir: kontur yeniden sıralanır, düzenlenir veya dönüştürülürse mevcut kayıt bayatlar. Özeti yenilemek otomatik onay değildir; kaynağın yeniden bağlanması, dönüşümün ve sonucun ayrıca doğrulanması gerekir.

Kayıtta açıkça `version=1`, `edgeKind=straight`, yönlü kenar üzerindeki `(0,1)` aralığında oran, kenara göre `left`/`right` yönü, `unit=mm`, pozitif ağız genişliği ve derinliği ile konum, genişlik ve derinlik için ayrı, sıfır veya pozitif mutlak mm toleransları bulunur. Yön, yönlü kenarın solu/sağıdır; v1 yalnız kapalı konturun içine işaret eden yönü inceler. Ağız uçları kenar üzerinde oranla verilen merkezin iki yanında, uç ise seçilen dik doğrultuda derinlik kadar içeridedir. Kayıtla birlikte incelenen üç nokta, bu konuma ve belirtilen boyut toleranslarına uymalıdır.

Eski projede bu kaydın bulunmaması **bilinmiyor** demektir; fiziksel çentik yok demek değildir. `NotchMark` bir köşe görsel işaretidir; ondan çentik derinliği, yönü veya fiziksel bir çentik kaydı üretilmez. Zaten kontura gömülmüş üç noktadan da kayıt otomatik çıkarılmaz.

Eğri kenarlar, yaylar, birden fazla eşleşme, bölünmüş/birleştirilmiş kenar ve ofset sonrası kaynak–aday eşlemesi v1 kapsamında değildir. Eğri için eğri türü, kaynak parametreleme, teğet/normal, yön ve sayısal tolerans sözleşmesi ayrıca tasarlanmalıdır; test kodu `curve` değerini reddeder. Gerçek dönüşümde kaynağın, kenarın, üç noktanın ve yansıma yönünün tutarlı olarak yeniden bağlanması gerekir; testte bunun yalnız sentetik bir örneği vardır.

Test doğrulaması ikili64 hesaplarla örnek üç nokta ölçer; hata sınırları ve derinlik/gösterim yaklaşık olabilir. Çentik geometrisinin bütün konturla kesişmediği, üretim işlemi, fiziksel toleransların uygunluğu, yazıcı/makine davranışı ve saklama uyumluluğu bu deneyle kanıtlanmaz. Uygulama öncesinde sürüm yükseltme, eski dosyalar, geri alma, kullanıcı incelemesi, DXF ve makine sınırları için ayrı tasarım ve test gerekir.
