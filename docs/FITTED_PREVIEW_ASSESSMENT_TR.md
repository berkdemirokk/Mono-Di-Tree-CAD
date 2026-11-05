# Kişisel ölçülü fitted önizleme: entegrasyon değerlendirmesi

Temel: `469c0a9`; araştırma: `fitted-csharp-geometry-1105`, `experiments/garmentcode-fitted-synthetic` (çalışma ağacındaki kaydedilmemiş değişiklikler değerlendirmeye dahil edilmedi).

GarmentCode kaynak lisansı MIT'dir (Maria Korosteleva, 2024); kod veya önemli bölümleri taşınırsa telif ve tam izin bildirimi dağıtılan kopyalarda korunmalıdır. Bağımlılıkların dağıtım incelemesi ayrıca gerekir. Bu değerlendirme uygulamaya kaynak aktarmamıştır.

Araştırma belgesi ve `PINNED_MEASUREMENT_SPEC_TR.md`, insan üzerinde ölçüm protokolü doğrulanmadan gerçek ölçü toplamayı kapalı tutar. Kaynak ölçüm yolları ve işaretlemeleri eksik, kaynak BFGS optimizasyonunun genel eşdeğerliği ve fiziksel prova gösterilmemiştir. İki sentetik örnek üzerindeki karşılaştırma başka bedenlere genellenmez. Bu nedenle 20 kişisel ölçüyle çalışan önizleme açılmamıştır; çıktı `Unverified` / `unresolved` kalır. Sabit sentetik katalogla görsel önizleme ayrı bir araştırma adımı olabilir; altı panel adayı değişik iç türler, bağıl eğri kontrolleri ve dairesel yay temsilinden oluşur. Bunların WPF'ye aktarımında eğri gösteriminin kaynakla eşitliğini ve kopuk/eskimiş çıktıların reddini test etmek gerekir. Bu değerlendirmede derleme ve test yapılmadığı için bu aktarımın doğruluğu kanıtlanmamıştır.

## Gelecekteki izole sözleşme

- Yalnız sabit tasarım, `cm` uzunluk ve `degree` açı; 20 ham alan: `bust`, `back_width`, `waist`, `waist_back_width`, `bust_points`, `shoulder_w`, `shoulder_incl`, `waist_line`, `waist_over_bust_line`, `bust_line`, `vert_bust_line`, `bum_points`, `neck_w`, `armscye_depth`, `hips`, `hip_back_width`, `hips_line`, `height`, `head_l`, `hip_inclination`. Türetilmiş değerler istenmez, tahmin edilmez.
- Adaptör girdi anlık görüntüsünü değişmez tutar; eksik, yinelenmiş, bilinmeyen, yanlış birimli veya kapsam dışı değerleri hesap öncesi yapılandırılmış hatayla reddeder. Teknik sınırlar anatomik onay değildir. Metin ayrıştırma ve ondalık kesinlik ayrıca tanımlanmalıdır.
- Başarılı hesap sadece bellekte, sabit tasarım ve girdi nesline bağlı altı panelin çizgi ve eğrilerini döndürür; her sonuç açıkça `Unverified` ve `unresolved` taşır. Her girdi/tasarım değişiminde eski çizim hemen silinir; hesap tamamlandığında nesil tekrar karşılaştırılır. Hatalı veya eski sonuç hiçbir yerde gösterilmez.
- Önizleme, proje parçası veya üretim belgesine dönüşüm API'si sunmaz; CAD, kaydetme, dışa aktarma, yazdırma ve plotter yolları kapalıdır. Ölçüler, özetler ve tanılar dosyaya, telemetriye veya günlüğe yazılmaz; pencere kapanınca bellekten bırakılır. Özetler de hassastır.
- Gerçek veri arayüzünden önce bağımsız ölçüm protokolü ve tasarım kapsamı incelemesi, gösterimin kaynak aday eğrileriyle karşılaştırılması, anlık görüntü geçersizleştirme ve kaçış yolları testleri gerekir. Sentetik katalog çalışması gerçek kişi verisi için onay oluşturmaz.
