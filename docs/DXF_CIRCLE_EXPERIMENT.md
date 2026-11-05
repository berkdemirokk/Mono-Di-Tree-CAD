# Deneysel DXF daire örnekleme çekirdeği

5 Kasım 2026. Bu çalışma yalnız zaten çözülmüş, iki boyutlu `center` ve pozitif `radius` değerlerini kapalı, çok noktalı bir kontur olarak örnekler. `CIRCLE` DXF kaydını okumaz; uygulamanın içe aktarma akışına veya arayüzüne bağlı değildir. Mevcut DXF davranışı değişmemiştir.

Dört ana yön noktası oluşturulur ve her çeyrek yay mevcut deneysel bulge örnekleyicisiyle hesaplanır. Sayısal aralık ve yarıçap kaybı kontrolleri, nokta bütçesi ve iptal uygulanır. Bunlar nominal örnekleme toleransının tüm ondalık dönüşüm ve trigonometrik hataları kapsadığını kanıtlamaz. Testler bilinen merkez ve yarıçaplara göre noktaların yönünü, bütün çevreyi, kiriş orta noktası sapmasını, kontur geometrisini, bütçeyi ve iptali sınar; gerçek DXF dosyası ve fabrika verisi değildir.

Gerçek aktarım için en azından kaynak ondalıkların tekil ve sonlu okunması, OCS/normal ve yükseklik, model/kâğıt alanı, görünürlük ve katman/çizgi tipi anlamı, desteklenmeyen meta veriler, dönüşüm ve blok yerleşimleri, onay ve kaynak geçmişi, toplam kaynak bütçesi, gerçek dosyalar ve kullanıcı arayüzü ayrıca çözülmelidir. Güvenli olmayan veya anlamı belirsiz kayıtlar reddedilmelidir. Örnekleyicinin varlığı `CIRCLE` veya genel DXF/AAMA desteği iddiası değildir.
