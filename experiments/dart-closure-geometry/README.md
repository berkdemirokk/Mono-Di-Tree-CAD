# Sentetik pens kapatma geometrisi

Bu bağımsız C# deneyi, Breanna 4.10.2 kaynağından daha önce üretilmiş, depodaki sentetik ön beden verisini kullanır. Node çalıştırmaz, gerçek ölçü almaz, projeye geometri eklemez ve çıktı üretmez. Fixture içindeki kaynak özetleri yerel kayıtlardır; dosyanın veya çalışma zamanı kaynağının bağımsız kimlik doğrulaması değildir.

Yalnızca `primaryBustDart=06:00`, `secondaryBustDart=none`, arka bel ve omuz pensleri kapalı olan sabit seçenekler için `cfWaist → primaryBustDart1 → primaryBustDartTip → primaryBustDart2 → waist` çizgi zinciri incelenir. Pens ucunun çevresinde ilk bacak ve ona bağlı merkez tarafı döndürülür; diğer bacak ve yan taraf sabit kalır. Döndürülen uçla sabit uç arasındaki boşluk, iki dış çizginin toplamı, açık zincir ve ilk kiriş ayrı raporlanır. Uçlar birleştirilmez veya zorla eşitlenmez. Bu, olası bir geometrik işlemdir; kumaşın nasıl katlanacağı ve gerçek dikilmiş bel çizgisi doğrulanmış değildir.

`ExactBinarySquaredLegsEqual`, verilen binary64 koordinatlardan hesaplanan kareli uzaklıkların tam eşitliğidir. Kaynak verinin ölçüm doğruluğuna dair bir iddia değildir. Döndürme ve boşluk hesapları kayan noktalıdır; sertifikalı hata aralığı sunulmaz. Çağrıcının belirttiği sayısal eşik dikiş toleransı değildir; eşik sağlansa bile dikiş durumu `unresolved`, uygulama ve dışa aktarma kapalıdır. Koordinat ve uzunluk sınırları yalnızca sayısal giriş sınırlarıdır.

Sentetik temel örnekte dış parçalar toplamı yaklaşık 203,5 mm, açık zincir yaklaşık 512,010856 mm, düz başlangıç kirişi yaklaşık 320,877169 mm'dir. Bunlar birbirinin yerine geçmez. Aynı senaryodaki bağımsız alt panelin ön ve arka yarım bel çizgileri sırasıyla 180 ve 195 mm'dir; parçaların ilişkilendirilmesi, üst arka zincir, pens kapatılmış gerçek kontur, bolluk, yön, dikiş payı, kumaş ve prova henüz çözülmedi. Ölçülen farklardan üretim ya da uyum kararı çıkarılamaz.

Doğrulama: `dotnet run --project experiments/dart-closure-geometry/DartClosureGeometry.csproj -- --verify`. Analitik eşit ve eşitsiz bacak örnekleri, dönüş, öteleme, eşik ve hatalı girişler, iki sentetik kaynak zinciri ve mevcut A-line alt paneli sınanır.
