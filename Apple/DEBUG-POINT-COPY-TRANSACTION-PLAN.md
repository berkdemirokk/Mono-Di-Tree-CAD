# DEBUG nokta kopyası için işlemsel kayıt entegrasyonu

Bu belge uygulamaya bağlı bir depolama özelliği değildir. Mevcut dosya tabanlı kopya yolu değişmedi; onun kontrol ile taşıma arasındaki yarışı devam eder. DEBUG koruması ve v6/v7 için 8 MiB sınırı korunmalıdır.

## İncelenen yapı taşı

Bağımsız `42c1a94` revizyonundaki `Apple/SQLitePrototype`, kimlik, ad ve özgün baytları tek SQLite satırına `BEGIN IMMEDIATE` / `COMMIT` ile ekler; aynı kimlikte ekleme hata verir. Hata halinde rollback dener. Bu prototip mevcut uygulama dalına bağlı değildir, mevcut kütüphaneyi okumaz ve kaynak dosyaları silmez. SQLite dosyalarını veya journal'ı değiştirebilen başka bir aktöre karşı koruma sağlamaz; uygulamaya özel dizin ön koşuldur. Salt kayıt atomikliği, belge düzenleme durumunun eskimemesi veya cihaz kapanmasında dayanıklılık kanıtı değildir.

## Önerilen entegrasyon sözleşmesi

1. Yeni DEBUG deposu için uygulamanın özel Application Support dizininde ayrı bir veritabanı ve sürümlenmiş şema oluştur. Eski kütüphane ve dosyalar taşınmaz, silinmez veya değiştirilmez. Veritabanı ve journal birlikte korunur; dosya koruma ve yedekleme davranışı Apple ortamında doğrulanır.
2. Kullanıcı onayıyla mevcut nokta düzenleme işleminin ürettiği baytların bağımsız, değiştirilemeyen anlık görüntüsünü al. 8 MiB sınırını ve mevcut biçim doğrulamasını uygula. Kaynak dosyaya yazma.
3. Belge değişiklikleri ve kayıt onayını tek bir seri oturumda sırala. İşçi hazırlığı bitince aynı oturumda belge kimliği ve revizyonu tekrar kontrol et; farklıysa işlemi reddet. Kontrolden commit sonucuna kadar yeni düzenleme/açma işlemlerini sırala veya devre dışı bırak. Sadece işçi görevinin iptaline güvenme.
4. Yeni UUID, açıklayıcı ad ve bu anlık görüntüyü aynı SQLite işleminde yalnız ekle. Kimlik çakışmasını veya herhangi bir SQLite hatasını başarı olarak gösterme; mevcut kaydı değiştirme. Commit başarılı olmadan kullanıcıya kayıt tamamlandı deme. Hatanın ardından bellekteki düzenlemeyi ve kaynak dosyayı koru.
5. Kütüphane için SQLite kaydını ayrı kimlik türüyle listele ve aynı satırdan oku. Eski dosya kayıtlarıyla kimliği karıştırma. Kurtarma, dışa aktarma, silme ve yeniden adlandırma için ayrıca gözden geçirilmiş akışlar olmadan bu depoyu üretim kütüphanesine bağlama.

## Uygulamadan önce gerekli doğrulama

- Aynı kimliğe eşzamanlı iki eklemede yalnız biri başarılı; önceden var olan veri değişmez.
- Hata enjeksiyonu, disk dolu, kilitli veritabanı, iptal ve commit hatası durumlarında yarım kayıt başarı olarak gösterilmez; kullanıcıdaki düzenleme ve orijinal dosya korunur.
- Hazırlık sırasında başka model açılması veya düzenleme yapılması eski anlık görüntünün yeni modele kaydedilmesine yol açmaz. Commit sırasında sıraya alma ve arayüz durumu gerçek zamanlı sınanır.
- SQLite satırından okunan baytlar, kaydedilmesi onaylanan baytlarla birebir eşittir; metadata ve payload aynı kayda aittir.
- Uygulama sonlandırması, yeniden açma, journal kurtarma, dosya koruma, yedekleme, eşzamanlı erişim ve bellek/performans gerçek iPhone ve uygun Apple geliştirme ortamında sınanır. Simülatör veya bağlantı kapanışı tek başına güç kaybı kanıtı değildir.

Bu koşullar uygulanıp doğrulanana kadar nokta düzenleme kaydı deneysel kalır; işlemsel prototip mevcut staging yarışını kendiliğinden düzeltmez.
