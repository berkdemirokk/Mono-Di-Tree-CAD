# Renkli geniş arama: eşli görüntü ve geometri deneyi

Tarih: 2026-11-05. Üründeki standart renkli yol (16 piksel pay) ile isteğe bağlı geniş renkli yol (32 piksel pay) aynı 80 ayrılmış görüntüde karşılaştırıldı. Bu görüntüler daha önce başka deneylerde de kullanıldı; yeni ve bağımsız bir dış doğrulama değildir. 40 şort ve 40 tişört görüntüsünün her birinde açıklama sınırından 12 eşit aralıklı referans nokta kullanıldı. İkinci koşulda bu noktalar deterministik olarak 2 piksele kadar kaydırıldı; bunlar gerçek kullanıcı tıklamaları değildir. Her koşulda kapanış dahil 960 aralık çalıştırıldı. 512 × 256 görüntülerde 160.000 piksel sınırına ulaşılıp standart yola dönüş olmadı; büyük görüntüler ve uzun aralıklar için sonuç genellenemez.

| Koşul | Standart ortalama hata | Geniş ortalama hata | Standart / geniş: 5 pikseli aşan yerel hata | Standart / geniş: geçersiz piksel geometrisi |
|---|---:|---:|---:|---:|
| Referans noktaları | 1,36348 px | 1,36354 px | 59 / 59 | 8 / 8 |
| 2 px kaydırılmış | 1,51417 px | 1,50701 px | 61 / 60 | 9 / 9 |

Geçersiz geometriler her iki yöntemde de aynı görüntülere aittir. Geniş arama kaydırılmış koşulda bir görüntüyü 5 piksel eşiğinin altına indirdi, yeni eşik aşımı oluşmadı. Geçerli geometrili sonuçlarda bile referans koşulunda 51/72, kaydırılmış koşulda geniş yöntemde 56/71 görüntüde yerel hata 5 pikseli aştı. Bu eşik üretim toleransı değildir; görüntü pikselleri fiziksel ölçüye çevrilmemiştir. Ortalama, kontur üzerindeki örneklenmiş simetrik en yakın parça mesafesidir; yerel değer örneklenmiş azami mesafedir.

Gerçek ürünün renkli yol sınıflarının kaynak anlık görüntüleriyle çalışıldı ve öneriler ürünün canvas ve piksel geometri kontrolünden geçirildi. Her koşulda 80 kontur için görüntü baytları, 960 işin kimliği ve uç noktaları, ölçüm kimlikleri ve çalışma dosyalarının hash bağları eşleştirildi. [Referans eşleştirme](PHOTO_WIDE_COLOR_HOLDOUT_PAIRING.json) ve [kaydırılmış eşleştirme](PHOTO_WIDE_COLOR_JITTER_PAIRING.json), `PHOTO_COLOR_*` satırları ve özetleri sonuçların denetlenmesine yardımcı olur. Eski deney dosyaları gerekli kaynak bağlarını taşımadığından standart yöntem yeniden çalıştırıldı.

Bu sonuçlar geniş seçeneğin genel doğruluk veya üretim güvenilirliği sağladığını göstermiyor. El ile düzeltme, gerçek kullanıcı fotoğrafları, gerçek tıklamalar, farklı giysi ve arka planlar, ölçek ve fiziksel kalıp doğrulaması hâlâ gereklidir. Hiçbir cihaz işi gönderilmedi.
