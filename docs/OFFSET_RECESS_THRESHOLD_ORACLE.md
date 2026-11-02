# Dikiş payında girinti kapanma eşiği: bağımsız sentetik kontrol

2 Kasım 2026'da `OffsetRecessThresholdOracleTests` ile 100 × 100 mm boyutunda, 80 mm derinliğinde dik açılı U girintisi ve 5 mm dış dikiş payı incelendi. Kaynak girintinin genişliği `10 mm + g`; ideal dik açılı sınırda iki yan kenar 5'er mm yaklaşır ve `g` genişlik kalır. `g <= 0` ise girinti kapanır. Bu referans, uygulamanın offset algoritmasını veya Clipper'ı hesaplamak için kullanmaz; sekiz sınır köşesi doğrudan koordinatlardan türetilir.

Dokuz `g` değeri (−1 mm, −0,000001 mm, 0, +0,000001 mm, +0,000002 mm, +0,000008 mm, +0,0001 mm, +0,01 mm, +1 mm), iki kontur yönü ve iki başlangıç indeksi denendi: **36 durum**. Eşik altında veya eşikteki 12 durum `OffsetLocalDetailUnverifiedException` ile reddedildi. Pozitif boşluklu 24 durumda uygulama sonuç üretti; tüm referans köşeleri ve üretilen her kenar tam sayı üretim ızgarasında beklenen sınırla eşleşti. Kaynak her durumda değişmedi. Test çıktısı 9/9 parametre kümesi için başarılıydı.

Test, geçerli sonucun ızgara üzerine dönüştürülmüş koordinatlarını karşılaştırır; yalnız bu dik açılı aile ve seçilen değerler için kanıttır. Örneğin en küçük kabul edilen pozitif boşluk **0,000001 mm**'dir; bunun fiziksel kesim/plotter tarafından elde edilebilir olduğu veya insanın kullanabileceği bir dikiş özelliği olduğu kanıtlanmamıştır. Gerçek malzeme, cihaz, eğri, işaret, tüm hata modları veya 512 noktalı loblu konturun reddi değerlendirilmemiştir. Üretim koruması ve mevcut test beklentileri değiştirilmedi.

Tekrarlama: `dotnet test tests/TekstilCAD.Tests/TekstilCAD.Tests.csproj --filter FullyQualifiedName~OffsetRecessThresholdOracleTests --logger "console;verbosity=detailed"`. Veri sentetiktir; WearCAD ve cihaz kullanılmaz.
